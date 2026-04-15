using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WhisperWriter.Util;
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

	// True once the initial Loaded positioning has been applied.
	private bool _positionApplied;

	/// <summary>Returns the model key used for ETA stats lookups (filename without extension).</summary>
	private static string GetModelKey () =>
		System.IO.Path.GetFileNameWithoutExtension(App.SettingsService.Settings.ModelPath);

	private const int GWL_EXSTYLE = -20;
	private const int WS_EX_TOOLWINDOW = 0x00000080;
	private const int WS_EX_APPWINDOW = 0x00040000;
	private const int WM_DISPLAYCHANGE = 0x007E;

	[DllImport("user32.dll")]
	private static extern int GetWindowLong (IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong (IntPtr hwnd, int index, int newStyle);

	public MainWindow () {
		InitializeComponent();
		PositionWindow();
		SourceInitialized += OnSourceInitialized;

		var settings = App.SettingsService.Settings;
		_hotkey = new HotkeyService(settings.HotkeyVkCodes);
		_hotkey.PushToTalkStarted += OnPttStarted;
		_hotkey.PushToTalkStopped += OnPttStopped;
		_recorder.AmplitudeChanged += OnAmplitude;

		App.WhisperService.StateChanged += OnWhisperState;

		_etaTimer.Tick += OnEtaTick;

		_hotkey.Start();

		// Show GPU/CPU backend info in status during startup
		var cudaVersion = WhisperService.DetectCudaVersion();
		SetStatus(cudaVersion.HasValue
			? $"Loading model… (GPU)"
			: "Loading model… (CPU)");

		// Fade in
		var anim = (Storyboard)Resources["FadeIn"];
		anim.Begin(this);
	}

	private void OnSourceInitialized (object? sender, EventArgs e) {
		var hwnd = new WindowInteropHelper(this).Handle;
		int style = GetWindowLong(hwnd, GWL_EXSTYLE);
		style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
		SetWindowLong(hwnd, GWL_EXSTYLE, style);
		HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
		SizeChanged += (_, _) => OnWindowSizeChanged();
	}

	private IntPtr WndProc (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == WM_DISPLAYCHANGE)
			Dispatcher.BeginInvoke(OnDisplayChange);
		return IntPtr.Zero;
	}

	/// <summary>
	/// Called on every SizeChanged event. Re-anchors the window to the stored centre
	/// point so it expands symmetrically. Skipped before the initial Loaded positioning
	/// has been applied (avoids SaveWindowPosition being called with ActualWidth == 0).
	/// </summary>
	private void OnWindowSizeChanged () {
		if (!_positionApplied)
			return;
		var s = App.SettingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0)
			ApplyStoredPosition();
		else
			ClampWindowToScreen();
	}

	/// <summary>
	/// Called when the display configuration changes (docking/undocking, resolution change).
	/// Resets the widget to the stored position relative to the new primary screen so it
	/// always appears on the primary monitor after docking.
	/// </summary>
	private void OnDisplayChange () {
		var s = App.SettingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0)
			ApplyStoredPosition();
		else
			PlaceAtDefaultPosition();
		// Clamp in case the restored position is still outside all screens.
		ClampWindowToScreen();
	}

	/// <summary>
	/// Ensures the window is fully visible on some monitor after a display-configuration
	/// change (docking / undocking / resolution change) or after a resize.
	/// Strategy:
	///   1. Try to keep the window on the monitor it currently overlaps most.
	///   2. Clamp so every pixel of the window is within that monitor's working area.
	///   3. If the window is entirely off every monitor, fall back to bottom-centre of
	///      the primary screen.
	/// The new position is persisted as (WindowLeft, WindowBottom) relative to the
	/// primary screen's working area.
	/// </summary>
	private void ClampWindowToScreen () {
		if (ActualWidth == 0 || ActualHeight == 0)
			return;

		var source = PresentationSource.FromVisual(this);
		double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
		double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

		// Find the screen whose working area overlaps the window the most.
		var winRectPx = new System.Drawing.Rectangle(
			(int)(Left   / scaleX), (int)(Top    / scaleY),
			(int)(ActualWidth / scaleX), (int)(ActualHeight / scaleY));
		var screen = Screen.AllScreens
			.OrderByDescending(s => {
				var inter = System.Drawing.Rectangle.Intersect(s.WorkingArea, winRectPx);
				return inter.Width * inter.Height;
			})
			.First();
		var wa = screen.WorkingArea;

		double waLeft   = wa.Left   * scaleX;
		double waTop    = wa.Top    * scaleY;
		double waRight  = wa.Right  * scaleX;
		double waBottom = wa.Bottom * scaleY;

		// Clamp so the full window fits inside the working area of that screen.
		double newLeft = Math.Max(waLeft,  Math.Min(Left, waRight  - ActualWidth));
		double newTop  = Math.Max(waTop,   Math.Min(Top,  waBottom - ActualHeight));

		// Safety check: if the result is still fully outside every screen's working
		// area (e.g. the monitor is gone), fall back to bottom-centre of primary.
		var primary = Screen.PrimaryScreen;
		if (primary != null) {
			bool onAnyScreen = Screen.AllScreens.Any(s => {
				var pw = s.WorkingArea;
				double l = pw.Left * scaleX, t = pw.Top * scaleY;
				double r = pw.Right * scaleX, b = pw.Bottom * scaleY;
				return newLeft < r && newLeft + ActualWidth  > l
				    && newTop  < b && newTop  + ActualHeight > t;
			});
		if (!onAnyScreen) {
				var pw = primary.WorkingArea;
				newLeft = pw.Left * scaleX + (pw.Width  * scaleX - ActualWidth)  / 2;
				newTop  = pw.Bottom * scaleY - ActualHeight / 2 - 20;
			}
		}

		Left = newLeft;
		Top  = newTop;
		SaveWindowPosition();
	}

	/// <summary>
	/// Returns the WPF device-independent scale factors for the primary screen.
	/// Falls back to 1.0 before the window has a presentation source (e.g. during construction).
	/// </summary>
	private (double scaleX, double scaleY) GetPrimaryScreenScale () {
		var source = PresentationSource.FromVisual(this);
		if (source?.CompositionTarget != null)
			return (source.CompositionTarget.TransformFromDevice.M11,
			        source.CompositionTarget.TransformFromDevice.M22);
		// Before the window is shown we approximate with WPF's built-in DPI awareness.
		double dpiScale = SystemParameters.PrimaryScreenWidth
		                / Screen.PrimaryScreen!.Bounds.Width;
		return (dpiScale, dpiScale);
	}

	private void PositionWindow () {
		var s = App.SettingsService.Settings;

		if (s.WindowLeft >= 0 && s.WindowBottom >= 0) {
			// Reconstruct position after the window has a size.
			Loaded += (_, _) => { ApplyStoredPosition(); _positionApplied = true; };
		} else {
			// Default: bottom-centre of primary screen, 20 px above the taskbar.
			Loaded += (_, _) => { PlaceAtDefaultPosition(); _positionApplied = true; };
		}
	}

	/// <summary>Positions the window using the stored Left and WindowBottom values.
	/// WindowLeft/WindowBottom store the centre of the window, so the widget
	/// expands symmetrically from that anchor point regardless of its size.</summary>
	private void ApplyStoredPosition () {
		var s = App.SettingsService.Settings;
		var primary = Screen.PrimaryScreen;
		if (primary == null) { PlaceAtDefaultPosition(); return; }

		var (scaleX, scaleY) = GetPrimaryScreenScale();
		var wa = primary.WorkingArea;
		double waLeft   = wa.Left   * scaleX;
		double waBottom = wa.Bottom * scaleY;

		// WindowLeft/WindowBottom are the centre of the window.
		Left = waLeft + s.WindowLeft - ActualWidth  / 2;
		Top  = waBottom - s.WindowBottom - ActualHeight / 2;

		ClampWindowToScreen();
	}

	/// <summary>Places the window at the default bottom-centre position of the primary screen.</summary>
	private void PlaceAtDefaultPosition () {
		var primary = Screen.PrimaryScreen;
		if (primary == null) {
			Left = (SystemParameters.PrimaryScreenWidth  - ActualWidth)  / 2;
			Top  =  SystemParameters.PrimaryScreenHeight - ActualHeight - 20;
			return;
		}
		var (scaleX, scaleY) = GetPrimaryScreenScale();
		var wa = primary.WorkingArea;
		Left = wa.Left * scaleX + (wa.Width  * scaleX - ActualWidth)  / 2;
		// Place 20 px + half window height above the taskbar so the centre anchor
		// is at the same visual distance from the bottom as the window edge was.
		Top  = wa.Bottom * scaleY - ActualHeight / 2 - 20;
		SaveWindowPosition();
	}

	// ── Drag to reposition ───────────────────────────────────────────────────
	private void Border_MouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) {
		DragMove();
		SaveWindowPosition();
	}

	/// <summary>
	/// Persists the current window position as (WindowLeft, WindowBottom) relative to
	/// the primary screen's working area. Both values represent the <b>centre</b> of the
	/// window so that the widget expands symmetrically from the anchor point when its
	/// size changes (e.g. status text grows or shrinks).
	/// </summary>
	private void SaveWindowPosition () {
		var primary = Screen.PrimaryScreen;
		var s = App.SettingsService.Settings;
		if (primary != null) {
			var (scaleX, scaleY) = GetPrimaryScreenScale();
			var wa = primary.WorkingArea;
			double waLeft   = wa.Left   * scaleX;
			double waBottom = wa.Bottom * scaleY;
			// Store the centre of the window.
			s.WindowLeft   = Left + ActualWidth  / 2 - waLeft;
			s.WindowBottom = waBottom - (Top  + ActualHeight / 2);
		} else {
			s.WindowLeft   = Left + ActualWidth  / 2;
			s.WindowBottom = SystemParameters.PrimaryScreenHeight - (Top + ActualHeight / 2);
		}
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

			// WAV bytes: 16000 samples/s × 2 bytes = 32000 bytes/s
			double recordedSeconds = wav.Length / 32000.0;
			var modelKey = GetModelKey();
			var estimatedSeconds = App.EtaStats.EstimateProcessingSeconds(modelKey, recordedSeconds);
			if (estimatedSeconds.HasValue)
				StartEtaCountdown(estimatedSeconds.Value);

			var settings = App.SettingsService.Settings;
			try {
				var sw = System.Diagnostics.Stopwatch.StartNew();
				var text = await App.WhisperService.TranscribeAsync(
					wav, settings.Language, settings.Prompt);

				StopEtaCountdown();

				if (!string.IsNullOrWhiteSpace(text)) {
					App.History.Add(new TranscriptionEntry {
						Text = text,
						Duration = sw.Elapsed,
					});
					LogService.Transcription(text, sw.Elapsed);
					if (settings.CopyToClipboard)
						System.Windows.Clipboard.SetText(text);
					// RestoreFocus must be called on the UI thread (owns message pump).
					// InjectText (WaitForPhysicalRelease + SendInput) runs on a background
					// thread so Thread.Sleep does not block the UI.
					TextInjector.RestoreFocus();
					_ = Task.Run(() => {
						TextInjector.InjectText(text);
						// Record total time from recording stop to injection complete.
						App.EtaStats.Record(modelKey, recordedSeconds, sw.Elapsed.TotalSeconds);
					}).ContinueWith(t => {
						if (t.IsFaulted)
							LogService.Error("InjectText failed", t.Exception?.InnerException);
					});
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
			if (AmplitudeRow.Visibility != Visibility.Visible)
				return;
			double maxWidth = AmplitudeRow.ActualWidth;
			if (maxWidth <= 0)
				return;
			AmplitudeBar.Width = Math.Min(rms * 4.0, 1.0) * maxWidth;
		});
	}

	// ── Whisper state callback ────────────────────────────────────────────────
	private void OnWhisperState (TranscriptionState state, string msg) {
		Dispatcher.Invoke(() => {
			var cudaVer = WhisperService.DetectCudaVersion();
			var backend = cudaVer.HasValue ? $"GPU" : "CPU";
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
					SetStatus($"Ready ({backend})");
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
					break;
				case TranscriptionState.Error:
					SetStatus(msg, isError: true);
					RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"];
					break;
				default:
					SetStatus($"Ready ({backend})");
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
			AmplitudeRow.Margin = new Thickness(0, 3, 0, 0);
			AmplitudeRow.Visibility = Visibility.Visible;
			SetStatus("Recording…");
		} else {
			pulse.Stop(this);
			RecDot.Fill = (SolidColorBrush)WpfApp.Current.Resources["AccentBrush"];
			AmplitudeBar.Width = 0;
			AmplitudeRow.Visibility = Visibility.Collapsed;
			AmplitudeRow.Margin = new Thickness(0);
			SetStatus("Processing…");
		}
	}

	private void SetStatus (string text, bool isError = false) {
		StatusLabel.Text = text;
		StatusLabel.Foreground = isError
			? (SolidColorBrush)WpfApp.Current.Resources["AccentRecordingBrush"]
			: (SolidColorBrush)WpfApp.Current.Resources["TextSecondaryBrush"];
	}

	private void WidgetBorder_MouseEnter (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)Resources["FadeToHover"];
		anim.Begin(this);
	}

	private void WidgetBorder_MouseLeave (object sender, System.Windows.Input.MouseEventArgs e) {
		var anim = (Storyboard)Resources["FadeToIdle"];
		anim.Begin(this);
	}

	// ── Window lifecycle ──────────────────────────────────────────────────────
	/// <summary>
	/// Applies a new key combination to the live hotkey service without restarting the app.
	/// Called by App after settings are saved.
	/// </summary>
	public void ReloadHotkey () {
		_hotkey.UpdateKeys(App.SettingsService.Settings.HotkeyVkCodes);
	}

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
