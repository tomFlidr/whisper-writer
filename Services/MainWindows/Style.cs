using System.Windows;
using System.Windows.Interop;
using WhisperWriter.Services.MainWindows.Styles;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter.Services.MainWindows;
public class Style: IService, ISingleton {
	
	public event EventHandler? WindowDisplayChange;

	protected Window window = null!;
	
	public Style SetWindow (Window window) {
		this.window = window;
		return this;
	}

	public void InitializeSystemEvents (object sender, EventArgs args) {
		var hwnd = new WindowInteropHelper(this.window).Handle;
		int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EX_STYLE);
		style = (style | NativeMethods.WS_EX_TOOL_WINDOW) & ~NativeMethods.WS_EX_APP_WINDOW;
		NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EX_STYLE, style);
		HwndSource.FromHwnd(hwnd)?.AddHook(this.handleWin32SystemHook);
	}
	
	protected IntPtr handleWin32SystemHook (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
		if (msg == NativeMethods.WM_DISPLAY_CHANGE) {
			this.WindowDisplayChange?.Invoke(this, EventArgs.Empty);
		}
		return IntPtr.Zero;
	}
	
}
