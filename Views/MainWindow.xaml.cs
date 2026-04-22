using Autofac.Features.Metadata;
using Microsoft.VisualBasic.ApplicationServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

	// Timer used for updating the ETA label during transcription.
	// Started in handlePush2TalkStopped if an ETA estimate is available,
	// stopped in handleWhisperStateChanged when transcription completes or fails.
	protected DispatcherTimer etaTimer = null!;

	private DateTime _transcribeStarted;
	private double _etaSeconds;

	// True once the initial Loaded positioning has been applied.
	private bool _positionApplied;
	private bool _allowClose = false;
	
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
	
	/// <summary>
	/// Applies a new key combination to the live hotkey service without restarting the app.
	/// Called by App after settings are saved.
	/// </summary>
	public void ReloadHotkey () {
		this.hotkey.UpdateKeys(this.settings.Data.HotkeyCodes);
	}
	
	public void ForceClose () {
		this._allowClose = true;
		this.etaTimer.Stop();
		this.hotkey.Dispose();
		this.audioRecorder.Dispose();
		this.Close();
	}
	
	protected override void OnActivated (EventArgs e) {
		base.OnActivated(e);
		
		this.whisper.StateChanged += this.handleWhisperStateChanged;
		
		var settings = this.settings.Data;
		this.hotkey.SetVirtualKeyCodes(settings.HotkeyCodes);
		this.hotkey.Push2TalkStarted += this.handlePush2TalkStarted;
		this.hotkey.Push2TalkStopped += this.handlePush2TalkStopped;

		this.audioRecorder.AmplitudeChanged += this.handleRecordingAmplitudeChanged;

		//this.whisper.StateChanged += this.handleWhisperStateChanged;

		this.etaTimer = new DispatcherTimer () {
			Interval = TimeSpan.FromMilliseconds(MainWindow._etaTimerMiliseconds)
		};
		this.etaTimer.Tick += this.handleEtaTick;

		this.hotkey.Start();

		// Show GPU/CPU backend info in status during startup
		var cudaVersion = Services.Whisper.DetectCudaVersion();
		this.setStatus(cudaVersion.HasValue
			? $"Loading model… (GPU)"
			: "Loading model… (CPU)");

		// Fade in
		var anim = (Storyboard)this.Resources["FadeIn"];
		anim.Begin(this);
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
	
	protected void handleMouseEnter (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)this.Resources["FadeToHover"];
		anim.Begin(this);
	}

	protected void handleMouseLeave (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)this.Resources["FadeToIdle"];
		anim.Begin(this);
	}
	
	protected void handleMouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) {
		this.DragMove();
		this.windowPosition.SaveWindowPosition();
	}

	protected void handlePush2TalkStarted () {
		this.textInjector.SaveFocus();
		this.Dispatcher.Invoke(() => {
			this.setRecordingState(true);
			this.audioRecorder.StartRecording();
		});
	}

	protected void handlePush2TalkStopped () {
		this.Dispatcher.Invoke(async () => {
			var wav = this.audioRecorder.StopRecording();
			this.setRecordingState(false);

			if (wav == null || wav.Length < 1000)
				return;

			// WAV bytes: 16000 samples/s × 2 bytes = 32000 bytes/s
			double recordedSeconds = wav.Length / MainWindow._wavBytesPerSecond;
			// Returns the model key used for ETA stats lookups (filename without extension).
			var modelKey = System.IO.Path.GetFileNameWithoutExtension(this.settings.Data.ModelPath);
			var estimatedSeconds = this.etaCalc.EstimateProcessingSeconds(modelKey, recordedSeconds);
			if (estimatedSeconds.HasValue)
				this.startEtaCountdown(estimatedSeconds.Value);

			var settings = this.settings.Data;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var text = await this.whisper.TranscribeAsync(
					wav, settings.Language, settings.Prompt);

				this.stopEtaCountdown();

				if (!string.IsNullOrWhiteSpace(text)) {
					this.history.Add(new TranscriptionEntry {
						Text = text,
						Duration = sw.Elapsed,
					});
					this.log.Transcription(text, sw.Elapsed);
					if (settings.CopyToClipboard)
						System.Windows.Clipboard.SetText(text);
					// RestoreFocus must be called on the UI thread (owns message pump).
					// InjectText (_waitForPhysicalRelease + SendInput) runs on a background
					// thread so Thread.Sleep does not block the UI.
					this.textInjector.RestoreFocus();
					_ = Task.Run(() => {
						this.textInjector.InjectText(text, this.settings.Data.HotkeyCodes);
						// Record total time from recording stop to injection complete.
						this.etaCalc.Record(modelKey, recordedSeconds, sw.Elapsed.TotalSeconds);
					}).ContinueWith(t => {
						if (t.IsFaulted)
							this.log.Error("InjectText failed", t.Exception?.InnerException);
					});
				}
			} catch (Exception ex) {
				this.stopEtaCountdown();
				this.log.Error("Transcription failed", ex);
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
			var cudaVer = Services.Whisper.DetectCudaVersion();
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
		var pulse = (Storyboard)this.Resources["PulseAnim"];
		if (recording) {
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
			pulse.Begin(this, true);
			this.AmplitudeRow.Margin = new Thickness(0, 3, 0, 0);
			this.AmplitudeRow.Visibility = Visibility.Visible;
			this.setStatus("Recording…");
		} else {
			pulse.Stop(this);
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
		double elapsed = (DateTime.UtcNow - this._transcribeStarted).TotalSeconds;
		double remaining = this._etaSeconds - elapsed;
		if (remaining < 0)
			remaining = 0;

		int secs = (int)Math.Ceiling(remaining);
		this.EtaLabel.Text = $"~{secs}s";
	}
	
	protected void startEtaCountdown (double estimatedSeconds) {
		this._etaSeconds = Math.Max(estimatedSeconds, 1.0);
		this._transcribeStarted = DateTime.UtcNow;
		this.EtaLabel.Visibility = Visibility.Visible;
		this.updateEtaLabel();
		this.etaTimer.Start();
	}

	protected void stopEtaCountdown () {
		this.etaTimer.Stop();
		this.EtaLabel.Visibility = Visibility.Collapsed;
	}

}
