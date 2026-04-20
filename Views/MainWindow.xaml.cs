using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WhisperWriter.Util;
using WhisperWriter.Util.Enums;
using WhisperWriter.Services;


// Alias to avoid ambiguity with System.Windows.Forms.Application
using WpfApp = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace WhisperWriter.Views;

public partial class MainWindow : Window {
	private readonly AudioRecorder _recorder = new();
	private readonly HotkeyService _hotkey;
	private readonly WindowPositioner _positioner;
	private bool _allowClose;

	// ETA countdown
	private readonly DispatcherTimer _etaTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
	private DateTime _transcribeStarted;
	private double _etaSeconds;

	// True once the initial Loaded positioning has been applied.
	private bool _positionApplied;

	/// <summary>Returns the model key used for ETA stats lookups (filename without extension).</summary>
	private static string GetModelKey () =>
		System.IO.Path.GetFileNameWithoutExtension(App.SettingsService.Settings.ModelPath);

	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_TOOLWINDOW = 0x00000080;
	private const int WS_EX_APPWINDOW = 0x00040000;
	private const int WM_DISPLAYCHANGE = 0x007E;
	// WAV PCM 16-bit mono 16000 Hz: 16000 samples/s × 2 bytes/sample = 32000 bytes/s
	private const double WavBytesPerSecond = 32000.0;

	[DllImport("user32.dll")]
	private static extern int GetWindowLong (IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong (IntPtr hwnd, int index, int newStyle);

	public MainWindow () {
		this.InitializeComponent();
		this._positioner = new WindowPositioner(this);
		this._positioner.InitialPosition(() => this._positionApplied = true);
		this.SourceInitialized += this._onSourceInitialized;

		var settings = App.SettingsService.Settings;
		this._hotkey = new HotkeyService(settings.HotkeyVkCodes);
		this._hotkey.PushToTalkStarted += this._onPttStarted;
		this._hotkey.PushToTalkStopped += this._onPttStopped;
		this._recorder.AmplitudeChanged += this._onAmplitude;

		App.WhisperService.StateChanged += this._onWhisperState;

		this._etaTimer.Tick += this._onEtaTick;

		this._hotkey.Start();

		// Show GPU/CPU backend info in status during startup
		var cudaVersion = WhisperService.DetectCudaVersion();
		this._setStatus(cudaVersion.HasValue
			? $"Loading model… (GPU)"
			: "Loading model… (CPU)");

		// Fade in
		var anim = (Storyboard)this.Resources["FadeIn"];
		anim.Begin(this);
	}

	private void _onSourceInitialized (object? sender, EventArgs e) {
		var hwnd = new WindowInteropHelper(this).Handle;
		int style = GetWindowLong(hwnd, GWL_EXSTYLE);
		style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
		SetWindowLong(hwnd, GWL_EXSTYLE, style);
		HwndSource.FromHwnd(hwnd)?.AddHook(this.WndProc);
		SizeChanged += (_, _) => this._onWindowSizeChanged();
	}

	private IntPtr WndProc (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_DISPLAYCHANGE)
			this.Dispatcher.BeginInvoke(this._onDisplayChange);
		return IntPtr.Zero;
	}

	/// <summary>
	/// Called on every SizeChanged event. Re-anchors the window to the stored centre
	/// point so it expands symmetrically. Skipped before the initial Loaded positioning
	/// has been applied (avoids SaveWindowPosition being called with ActualWidth == 0).
	/// </summary>
	private void _onWindowSizeChanged () {
		if (!this._positionApplied)
			return;
		var s = App.SettingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0)
			this._positioner.ApplyStoredPosition();
		else
			this._positioner.ClampWindowToScreen();
	}

	/// <summary>
	/// Called when the display configuration changes (docking/undocking, resolution change).
	/// Resets the widget to the stored position relative to the new primary screen so it
	/// always appears on the primary monitor after docking.
	/// </summary>
	private void _onDisplayChange () {
		var s = App.SettingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0)
			this._positioner.ApplyStoredPosition();
		else
			this._positioner.PlaceAtDefaultPosition();
		// Clamp in case the restored position is still outside all screens.
		this._positioner.ClampWindowToScreen();
	}

	private void _border_MouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) {
		this.DragMove();
		this._positioner.SaveWindowPosition();
	}

	private void _onPttStarted () {
		TextInjector.SaveFocus();

		this.Dispatcher.Invoke(() => {
			this._setRecordingState(true);
			this._recorder.StartRecording();
		});
	}

	private void _onPttStopped () {
		this.Dispatcher.Invoke(async () => {
			var wav = this._recorder.StopRecording();
			this._setRecordingState(false);

			if (wav == null || wav.Length < 1000)
				return;

			// WAV bytes: 16000 samples/s × 2 bytes = 32000 bytes/s
			double recordedSeconds = wav.Length / MainWindow.WavBytesPerSecond;
			var modelKey = GetModelKey();
			var estimatedSeconds = App.Eta.EstimateProcessingSeconds(modelKey, recordedSeconds);
			if (estimatedSeconds.HasValue)
				this._startEtaCountdown(estimatedSeconds.Value);

			var settings = App.SettingsService.Settings;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var text = await App.WhisperService.TranscribeAsync(
					wav, settings.Language, settings.Prompt);

				this._stopEtaCountdown();

				if (!string.IsNullOrWhiteSpace(text)) {
					App.History.Add(new TranscriptionEntry {
						Text = text,
						Duration = sw.Elapsed,
					});
					LogService.Transcription(text, sw.Elapsed);
					if (settings.CopyToClipboard)
						System.Windows.Clipboard.SetText(text);
					// RestoreFocus must be called on the UI thread (owns message pump).
					// InjectText (_waitForPhysicalRelease + SendInput) runs on a background
					// thread so Thread.Sleep does not block the UI.
					TextInjector.RestoreFocus();
					_ = Task.Run(() => {
						TextInjector.InjectText(text, App.SettingsService.Settings.HotkeyVkCodes);
						// Record total time from recording stop to injection complete.
						App.Eta.Record(modelKey, recordedSeconds, sw.Elapsed.TotalSeconds);
					}).ContinueWith(t => {
						if (t.IsFaulted)
							LogService.Error("InjectText failed", t.Exception?.InnerException);
					});
				}
			} catch (Exception ex) {
				this._stopEtaCountdown();
				LogService.Error("Transcription failed", ex);
				this._setStatus($"Error: {ex.Message}", isError: true);
			}
		});
	}

	private void _startEtaCountdown (double estimatedSeconds) {
		this._etaSeconds = Math.Max(estimatedSeconds, 1.0);
		this._transcribeStarted = DateTime.UtcNow;
		this.EtaLabel.Visibility = Visibility.Visible;
		this._updateEtaLabel();
		this._etaTimer.Start();
	}

	private void _stopEtaCountdown () {
		this._etaTimer.Stop();
		this.EtaLabel.Visibility = Visibility.Collapsed;
	}

	private void _onEtaTick (object? sender, EventArgs e) {
		this._updateEtaLabel();
	}

	private void _updateEtaLabel () {
		double elapsed = (DateTime.UtcNow - this._transcribeStarted).TotalSeconds;
		double remaining = this._etaSeconds - elapsed;
		if (remaining < 0)
			remaining = 0;

		int secs = (int)Math.Ceiling(remaining);
		this.EtaLabel.Text = $"~{secs}s";
	}

	private void _onAmplitude (float rms) {
		this.Dispatcher.Invoke(() => {
			if (this.AmplitudeRow.Visibility != Visibility.Visible)
				return;
			double maxWidth = this.AmplitudeRow.ActualWidth;
			if (maxWidth <= 0)
				return;
			this.AmplitudeBar.Width = Math.Min(rms * 4.0, 1.0) * maxWidth;
		});
	}

	private void _onWhisperState (TranscriptionState state, string msg) {
		this.Dispatcher.Invoke(() => {
			var cudaVer = WhisperService.DetectCudaVersion();
			var backend = cudaVer.HasValue ? $"GPU" : "CPU";
			switch (state) {
				case TranscriptionState.Loading:
					this._setStatus(msg);
					this.RecDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xCC, 0x00));
					break;
				case TranscriptionState.Transcribing:
					this._setStatus("Transcribing…");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Done:
					this._setStatus($"Ready ({backend})");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Error:
					this._setStatus(msg, isError: true);
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
					break;
				default:
					this._setStatus($"Ready ({backend})");
					this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
			}
		});
	}

	private void _setRecordingState (bool recording) {
		var pulse = (Storyboard)this.Resources["PulseAnim"];
		if (recording) {
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
			pulse.Begin(this, true);
			this.AmplitudeRow.Margin = new Thickness(0, 3, 0, 0);
			this.AmplitudeRow.Visibility = Visibility.Visible;
			this._setStatus("Recording…");
		} else {
			pulse.Stop(this);
			this.RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
			this.AmplitudeBar.Width = 0;
			this.AmplitudeRow.Visibility = Visibility.Collapsed;
			this.AmplitudeRow.Margin = new Thickness(0);
			this._setStatus("Processing…");
		}
	}

	private void _setStatus (string text, bool isError = false) {
		this.StatusLabel.Text = text;
		this.StatusLabel.Foreground = isError
			? (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"]
			: (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"];
	}

	private void _widgetBorder_MouseEnter (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)this.Resources["FadeToHover"];
		anim.Begin(this);
	}

	private void _widgetBorder_MouseLeave (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)this.Resources["FadeToIdle"];
		anim.Begin(this);
	}

	/// <summary>
	/// Applies a new key combination to the live hotkey service without restarting the app.
	/// Called by App after settings are saved.
	/// </summary>
	public void ReloadHotkey () {
		this._hotkey.UpdateKeys(App.SettingsService.Settings.HotkeyVkCodes);
	}

	protected override void OnClosing (System.ComponentModel.CancelEventArgs e) {
		if (!this._allowClose) {
			e.Cancel = true;
			this.Hide();
		}
		base.OnClosing(e);
	}

	public void ForceClose () {
		this._allowClose = true;
		this._etaTimer.Stop();
		this._hotkey.Dispose();
		this._recorder.Dispose();
		this.Close();
	}
}
