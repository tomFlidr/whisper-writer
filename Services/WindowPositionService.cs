using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Handles positioning logic for the floating widget window.
/// Encapsulates all screen-placement, clamping, and persistence logic
/// so that MainWindow stays focused on UI behaviour.
/// </summary>
public class WindowPositionService: IService, ITransient {

	protected SettingsService settingsService { get; set; } = null!;

	protected Window window = null!;
	
	public WindowPositionService (SettingsService settingsService) {
		this.settingsService = settingsService;
	}

	public WindowPositionService SetWindow (Window window) {
		this.window = window;
		return this;
	}

	/// <summary>
	/// Positions the window on first load. Registers a Loaded handler that either
	/// restores the stored position or falls back to the default bottom-centre placement.
	/// Returns true if a stored position was found, false if default was used.
	/// </summary>
	public bool InitialPosition(Action onPositionApplied) {
		var s = this.settingsService.Settings;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0) {
			this.window.Loaded += (_, _) => { this.ApplyStoredPosition(); onPositionApplied(); };
			return true;
		}
		this.window.Loaded += (_, _) => { this.PlaceAtDefaultPosition(); onPositionApplied(); };
		return false;
	}

	/// <summary>Positions the window using the stored Left and WindowBottom values.</summary>
	public void ApplyStoredPosition() {
		var s = this.settingsService.Settings;
		var primary = Screen.PrimaryScreen;
		if (primary == null) { this.PlaceAtDefaultPosition(); return; }

		var (scaleX, scaleY) = this.getPrimaryScreenScale();
		var wa = primary.WorkingArea;
		double waLeft = wa.Left * scaleX;
		double waBottom = wa.Bottom * scaleY;

		this.window.Left = waLeft + s.WindowLeft - this.window.ActualWidth / 2;
		this.window.Top = waBottom - s.WindowBottom - this.window.ActualHeight / 2;

		this.ClampWindowToScreen();
	}

	/// <summary>Places the window at the default bottom-centre position of the primary screen.</summary>
	public void PlaceAtDefaultPosition() {
		var primary = Screen.PrimaryScreen;
		if (primary == null) {
			this.window.Left = (SystemParameters.PrimaryScreenWidth - this.window.ActualWidth) / 2;
			this.window.Top = SystemParameters.PrimaryScreenHeight - this.window.ActualHeight - 20;
			return;
		}
		var (scaleX, scaleY) = this.getPrimaryScreenScale();
		var wa = primary.WorkingArea;
		this.window.Left = wa.Left * scaleX + (wa.Width * scaleX - this.window.ActualWidth) / 2;
		// Place 20 px + half window height above the taskbar so the centre anchor
		// is at the same visual distance from the bottom as the window edge was.
		this.window.Top = wa.Bottom * scaleY - this.window.ActualHeight / 2 - 20;
		this.SaveWindowPosition();
	}

	/// <summary>
	/// Ensures the window is fully visible on some monitor. Clamps to the monitor the window
	/// overlaps most, falls back to bottom-centre of primary if fully off-screen.
	/// Persists the resulting position.
	/// </summary>
	public void ClampWindowToScreen() {
		if (this.window.ActualWidth == 0 || this.window.ActualHeight == 0)
			return;

		var source = PresentationSource.FromVisual(this.window);
		double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
		double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

		// Find the screen whose working area overlaps the window the most.
		var winRectPx = new System.Drawing.Rectangle(
			(int)(this.window.Left / scaleX), (int)(this.window.Top / scaleY),
			(int)(this.window.ActualWidth / scaleX), (int)(this.window.ActualHeight / scaleY));
		var screen = Screen.AllScreens
			.OrderByDescending(s => {
				var inter = System.Drawing.Rectangle.Intersect(s.WorkingArea, winRectPx);
				return inter.Width * inter.Height;
			})
			.First();
		var wa = screen.WorkingArea;

		double waLeft = wa.Left * scaleX;
		double waTop = wa.Top * scaleY;
		double waRight = wa.Right * scaleX;
		double waBottom = wa.Bottom * scaleY;

		double newLeft = Math.Max(waLeft, Math.Min(this.window.Left, waRight - this.window.ActualWidth));
		double newTop = Math.Max(waTop, Math.Min(this.window.Top, waBottom - this.window.ActualHeight));

		// Safety: if still fully outside every screen, fall back to bottom-centre of primary.
		var primary = Screen.PrimaryScreen;
		if (primary != null) {
			bool onAnyScreen = Screen.AllScreens.Any(s => {
				var pw = s.WorkingArea;
				double l = pw.Left * scaleX, t = pw.Top * scaleY;
				double r = pw.Right * scaleX, b = pw.Bottom * scaleY;
				return newLeft < r && newLeft + this.window.ActualWidth > l
					&& newTop < b && newTop + this.window.ActualHeight > t;
			});
			if (!onAnyScreen) {
				var pw = primary.WorkingArea;
				newLeft = pw.Left * scaleX + (pw.Width * scaleX - this.window.ActualWidth) / 2;
				newTop = pw.Bottom * scaleY - this.window.ActualHeight / 2 - 20;
			}
		}

		this.window.Left = newLeft;
		this.window.Top = newTop;
		this.SaveWindowPosition();
	}

	/// <summary>
	/// Persists the current window position as (WindowLeft, WindowBottom) relative to
	/// the primary screen's working area. Both values represent the centre of the window.
	/// </summary>
	public void SaveWindowPosition() {
		var primary = Screen.PrimaryScreen;
		var s = this.settingsService.Settings;
		if (primary != null) {
			var (scaleX, scaleY) = this.getPrimaryScreenScale();
			var wa = primary.WorkingArea;
			double waLeft = wa.Left * scaleX;
			double waBottom = wa.Bottom * scaleY;
			s.WindowLeft = this.window.Left + this.window.ActualWidth / 2 - waLeft;
			s.WindowBottom = waBottom - (
				this.window.Top + this.window.ActualHeight / 2
			);
		} else {
			s.WindowLeft = this.window.Left + this.window.ActualWidth / 2;
			s.WindowBottom = SystemParameters.PrimaryScreenHeight - (
				this.window.Top + this.window.ActualHeight / 2
			);
		}
		this.settingsService.Save();
	}

	/// <summary>
	/// Returns the WPF device-independent scale factors for the primary screen.
	/// Falls back to 1.0 before the window has a presentation source.
	/// </summary>
	protected (double scaleX, double scaleY) getPrimaryScreenScale () {
		var source = PresentationSource.FromVisual(this.window);
		if (source?.CompositionTarget != null)
			return (source.CompositionTarget.TransformFromDevice.M11,
					source.CompositionTarget.TransformFromDevice.M22);
		// Before the window is shown, approximate with WPF's built-in DPI awareness.
		double dpiScale = SystemParameters.PrimaryScreenWidth / Screen.PrimaryScreen!.Bounds.Width;
		return (dpiScale, dpiScale);
	}
}
