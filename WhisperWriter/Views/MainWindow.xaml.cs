using Autofac.Features.Metadata;
using Microsoft.VisualBasic.ApplicationServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Utils.Interfaces;
// Alias to avoid ambiguity with System.Windows.Forms.Application
using WpfApp = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace WhisperWriter.Views;

/// <summary>
/// Main floating widget window implementing the push-to-talk lifecycle.
/// Handles UI state, coordinates services (Hotkey, AudioRecorder, Whisper, TextInjector)
/// and manages the ETA countdown shown during transcription.
/// </summary>
public partial class MainWindow : Window, IService, ISingleton {
	
	// ETA countdown
	private static readonly uint _etaTimerMiliseconds = 100;
	
	// WAV PCM 16-bit mono 16000 Hz: 16000 samples/s × 2 bytes/sample = 32000 bytes/s
	private static readonly double _wavBytesPerSecond = 32000.0;

	[Inject]
	protected TranscriptionHistory history { get; set; } = null!;
	[Inject]
	protected Services.Whisper whisper { get; set; } = null!;
	[Inject]
	protected EtaCalc etaCalc { get; set; } = null!;
	[Inject]
	protected Log log { get; set; } = null!;
	[Inject]
	protected TextInjector textInjector { get; set; } = null!;
	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected AudioRecorder audioRecorder { get; set; } = null!;
	[Inject]
	protected Hotkey hotkey { get; set; } = null!;

	// ctor injection
	protected Services.MainWindows.Style windowStyle { get; set; } = null!;
	protected Services.MainWindows.Position windowPosition { get; set; } = null!;
	protected IWindowPlatform windowPlatform { get; set; } = new WindowsWindowPlatform();

	// Timer used for updating the ETA label during transcription.
	// Started in handlePush2TalkStopped if an ETA estimate is available,
	// stopped in handleWhisperStateChanged when transcription completes or fails.
	protected DispatcherTimer etaTimer = null!;

	private DateTime _transcribeStarted;
	private double _etaSeconds;

	// True once the initial Loaded positioning has been applied.
	private bool _positionApplied;
	private bool _allowClose = false;
	private bool _initialized = false;
	protected IMainWindowRuntime? runtime { get; set; }
	
	/// <summary>
	/// Constructs the MainWindow and injects required helpers for positioning and style.
	/// The constructor wires up initial event handlers but defers service hookup until OnActivated.
	/// </summary>
	public MainWindow (
		Services.MainWindows.Position windowPosition,
		Services.MainWindows.Style windowStyle
	) {
		this.windowPosition = windowPosition;
		this.windowStyle = windowStyle;

		this.InitializeComponent();
		
		this.windowPosition.SetWindow(this);
		this.windowPosition.InitialPosition(() => this._positionApplied = true);
		
		this.windowStyle.SetWindow(this);
		this.windowStyle.WindowDisplayChange += (_, _) => {
			this.Dispatcher.BeginInvoke(this.handleWindowDisplayChange);
		};

		this.SourceInitialized += this.windowStyle.InitializeSystemEvents!;
		
		this.SizeChanged += this.handleWindowSizeChanged;
	}
	
	private IMainWindowRuntime getRuntime () {
		return this.runtime ??= new MainWindowRuntime(
			this.history,
			this.whisper,
			this.etaCalc,
			this.log,
			this.textInjector,
			this.settings,
			this.audioRecorder,
			this.hotkey
		);
	}

	/// <summary>
	/// Applies a new key combination to the live hotkey service without restarting the app.
	/// Called by App after settings are saved.
	/// </summary>
	/// <summary>
	/// Applies hotkey changes from the settings into the live Hotkey service so the new combo
	/// takes effect immediately without restarting the application.
	/// </summary>
	public void ReloadHotkey () {
		this.getRuntime().UpdateHotkeyKeys(this.getRuntime().Settings.HotkeyCodes);
	}
	
	/// <summary>
	/// Forces application shutdown from the UI, disposing services and closing the window.
	/// Used by the tray menu Exit command to ensure a clean termination.
	/// </summary>
	public void ForceClose () {
		this._allowClose = true;
		this.etaTimer.Stop();
		var runtime = this.getRuntime();
		runtime.DisposeHotkey();
		runtime.DisposeAudioRecorder();
		this.Close();
	}
	
	/// <summary>
	/// Initializes runtime connections to services the first time the window is activated.
	/// Hooks up event handlers for hotkey, audio amplitude and whisper state changes.
	/// </summary>
	protected override void OnActivated (EventArgs e) {
		base.OnActivated(e);

		if (this._initialized)
			return;
		this._initialized = true;

		var runtime = this.getRuntime();
		runtime.StateChanged += this.handleWhisperStateChanged;
		
		var settings = runtime.Settings;
		runtime.SetHotkeyCodes(settings.HotkeyCodes);
		runtime.PushToTalkStarted += this.handlePush2TalkStarted;
		runtime.PushToTalkStopped += this.handlePush2TalkStopped;

		runtime.AmplitudeChanged += this.handleRecordingAmplitudeChanged;

		this.etaTimer = new DispatcherTimer () {
			Interval = TimeSpan.FromMilliseconds(MainWindow._etaTimerMiliseconds)
		};
		this.etaTimer.Tick += this.handleEtaTick;

		runtime.StartHotkey();

		// Show GPU/CPU backend info in status during startup
		var cudaVersion = this.windowPlatform.DetectCudaVersion();
		this.setStatus(cudaVersion.HasValue
			? $"Loading model… (GPU)"
			: "Loading model… (CPU)");

		// Fade in
		this.windowPlatform.BeginStoryboard(this, "FadeIn");
	}
	
	protected override void OnClosing (System.ComponentModel.CancelEventArgs e) {
		if (!this._allowClose) {
			e.Cancel = true;
			this.Hide();
		}
		base.OnClosing(e);
	}

	/// <summary>
	/// Called when the display configuration changes (docking/undocking, resolution change).
	/// Resets the widget to the stored position relative to the new primary screen so it
	/// always appears on the primary monitor after docking.
	/// </summary>
	/// <summary>
	/// Handles display configuration changes by re-applying the stored position or
	/// placing the window at its default fallback position and clamping it to the screen.
	/// </summary>
	protected void handleWindowDisplayChange () {
		var s = this.settings.Data;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0) {
			this.windowPosition.ApplyStoredPosition();
		} else {
			this.windowPosition.PlaceAtDefaultPosition();
		}
		// Clamp in case the restored position is still outside all screens.
		this.windowPosition.ClampWindowToScreen();
	}

	/// <summary>
	/// Called on every SizeChanged event. Re-anchors the window to the stored centre
	/// point so it expands symmetrically. Skipped before the initial Loaded positioning
	/// has been applied (avoids SaveWindowPosition being called with ActualWidth == 0).
	/// </summary>
	/// <summary>
	/// Called on SizeChanged to reposition the window according to the stored centre anchor.
	/// Skip actions until the initial stored position has been applied.
	/// </summary>
	protected void handleWindowSizeChanged (object sender, SizeChangedEventArgs args) {
		if (!this._positionApplied)
			return;
		var s = this.settings.Data;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0) {
			this.windowPosition.ApplyStoredPosition();
		} else {
			this.windowPosition.ClampWindowToScreen();
		}
	}
	
	/// <summary>
	/// Triggers the hover animation when mouse enters the widget.
	/// </summary>
	protected void handleMouseEnter (object sender, System.Windows.Input.MouseEventArgs e) {
		this.windowPlatform.BeginStoryboard(this, "FadeToHover");
	}

	/// <summary>
	/// Triggers the idle animation when mouse leaves the widget.
	/// </summary>
	protected void handleMouseLeave (object sender, System.Windows.Input.MouseEventArgs e) {
		this.windowPlatform.BeginStoryboard(this, "FadeToIdle");
	}
	
	protected void handleMouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) {
		this.windowPlatform.DragMove(this);
		this.windowPosition.SaveWindowPosition();
	}

	/// <summary>
	/// Called when the hotkey press is detected. Saves focus, shows recording UI and starts audio capture.
	/// </summary>
	protected void handlePush2TalkStarted () {
		this.getRuntime().SaveFocus();
		this.Dispatcher.Invoke(() => {
			this.setRecordingState(true);
			this.getRuntime().StartRecording();
		});
	}

	/// <summary>
	/// Called when the hotkey release is detected. Stops recording, optionally starts ETA countdown,
	/// invokes the transcription service and handles injection of the result back to the focused app.
	/// </summary>
	protected void handlePush2TalkStopped () {
		this.Dispatcher.Invoke(async () => {
			var runtime = this.getRuntime();
			var wav = runtime.StopRecording();
			this.setRecordingState(false);

			if (wav == null || wav.Length < 1000)
				return;

			// WAV bytes: 16000 samples/s × 2 bytes = 32000 bytes/s
			double recordedSeconds = wav.Length / MainWindow._wavBytesPerSecond;
			// Returns the model key used for ETA stats lookups (filename without extension).
			var modelKey = System.IO.Path.GetFileNameWithoutExtension(runtime.Settings.ModelPath);
			var estimatedSeconds = runtime.EstimateProcessingSeconds(modelKey, recordedSeconds);
			if (estimatedSeconds.HasValue)
				this.startEtaCountdown(estimatedSeconds.Value);

			var settings = runtime.Settings;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var text = await runtime.TranscribeAsync(
					wav, settings.Language, settings.Prompt);

				this.stopEtaCountdown();

				if (!string.IsNullOrWhiteSpace(text)) {
					runtime.AddHistory(new TranscriptionEntry {
						Text = text,
						Duration = sw.Elapsed,
					});
					runtime.LogTranscription(text, sw.Elapsed);
					if (settings.CopyToClipboard)
						this.windowPlatform.SetClipboardText(text);
					// RestoreFocus must be called on the UI thread (owns message pump).
					// InjectText (_waitForPhysicalRelease + SendInput) runs on a background
					// thread so Thread.Sleep does not block the UI.
					runtime.RestoreFocus();
					_ = runtime.RunBackground(() => {
						runtime.InjectText(text, runtime.Settings.HotkeyCodes);
						// Record total time from recording stop to injection complete.
						runtime.Record(modelKey, recordedSeconds, sw.Elapsed.TotalSeconds);
					}).ContinueWith(t => {
						if (t.IsFaulted)
							runtime.LogError("InjectText failed", t.Exception?.InnerException);
					});
				}
			} catch (Exception ex) {
				this.stopEtaCountdown();
				runtime.LogError("Transcription failed", ex);
				this.setStatus($"Error: {ex.Message}", isError: true);
			}
		});
	}
	
	protected void handleRecordingAmplitudeChanged (float rms) {
		this.Dispatcher.Invoke(() => {
			if (this.AmplitudeRow.Visibility != Visibility.Visible)
				return;
			double maxWidth = this.AmplitudeRow.ActualWidth;
			if (maxWidth <= 0)
				return;
			this.AmplitudeBar.Width = Math.Min(rms * 4.0, 1.0) * maxWidth;
		});
	}

	protected void handleWhisperStateChanged (TranscriptionState state, string msg) {
		this.Dispatcher.Invoke(() => {
			var cudaVer = this.windowPlatform.DetectCudaVersion();
			var backend = cudaVer.HasValue ? $"GPU" : "CPU";
			switch (state) {
				case TranscriptionState.Loading:
					this.setStatus(msg);
					this.RecDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0x00));
					break;
				case TranscriptionState.Transcribing:
					this.setStatus("Transcribing…");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Done:
					this.setStatus($"Ready ({backend})");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Error:
					this.setStatus(msg, isError: true);
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
					break;
				default:
					this.setStatus($"Ready ({backend})");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
			}
		});
	}

	protected void handleEtaTick (object? sender, EventArgs e) {
		this.updateEtaLabel();
	}

	protected void setRecordingState (bool recording) {
		if (recording) {
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
			this.windowPlatform.BeginStoryboard(this, "PulseAnim", controllable: true);
			this.AmplitudeRow.Margin = new Thickness(0, 3, 0, 0);
			this.AmplitudeRow.Visibility = Visibility.Visible;
			this.setStatus("Recording…");
		} else {
			this.windowPlatform.StopStoryboard(this, "PulseAnim");
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
			this.AmplitudeBar.Width = 0;
			this.AmplitudeRow.Visibility = Visibility.Collapsed;
			this.AmplitudeRow.Margin = new Thickness(0);
			this.setStatus("Processing…");
		}
	}

	protected void setStatus (string text, bool isError = false) {
		this.StatusLabel.Text = text;
		this.StatusLabel.Foreground = isError
			? (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"]
			: (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"];
	}
	
	protected void updateEtaLabel () {
		double elapsed = (this.windowPlatform.UtcNow - this._transcribeStarted).TotalSeconds;
		double remaining = this._etaSeconds - elapsed;
		if (remaining < 0)
			remaining = 0;

		int secs = (int)Math.Ceiling(remaining);
		this.EtaLabel.Text = $"~{secs}s";
	}
	
	protected void startEtaCountdown (double estimatedSeconds) {
		this._etaSeconds = Math.Max(estimatedSeconds, 1.0);
		this._transcribeStarted = this.windowPlatform.UtcNow;
		this.EtaLabel.Visibility = Visibility.Visible;
		this.updateEtaLabel();
		this.etaTimer.Start();
	}

	protected void stopEtaCountdown () {
		this.etaTimer.Stop();
		this.EtaLabel.Visibility = Visibility.Collapsed;
	}

}
