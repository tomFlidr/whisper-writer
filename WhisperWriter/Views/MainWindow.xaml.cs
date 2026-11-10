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
using WhisperWriter.Services.TextInjectors;
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
	[Inject]
	protected TranscriptionQueue transcriptionQueue { get; set; } = null!;

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
	private RecordingContext? _recordingContext;
	private bool _recordingRejected;
	private bool _isRecording;
	private long _recordingSequence;
	private readonly object _resultPipelineLock = new();
	private Task _resultPipeline = Task.CompletedTask;
	private int _pendingInjections;

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
			this.hotkey,
			this.transcriptionQueue
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
		runtime.QueueStatusChanged += this.handleQueueStatusChanged;
		runtime.JobCompleted += this.handleJobCompleted;
		
		var settings = runtime.Settings;
		runtime.SetHotkeyCodes(settings.HotkeyCodes);
		runtime.PushToTalkStarted += this.handlePush2TalkStarted;
		runtime.PushToTalkStopped += this.handlePush2TalkStopped;

		runtime.AmplitudeChanged += this.handleRecordingAmplitudeChanged;

		this.etaTimer = new DispatcherTimer () {
			Interval = TimeSpan.FromMilliseconds(MainWindow._etaTimerMiliseconds)
		};
		this.etaTimer.Tick += this.handleEtaTick;
		this.etaTimer.Interval = TimeSpan.FromSeconds(1);
		this.handleQueueStatusChanged(runtime.GetQueueStatus());

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
	/// Called when the hotkey press is detected. Reserves queue space and snapshots the destination/settings
	/// before starting a fresh audio capture.
	/// </summary>
	protected void handlePush2TalkStarted () {
		var runtime = this.getRuntime();
		FocusTarget target = runtime.CaptureFocus();
		this.Dispatcher.Invoke(() => {
			if (this._isRecording)
				return;
			if (!runtime.TryReserveRecording()) {
				this._recordingRejected = true;
				this.setStatus("Busy · transcription queue full");
				this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"];
				return;
			}

			AppSettings settings = runtime.Settings;
			this._recordingContext = new RecordingContext(
				target,
				settings.Language,
				settings.Prompt,
				settings.ModelPath,
				settings.HotkeyCodes.ToArray(),
				settings.CopyToClipboard,
				new DateTimeOffset(this.windowPlatform.UtcNow),
				Interlocked.Increment(ref this._recordingSequence));
			try {
				this.setRecordingState(true);
				runtime.StartRecording();
			} catch (Exception exception) {
				this._recordingContext = null;
				this.setRecordingState(false);
				runtime.CancelRecordingReservation();
				runtime.LogError("Recording start failed", exception);
				this.setStatus($"Error: {exception.Message}", isError: true);
			}
		});
	}

	/// <summary>
	/// Called when the hotkey release is detected. Stops recording and hands the immutable batch to the
	/// inference queue; the UI thread never waits for transcription.
	/// </summary>
	protected void handlePush2TalkStopped () {
		var runtime = this.getRuntime();
		RecordingContext? context = null;
		byte[]? wav = null;
		this.Dispatcher.Invoke(() => {
			if (this._recordingRejected) {
				this._recordingRejected = false;
				this.updateQueueUi(runtime.GetQueueStatus());
				return;
			}
			context = this._recordingContext;
			this._recordingContext = null;
			if (context is null)
				return;
			wav = runtime.StopRecording();
			this.setRecordingState(false);
		});

		if (context is null)
			return;
		if (wav is null || wav.Length < 1000) {
			runtime.CancelRecordingReservation();
			this.Dispatcher.Invoke(() => this.updateQueueUi(runtime.GetQueueStatus()));
			return;
		}

		try {
			double recordedSeconds = wav.Length / MainWindow._wavBytesPerSecond;
			string modelKey = System.IO.Path.GetFileNameWithoutExtension(context.ModelPath);
			var job = new TranscriptionJob {
				Id = Guid.NewGuid(),
				Sequence = context.Sequence,
				CapturedAt = context.CapturedAt,
				AudioBytes = wav,
				Target = context.Target,
				Language = context.Language,
				Prompt = context.Prompt,
				ModelPath = context.ModelPath,
				ModelKey = modelKey,
				HotkeyCodes = context.HotkeyCodes,
				CopyToClipboard = context.CopyToClipboard,
				AudioSeconds = recordedSeconds,
				EstimatedProcessingSeconds = runtime.EstimateProcessingSeconds(modelKey, recordedSeconds),
			};
			runtime.EnqueueTranscription(job);
			this.Dispatcher.Invoke(() => this.updateQueueUi(runtime.GetQueueStatus()));
		} catch (Exception exception) {
			runtime.CancelRecordingReservation();
			runtime.LogError("Failed to queue transcription", exception);
			this.Dispatcher.BeginInvoke(() => this.setStatus($"Error: {exception.Message}", isError: true));
		}
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

	protected void handleQueueStatusChanged (TranscriptionQueueStatus status) {
		if (this.Dispatcher.CheckAccess()) {
			this.updateQueueUi(status);
			return;
		}
		this.Dispatcher.BeginInvoke(() => this.updateQueueUi(status));
	}

	protected void handleJobCompleted (TranscriptionJobResult result) {
		Interlocked.Increment(ref this._pendingInjections);
		lock (this._resultPipelineLock)
			this._resultPipeline = this.processJobAfterAsync(this._resultPipeline, result);
	}

	private async Task processJobAfterAsync (Task previous, TranscriptionJobResult result) {
		try { await previous.ConfigureAwait(false); } catch { }
		await this.processJobResultAsync(result).ConfigureAwait(false);
	}

	private async Task processJobResultAsync (TranscriptionJobResult result) {
		var runtime = this.getRuntime();
		bool preserveErrorStatus = result.Error is not null;
		try {
			if (result.Error is not null) {
				runtime.LogError("Transcription failed", result.Error);
				this.Dispatcher.Invoke(() => {
					TranscriptionQueueStatus status = runtime.GetQueueStatus();
					if (status.ActiveCount == 0 && status.QueuedCount == 0)
						this.setStatus($"Error: {result.Error.Message}", isError: true);
					else
						this.updateQueueUi(status);
				});
				return;
			}

			string? text = result.Text;
			if (string.IsNullOrWhiteSpace(text))
				return;

			this.Dispatcher.Invoke(() => {
				runtime.AddHistory(new TranscriptionEntry {
					Timestamp = result.Job.CapturedAt.LocalDateTime,
					Text = text,
					Duration = result.ProcessingTime,
				});
				runtime.LogTranscription(text, result.ProcessingTime);
				if (result.Job.CopyToClipboard)
					this.windowPlatform.SetClipboardText(text);
				// Restore the captured target on the UI thread before background SendInput.
				runtime.RestoreFocus(result.Job.Target);
			});
			await runtime.RunBackground(() => {
				runtime.InjectText(text, result.Job.HotkeyCodes, result.Job.Target);
				runtime.Record(result.Job.ModelKey, result.Job.AudioSeconds, result.ProcessingTime.TotalSeconds);
			});
		} catch (Exception exception) {
			preserveErrorStatus = true;
			runtime.LogError("InjectText failed", exception);
			this.Dispatcher.Invoke(() => this.setStatus($"Error: {exception.Message}", isError: true));
		} finally {
			Interlocked.Decrement(ref this._pendingInjections);
			TranscriptionQueueStatus status = runtime.GetQueueStatus();
			if (!preserveErrorStatus || status.ActiveCount > 0 || status.QueuedCount > 0 || Volatile.Read(ref this._pendingInjections) > 0)
				this.Dispatcher.Invoke(() => this.updateQueueUi(status));
		}
	}

	protected void handleWhisperStateChanged (TranscriptionState state, string msg) {
		this.Dispatcher.Invoke(() => {
			switch (state) {
				case TranscriptionState.Loading:
					this.setStatus(msg);
					this.RecDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0x00));
					break;
				case TranscriptionState.Error:
					this.setStatus(msg, isError: true);
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
					break;
				default:
					this.updateQueueUi(this.getRuntime().GetQueueStatus());
					break;
			}
		});
	}

	private TranscriptionQueueStatus _queueStatus = new(0, 0, 0, 0, 0, 2, Array.Empty<TimeSpan?>(), 0);

	private void updateQueueUi (TranscriptionQueueStatus status) {
		this._queueStatus = status;
		if (this._isRecording)
			return;

		int pendingInjections = Volatile.Read(ref this._pendingInjections);
		int maxOutstandingJobs = Math.Max(1, this.getRuntime().Settings.MaxOutstandingTranscriptionJobs);
		bool admissionPaused = status.AdmissionPaused || status.BufferedAudioBudgetBytes <= 0 ||
			status.BufferedAudioBytes >= status.BufferedAudioBudgetBytes || status.OutstandingCount >= maxOutstandingJobs;
		bool hasWork = status.ActiveCount > 0 || status.QueuedCount > 0 ||
			status.OutstandingCount > 0 || pendingInjections > 0 || admissionPaused;
		if (!hasWork) {
			this.stopEtaCountdown();
			string backend = this.getRuntime().ActiveProvider ??
				(this.windowPlatform.DetectCudaVersion().HasValue ? "GPU" : "CPU");
			this.setStatus($"Ready ({backend})");
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
			return;
		}

		var statusParts = new List<string>();
		if (status.ActiveCount > 0)
			statusParts.Add($"Transcribing {status.ActiveCount}");
		if (status.QueuedCount > 0)
			statusParts.Add($"Queued {status.QueuedCount}");
		if (pendingInjections > 0)
			statusParts.Add($"Inserting {pendingInjections}");
		if (admissionPaused)
			statusParts.Add("Paused");
		this.setStatus(statusParts.Count > 0 ? string.Join(" · ", statusParts) : "Processing…");

		var etaParts = status.NextResultReadyEtas.Select(eta => eta.HasValue
			? $"~{Math.Max(0, (int)Math.Ceiling(eta.Value.TotalSeconds))}s"
			: "—").ToList();
		if (status.AdditionalEtaCount > 0)
			etaParts.Add($"+{status.AdditionalEtaCount}");
		this.EtaLabel.Text = string.Join("; ", etaParts);
		this.EtaLabel.Visibility = etaParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		if (status.ActiveCount > 0 || status.QueuedCount > 0)
			this.etaTimer.Start();
		else if (etaParts.Count == 0)
			this.etaTimer.Stop();
		this.RecDot.Fill = admissionPaused
			? (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"]
			: (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
	}

	protected void handleEtaTick (object? sender, EventArgs e) {
		if (this._queueStatus.ActiveCount > 0 || this._queueStatus.QueuedCount > 0)
			this.updateQueueUi(this.getRuntime().GetQueueStatus());
		else
			this.updateEtaLabel();
	}

	protected void setRecordingState (bool recording) {
		this._isRecording = recording;
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

	private sealed record RecordingContext (
		FocusTarget Target,
		string Language,
		string Prompt,
		string ModelPath,
		IReadOnlyList<int> HotkeyCodes,
		bool CopyToClipboard,
		DateTimeOffset CapturedAt,
		long Sequence
	);

}
