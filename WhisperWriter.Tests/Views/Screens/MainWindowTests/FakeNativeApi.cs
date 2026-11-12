using System.Windows;
using System.Windows.Interop;
using WhisperWriter.Services.MainWindows.Styles;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal class FakeNativeApi : INativeApi {
	public IntPtr Handle { get; } = new(1234);
	public int InitialStyle { get; init; }
	public Window? Window { get; private set; }
	public IntPtr ReadHandle { get; private set; }
	public IntPtr WrittenHandle { get; private set; }
	public int WrittenStyle { get; private set; }
	public HwndSourceHook? Hook { get; private set; }

	public IntPtr GetWindowHandle (Window window) {
		this.Window = window;
		return this.Handle;
	}

	public int GetExtendedWindowStyle (IntPtr hwnd) {
		this.ReadHandle = hwnd;
		return this.InitialStyle;
	}

	public void SetExtendedWindowStyle (IntPtr hwnd, int style) {
		this.WrittenHandle = hwnd;
		this.WrittenStyle = style;
	}

	public void AddHook (IntPtr hwnd, HwndSourceHook hook) {
		this.Hook = hook;
	}
}