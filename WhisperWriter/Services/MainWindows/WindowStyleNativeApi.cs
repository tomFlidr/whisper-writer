using System.Windows;
using System.Windows.Interop;
using WhisperWriter.Services.MainWindows.Styles;

namespace WhisperWriter.Services.MainWindows;

public class WindowStyleNativeApi : IWindowStyleNativeApi {
	public IntPtr GetWindowHandle (Window window) {
		return new WindowInteropHelper(window).Handle;
	}

	public int GetExtendedWindowStyle (IntPtr hwnd) {
		return NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EX_STYLE);
	}

	public void SetExtendedWindowStyle (IntPtr hwnd, int style) {
		NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EX_STYLE, style);
	}

	public void AddHook (IntPtr hwnd, HwndSourceHook hook) {
		HwndSource.FromHwnd(hwnd)?.AddHook(hook);
	}
}