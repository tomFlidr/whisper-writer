using System.Windows;
using WhisperWriter.Services.MainWindows.Styles;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.MainWindows;
/// <summary>
/// Window style helper that tweaks extended window styles to make the main widget
/// appear as a tool window (no taskbar entry) and listens for display change messages.
/// The WindowDisplayChange event is raised when the system notifies of a display configuration change.
/// </summary>
public class Style : IService, ISingleton {

	public event EventHandler? WindowDisplayChange;

	protected Window window = null!;
	protected readonly INativeApi nativeApi;

	public Style () : this(new NativeApi()) {
	}

	protected Style (INativeApi nativeApi) {
		this.nativeApi = nativeApi;
	}

	/// <summary>
	/// Assigns the target window instance the style helper will operate on.
	/// Returns the helper for fluent wiring in the constructor.
	/// </summary>
	public Style SetWindow (Window window) {
		this.window = window;
		return this;
	}

	/// <summary>
	/// Initializes native window hooks and adjusts extended styles to prevent a taskbar button being shown.
	/// Should be called from the window SourceInitialized event.
	/// </summary>
	public void InitializeSystemEvents (object sender, EventArgs args) {
		var hwnd = this.nativeApi.GetWindowHandle(this.window);
		int style = this.nativeApi.GetExtendedWindowStyle(hwnd);
		style = (style | NativeMethods.WS_EX_TOOL_WINDOW) & ~NativeMethods.WS_EX_APP_WINDOW;
		this.nativeApi.SetExtendedWindowStyle(hwnd, style);
		this.nativeApi.AddHook(hwnd, this.handleWin32SystemHook);
	}

	/// <summary>
	/// Win32 hook that listens for WM_DISPLAYCHANGE and forwards it via WindowDisplayChange.
	/// </summary>
	protected IntPtr handleWin32SystemHook (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == NativeMethods.WM_DISPLAY_CHANGE) {
			this.WindowDisplayChange?.Invoke(this, EventArgs.Empty);
		}
		return IntPtr.Zero;
	}


}