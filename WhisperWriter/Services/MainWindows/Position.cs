using System.Windows;
using System.Drawing;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.MainWindows;

/// <summary>
/// Handles positioning logic for the floating widget window.
/// Encapsulates all screen-placement, clamping, and persistence logic
/// so that MainWindow stays focused on UI behaviour.
/// </summary>
public class Position: IService, ITransient {

	protected Settings settings { get; set; } = null!;

	protected Window window = null!;
	protected readonly IPositionEnvironment environment;
	
	public Position (Settings settingsService) : this(settingsService, new PositionEnvironment()) {
	}

	protected Position (Settings settingsService, IPositionEnvironment environment) {
		this.settings = settingsService;
		this.environment = environment;
	}

	/// <summary>
	/// Binds the position helper to a specific Window instance.
	/// Returns the helper for fluent initialization.
	/// </summary>
	public Position SetWindow (Window window) {
		this.window = window;
		return this;
	}

	/// <summary>
	/// Positions the window on first load. Registers a Loaded handler that either
	/// restores the stored position or falls back to the default bottom-centre placement.
	/// Returns true if a stored position was found, false if default was used.
	/// </summary>
	/// <summary>
	/// Registers a Loaded handler that applies the stored position on first layout.
	/// If a stored position exists it will be applied; otherwise a default position is used.
	/// The provided callback is invoked after the position has been applied.
	/// </summary>
	public bool InitialPosition(Action onPositionApplied) {
		var s = this.settings.Data;
		if (s.WindowLeft >= 0 && s.WindowBottom >= 0) {
			this.window.Loaded += (_, _) => { this.ApplyStoredPosition(); onPositionApplied(); };
			return true;
		}
		this.window.Loaded += (_, _) => { this.PlaceAtDefaultPosition(); onPositionApplied(); };
		return false;
	}

	/// <summary>Positions the window using the stored Left and WindowBottom values.</summary>
	/// <summary>
	/// Applies the stored WindowLeft and WindowBottom values converting from the
	/// primary screen working area coordinates into WPF device-independent pixels.
	/// The window is clamped to the chosen screen afterwards.
	/// </summary>
	public void ApplyStoredPosition() {
		var s = this.settings.Data;
		var primary = this.environment.GetPrimaryWorkingArea();
		if (primary == null) { this.PlaceAtDefaultPosition(); return; }

		var (scaleX, scaleY) = this.getPrimaryScreenScale();
		var wa = primary.Value;
		double waLeft = wa.Left * scaleX;
		double waBottom = wa.Bottom * scaleY;

		this.window.Left = waLeft + s.WindowLeft - this.window.ActualWidth / 2;
		this.window.Top = waBottom - s.WindowBottom - this.window.ActualHeight / 2;

		this.ClampWindowToScreen();
	}

	/// <summary>Places the window at the default bottom-centre position of the primary screen.</summary>
	/// <summary>
	/// Places the window at the default bottom-centre location of the primary screen
	/// using a 20 px offset above the taskbar to keep the widget visually separated.
	/// </summary>
	public void PlaceAtDefaultPosition() {
		var primary = this.environment.GetPrimaryWorkingArea();
		if (primary == null) {
			this.window.Left = (this.environment.PrimaryScreenWidth - this.window.ActualWidth) / 2;
			this.window.Top = this.environment.PrimaryScreenHeight - this.window.ActualHeight - 20;
			return;
		}
		var (scaleX, scaleY) = this.getPrimaryScreenScale();
		var wa = primary.Value;
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
	/// <summary>
	/// Ensures the window is fully visible on a monitor by clamping its Left/Top
	/// to the working area of the monitor the window overlaps most. Falls back to
	/// the primary screen if the window is entirely off-screen.
	/// </summary>
	public void ClampWindowToScreen() {
		if (this.window.ActualWidth == 0 || this.window.ActualHeight == 0)
			return;

		var (scaleX, scaleY) = this.environment.GetWindowScale(this.window);

		// Find the screen whose working area overlaps the window the most.
		var winRectPx = new System.Drawing.Rectangle(
			(int)(this.window.Left / scaleX), (int)(this.window.Top / scaleY),
			(int)(this.window.ActualWidth / scaleX), (int)(this.window.ActualHeight / scaleY));
		var screen = this.environment.GetWorkingAreas()
			.OrderByDescending(workingArea => {
				var inter = Rectangle.Intersect(workingArea, winRectPx);
				return inter.Width * inter.Height;
			})
			.First();
		var wa = screen;

		double waLeft = wa.Left * scaleX;
		double waTop = wa.Top * scaleY;
		double waRight = wa.Right * scaleX;
		double waBottom = wa.Bottom * scaleY;

		double newLeft = Math.Max(waLeft, Math.Min(this.window.Left, waRight - this.window.ActualWidth));
		double newTop = Math.Max(waTop, Math.Min(this.window.Top, waBottom - this.window.ActualHeight));

		// Safety: if still fully outside every screen, fall back to bottom-centre of primary.
		var primary = this.environment.GetPrimaryWorkingArea();
		if (primary != null) {
			bool onAnyScreen = this.environment.GetWorkingAreas().Any(pw => {
				double l = pw.Left * scaleX, t = pw.Top * scaleY;
				double r = pw.Right * scaleX, b = pw.Bottom * scaleY;
				return newLeft < r && newLeft + this.window.ActualWidth > l
					&& newTop < b && newTop + this.window.ActualHeight > t;
			});
			if (!onAnyScreen) {
				var pw = primary.Value;
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
	/// <summary>
	/// Persists the current window centre position as WindowLeft/WindowBottom relative
	/// to the primary screen's working area in device-independent pixels.
	/// </summary>
	public void SaveWindowPosition() {
		var primary = this.environment.GetPrimaryWorkingArea();
		var s = this.settings.Data;
		if (primary != null) {
			var (scaleX, scaleY) = this.getPrimaryScreenScale();
			var wa = primary.Value;
			double waLeft = wa.Left * scaleX;
			double waBottom = wa.Bottom * scaleY;
			s.WindowLeft = this.window.Left + this.window.ActualWidth / 2 - waLeft;
			s.WindowBottom = waBottom - (
				this.window.Top + this.window.ActualHeight / 2
			);
		} else {
			s.WindowLeft = this.window.Left + this.window.ActualWidth / 2;
			s.WindowBottom = this.environment.PrimaryScreenHeight - (
				this.window.Top + this.window.ActualHeight / 2
			);
		}
		this.settings.Save();
	}

	/// <summary>
	/// Returns the WPF device-independent scale factors for the primary screen.
	/// Falls back to 1.0 before the window has a presentation source.
	/// </summary>
	/// <summary>
	/// Returns the current WPF device-independent scale factors for the primary screen.
	/// Falls back to an approximation when a presentation source is not yet available.
	/// </summary>
	protected (double scaleX, double scaleY) getPrimaryScreenScale () {
		return this.environment.GetPrimaryScreenScale(this.window);
	}
}
