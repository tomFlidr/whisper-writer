using System.Windows;
using System.Windows.Interop;

namespace WhisperWriter.Services.MainWindows;

public interface IWindowStyleNativeApi {
	IntPtr GetWindowHandle (Window window);
	int GetExtendedWindowStyle (IntPtr hwnd);
	void SetExtendedWindowStyle (IntPtr hwnd, int style);
	void AddHook (IntPtr hwnd, HwndSourceHook hook);
}