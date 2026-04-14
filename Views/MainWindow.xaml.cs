using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WhisperWriter.Models;
using WhisperWriter.Services;

// Alias to avoid ambiguity with System.Windows.Forms.Application
using WpfApp = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace WhisperWriter.Views;

public partial class MainWindow : Window {
	private readonly AudioRecorder _recorder = new();
	private readonly HotkeyService _hotkey;
	private bool _allowClose;

	// ETA countdown
	private readonly DispatcherTimer _etaTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
	private DateTime _transcribeStarted;
	private double _etaSeconds;

	/// <summary>
	/// Empirical factor: transcription time ≈ recording length × EtaFactor.
	/// Tune based on GPU / model. 0.35 = large-v2 on Quadro T2000 CUDA.
	/// </summary>
	private const double EtaFactor = 0.35;

	public MainWindow () {
		InitializeComponent();
		PositionWindow();

		var settings = App.SettingsService.Settings;
		_hotkey = new HotkeyService((HotkeyModifiers)settings.HotkeyModifiers);
		_hotkey.PushToTalkStarted += OnPttStarted;
		_hotkey.PushToTalkStopped += OnPttStopped;
		_recorder.AmplitudeChanged += OnAmplitude;

		App.WhisperService.StateChanged += OnWhisperState;

		_etaTimer.Tick += OnEtaTick;

		_hotkey.Start();

		// Fade in
		var anim = (Storyboard)Resources["FadeIn"];
		anim.Begin(this);
	}

	private void PositionWindow () {
		var s = App.SettingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowTop >= 0) {
			Left = s.WindowLeft;
			Top = s.WindowTop;
		} else {
			// Default: bottom-center of primary screen
			Left = (SystemParameters.PrimaryScreenWidth - 280) / 2;
			Top = SystemParameters.PrimaryScreenHeight - 80;
		}
	}

	// ── Drag to reposition ───────────────────────────────────────────────────
	private void Border_MouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) {
		DragMove();
		App.SettingsService.Settings.WindowLeft = Left;
		App.SettingsService.Settings.WindowTop = Top;
		App.SettingsService.Save();
	}

	// ── Push-to-talk ─────────────────────────────────────────────────────────
	private void OnPttStarted () {
		TextInjector.SaveFocus();

		Dispatcher.Invoke(() => {
			SetRecordingState(true);
			_recorder.StartRecording();
		});
	}

	private void OnPttStopped () {
		Dispatcher.Invoke(async () => {
			var wav = _recorder.StopRecording();
			SetRecordingState(false);

			if (wav == null || wav.Length < 1000)
				return;

			// Estimate ETA from recorded audio length
			// WAV bytes: 16000 samples/s × 2 bytes = 32000 bytes/s
			double recordedSeconds = wav.Length / 32000.0;
			StartEtaCountdown(recordedSeconds * EtaFactor);

			var settings = App.SettingsService.Settings;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var text = await App.WhisperService.TranscribeAsync(
					wav, settings.Language, settings.Prompt);
				sw.Stop();

				StopEtaCountdown();

				if (!string.IsNullOrWhiteSpace(text)) {
					App.History.Add(new TranscriptionEntry {
						Text = text,
						Duration = sw.Elapsed,
					});
					TextInjector.InjectText(text);
				}
			} catch (Exception ex) {
				StopEtaCountdown();
				LogService.Error("Transcription failed", ex);
				SetStatus($"Error: {ex.Message}", isError: true);
			}
		});
	}

	// ── ETA countdown ─────────────────────────────────────────────────────────
	private void StartEtaCountdown (double estimatedSeconds) {
		_etaSeconds = Math.Max(estimatedSeconds, 1.0);
		_transcribeStarted = DateTime.UtcNow;
		EtaLabel.Visibility = Visibility.Visible;
		UpdateEtaLabel();
		_etaTimer.Start();
	}

	private void StopEtaCountdown () {
		_etaTimer.Stop();
		EtaLabel.Visibility = Visibility.Collapsed;
	}

	private void OnEtaTick (object? sender, EventArgs e) {
		UpdateEtaLabel();
	}

	private void UpdateEtaLabel () {
		double elapsed = (DateTime.UtcNow - _transcribeStarted).TotalSeconds;
		double remaining = _etaSeconds - elapsed;
		if (remaining < 0)
			remaining = 0;

		int secs = (int)Math.Ceiling(remaining);
		EtaLabel.Text = $"~{secs}s";
	}

	// ── Amplitude VU meter ───────────────────────────────────────────────────
	private void OnAmplitude (float rms) {
		Dispatcher.Invoke(() => {
			double maxWidth = AmplitudeRow.ActualWidth;
			if (maxWidth <= 0)
				maxWidth = 160;
			double barWidth = Math.Min(rms * 4.0, 1.0) * maxWidth;
			AmplitudeBar.Width = barWidth;
		});
	}

	// ── Whisper state callback ────────────────────────────────────────────────
	private void OnWhisperState (TranscriptionState state, string msg) {
		Dispatcher.Invoke(() => {
			switch (state) {
				case TranscriptionState.Loading:
					SetStatus(msg);
					RecDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0x00));
					break;
				case TranscriptionState.Transcribing:
					SetStatus("Transcribing…");
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Done:
					SetStatus("Ready  —  hold Ctrl+Win");
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Error:
					SetStatus(msg, isError: true);
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
					break;
				default:
					SetStatus("Ready  —  hold Ctrl+Win");
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
			}
		});
	}

	// ── Helpers ───────────────────────────────────────────────────────────────
	private void SetRecordingState (bool recording) {
		var pulse = (Storyboard)Resources["PulseAnim"];
		if (recording) {
			RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
			pulse.Begin(this, true);
			AmplitudeRow.Visibility = Visibility.Visible;
			SetStatus("Recording…");
		} else {
			pulse.Stop(this);
			RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
			AmplitudeRow.Visibility = Visibility.Collapsed;
			SetStatus("Processing…");
		}
	}

	private void SetStatus (string text, bool isError = false) {
		StatusLabel.Text = text;
		StatusLabel.Foreground = isError
			? (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"]
			: (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"];
	}

	// ── Window lifecycle ──────────────────────────────────────────────────────
	protected override void OnClosing (System.ComponentModel.CancelEventArgs e) {
		if (!_allowClose) {
			e.Cancel = true;
			Hide();
		}
		base.OnClosing(e);
	}

	public void ForceClose () {
		_allowClose = true;
		_etaTimer.Stop();
		_hotkey.Dispose();
		_recorder.Dispose();
		Close();
	}
}
