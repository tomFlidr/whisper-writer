using System.Runtime.InteropServices;

namespace WhisperWriter.Services.TextOutput.TextInjectors;

/// <summary>Win32 implementation of the window, keyboard, timing, and input platform boundary.</summary>
internal class NativePlatform : IPlatform {
	public int InputSize => Marshal.SizeOf<Input>();

	[DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
	private static extern IntPtr GetForegroundWindowNative ();

	[DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo", SetLastError = true)]
	private static extern bool GetGUIThreadInfoNative (uint threadId, ref GuiThreadInfo info);

	[DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
	private static extern bool SetForegroundWindowNative (IntPtr hWnd);

	[DllImport("user32.dll", EntryPoint = "BringWindowToTop")]
	private static extern bool BringWindowToTopNative (IntPtr hWnd);

	[DllImport("user32.dll", EntryPoint = "SetFocus")]
	private static extern IntPtr SetFocusNative (IntPtr hWnd);

	[DllImport("user32.dll", EntryPoint = "AttachThreadInput")]
	private static extern bool AttachThreadInputNative (uint idAttach, uint idAttachTo, bool attach);

	[DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
	private static extern uint GetWindowThreadProcessIdNative (IntPtr hWnd, out uint processId);

	[DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
	private static extern uint GetCurrentThreadIdNative ();

	[DllImport("user32.dll", EntryPoint = "SendInput")]
	private static extern uint SendInputNative (uint inputCount, Input[] inputs, int inputSize);

	[DllImport("user32.dll", EntryPoint = "GetMessageExtraInfo")]
	private static extern IntPtr GetMessageExtraInfoNative ();

	[DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
	private static extern short GetAsyncKeyStateNative (int virtualKey);

	public IntPtr GetForegroundWindow () => NativePlatform.GetForegroundWindowNative();

	public IntPtr GetFocusedWindow (IntPtr foregroundWindow) {
		uint threadId = NativePlatform.GetWindowThreadProcessIdNative(foregroundWindow, out _);
		var info = new GuiThreadInfo {
			Size = (uint)Marshal.SizeOf<GuiThreadInfo>(),
		};
		return NativePlatform.GetGUIThreadInfoNative(threadId, ref info)
			? info.FocusWindow
			: IntPtr.Zero;
	}

	public bool SetForegroundWindow (IntPtr hWnd) => NativePlatform.SetForegroundWindowNative(hWnd);

	public void SetFocus (IntPtr hWnd) => NativePlatform.SetFocusNative(hWnd);

	public bool BringWindowToTop (IntPtr hWnd) => NativePlatform.BringWindowToTopNative(hWnd);

	public bool AttachThreadInput (uint idAttach, uint idAttachTo, bool attach) =>
		NativePlatform.AttachThreadInputNative(idAttach, idAttachTo, attach);

	public uint GetWindowThreadProcessId (IntPtr hWnd) =>
		NativePlatform.GetWindowThreadProcessIdNative(hWnd, out _);

	public uint GetCurrentThreadId () => NativePlatform.GetCurrentThreadIdNative();

	public uint SendInput (uint inputCount, IReadOnlyList<InputEvent> inputs, int inputSize) {
		var nativeInputs = new Input[inputs.Count];
		for (int index = 0; index < inputs.Count; index++) {
			InputEvent input = inputs[index];
			nativeInputs[index] = new Input {
				type = input.Type,
				ki = new InputKeyboard {
					wVk = input.VirtualKey,
					wScan = input.ScanCode,
					dwFlags = input.Flags,
					time = input.Time,
					dwExtraInfo = input.ExtraInfo,
				},
			};
		}
		return NativePlatform.SendInputNative(inputCount, nativeInputs, inputSize);
	}

	public IntPtr GetMessageExtraInfo () => NativePlatform.GetMessageExtraInfoNative();

	public short GetAsyncKeyState (int virtualKey) => NativePlatform.GetAsyncKeyStateNative(virtualKey);

	public void Delay (int milliseconds) => Thread.Sleep(milliseconds);

	public int GetLastError () => Marshal.GetLastWin32Error();

	[StructLayout(LayoutKind.Sequential)]
	private struct GuiThreadInfo {
		public uint Size;
		public uint Flags;
		public IntPtr ActiveWindow;
		public IntPtr FocusWindow;
		public IntPtr CaptureWindow;
		public IntPtr MenuOwner;
		public IntPtr MoveSizeWindow;
		public IntPtr CaretWindow;
		public Rect CaretRect;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect {
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}
}