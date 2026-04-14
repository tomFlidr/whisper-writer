using System.Runtime.InteropServices;

namespace WhisperWriter.Services;

/// <summary>
/// Injects text into the previously focused window using SendInput (Unicode).
/// Records the focused HWND before we show the widget so we can restore it.
/// </summary>
public static class TextInjector
{
	#region Win32

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern IntPtr GetMessageExtraInfo();

	private const uint INPUT_KEYBOARD = 1;
	private const ushort KEYEVENTF_UNICODE = 0x0004;
	private const ushort KEYEVENTF_KEYUP = 0x0002;

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT
	{
		public int dx, dy, mouseData, dwFlags, time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct INPUT
	{
		[FieldOffset(0)] public uint type;
		[FieldOffset(4)] public KEYBDINPUT ki;
		[FieldOffset(4)] public MOUSEINPUT mi;
	}

	#endregion

	private static IntPtr _savedHwnd = IntPtr.Zero;

	/// <summary>Call this just before we steal focus (when PTT key is pressed).</summary>
	public static void SaveFocus()
	{
		_savedHwnd = GetForegroundWindow();
	}

	/// <summary>Restores focus and types the text into the target window.</summary>
	public static void InjectText(string text)
	{
		if (string.IsNullOrEmpty(text)) return;

		// Restore focus to the original window
		if (_savedHwnd != IntPtr.Zero)
		{
			SetForegroundWindow(_savedHwnd);
			// Give the window a moment to regain focus
			Thread.Sleep(100);
		}

		// Build INPUT array: each Unicode codepoint = keydown + keyup
		var inputs = new List<INPUT>();
		foreach (char c in text)
		{
			inputs.Add(MakeKeyInput(c, false));
			inputs.Add(MakeKeyInput(c, true));
		}

		SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
	}

	private static INPUT MakeKeyInput(char c, bool keyUp)
	{
		return new INPUT
		{
			type = INPUT_KEYBOARD,
			ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = c,
				dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
				time = 0,
				dwExtraInfo = GetMessageExtraInfo(),
			},
		};
	}
}
