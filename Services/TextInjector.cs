using System.Runtime.InteropServices;

namespace WhisperWriter.Services;

/// <summary>
/// Injects text into the previously focused window using SendInput (Unicode).
/// Records the focused HWND before we show the widget so we can restore it.
/// </summary>
public static class TextInjector {
	#region Win32

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow ();

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow (IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop (IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool AttachThreadInput (uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId (IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId ();

	[DllImport("user32.dll")]
	private static extern uint SendInput (uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern IntPtr GetMessageExtraInfo ();

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState (int vKey);

	private const uint INPUT_KEYBOARD = 1;
	private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
	private const uint KEYEVENTF_UNICODE = 0x0004;
	private const uint KEYEVENTF_KEYUP = 0x0002;

	// Virtual key codes for modifier keys that must be released before injecting text
	private const ushort VK_LWIN = 0x5B;
	private const ushort VK_RWIN = 0x5C;
	private const ushort VK_LCONTROL = 0xA2;
	private const ushort VK_RCONTROL = 0xA3;
	private const ushort VK_LMENU = 0xA4;
	private const ushort VK_RMENU = 0xA5;
	private const ushort VK_LSHIFT = 0xA0;
	private const ushort VK_RSHIFT = 0xA1;

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT {
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT {
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	// On 64-bit Windows the union inside INPUT is aligned to 8 bytes,
	// so ki/mi start at offset 8, not 4.
	// Win32 sizeof(INPUT) = 40 bytes on 64-bit, 28 bytes on 32-bit.
	[StructLayout(LayoutKind.Explicit)]
	private struct INPUT {
		[FieldOffset(0)] public uint type;
		[FieldOffset(8)] public KEYBDINPUT ki;
		[FieldOffset(8)] public MOUSEINPUT mi;
	}

	#endregion

	private static IntPtr _savedHwnd = IntPtr.Zero;

	/// <summary>Call this just before we steal focus (when PTT key is pressed).</summary>
	public static void SaveFocus () {
		_savedHwnd = GetForegroundWindow();
		// Verify struct layout matches Win32 at runtime (40 bytes on 64-bit, 28 on 32-bit).
		int expected = IntPtr.Size == 8 ? 40 : 28;
		int actual = Marshal.SizeOf<INPUT>();
		if (actual != expected)
			LogService.Error($"INPUT struct size mismatch: got {actual}, expected {expected}. Text injection will fail.");
	}

	/// <summary>
	/// Restores keyboard focus to the saved window.
	/// Must be called from the UI thread (which owns a message pump), because
	/// AttachThreadInput requires the calling thread to have a message queue.
	/// </summary>
	public static void RestoreFocus () {
		if (_savedHwnd == IntPtr.Zero) return;
		uint targetThread = GetWindowThreadProcessId(_savedHwnd, out _);
		uint currentThread = GetCurrentThreadId();
		bool attached = targetThread != currentThread
			&& AttachThreadInput(currentThread, targetThread, true);
		SetForegroundWindow(_savedHwnd);
		BringWindowToTop(_savedHwnd);
		if (attached)
			AttachThreadInput(currentThread, targetThread, false);
	}

	/// <summary>
	/// Waits for PTT modifier keys to be physically released, then injects text.
	/// Must be called from a background thread (NOT the UI/Dispatcher thread) so
	/// that Thread.Sleep does not block the message pump.
	/// </summary>
	public static void InjectText (string text) {
		if (string.IsNullOrEmpty(text)) return;

		// Wait until Win and Ctrl are physically released (up to 2 s).
		// SendInput keyup events sent while a key is still physically held are
		// silently ignored by Windows – the Win shell hook stays active and
		// permanently breaks mouse input until reboot.
		WaitForPhysicalRelease();

		// Release all modifier keys via synthetic keyup events.
		// Win keys require KEYEVENTF_EXTENDEDKEY or the keyup is ignored.
		ReleaseModifierKeys();

		// Small pause so the target window has time to process the focus change
		// that RestoreFocus() already requested on the UI thread.
		Thread.Sleep(80);

		// Build INPUT array: each Unicode codepoint = keydown + keyup
		var inputs = new List<INPUT>();
		foreach (char c in text) {
			inputs.Add(MakeUnicodeKeyInput(c, false));
			inputs.Add(MakeUnicodeKeyInput(c, true));
		}

		uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
		if (sent != (uint)inputs.Count)
			LogService.Warning($"SendInput: sent {sent}/{inputs.Count} events. LastError={Marshal.GetLastWin32Error()}");
		else
			LogService.Info($"SendInput: OK, {sent} events for {text.Length} chars");
	}

	/// <summary>
	/// Blocks (max 2 s) until VK_LWIN and VK_LCONTROL are physically released.
	/// This is necessary because HotkeyService polling fires PushToTalkStopped
	/// the moment it sees both keys up, but the Win key kernel shell hook may
	/// still register them as held for a few milliseconds. Sending a keyup via
	/// SendInput while the key is still physically down is silently ignored by
	/// Windows – the shell hook stays active and permanently breaks mouse input.
	/// </summary>
	private static void WaitForPhysicalRelease () {
		const int timeoutMs = 2000;
		const int stepMs = 10;
		int elapsed = 0;
		while (elapsed < timeoutMs) {
			bool winHeld = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
				|| (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
			bool ctrlHeld = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0
				|| (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
			if (!winHeld && !ctrlHeld)
				return;
			Thread.Sleep(stepMs);
			elapsed += stepMs;
		}
		LogService.Warning("WaitForPhysicalRelease: timed out after 2s, keys still held");
	}

	/// <summary>
	/// Sends synthetic key-up events for all common modifier keys so that no
	/// physically-held key (Win, Ctrl, Alt, Shift) interferes with SendInput.
	/// Win keys require KEYEVENTF_EXTENDEDKEY – without it the keyup is ignored
	/// and the Win shell hook stays active, permanently breaking mouse buttons.
	/// </summary>
	private static void ReleaseModifierKeys () {
		// (vk, needsExtendedKey)
		(ushort vk, bool ext)[] modifiers = [
			(VK_LWIN,     true),
			(VK_RWIN,     true),
			(VK_LCONTROL, false),
			(VK_RCONTROL, false),
			(VK_LMENU,    false),
			(VK_RMENU,    false),
			(VK_LSHIFT,   false),
			(VK_RSHIFT,   false),
		];
		var inputs = modifiers
			.Select(m => MakeVkKeyUp(m.vk, m.ext))
			.ToArray();
		SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
	}

	private static INPUT MakeVkKeyUp (ushort vk, bool extendedKey = false) {
		return new INPUT {
			type = INPUT_KEYBOARD,
			ki = new KEYBDINPUT {
				wVk = vk,
				wScan = 0,
				dwFlags = KEYEVENTF_KEYUP | (extendedKey ? KEYEVENTF_EXTENDEDKEY : 0u),
				time = 0,
				dwExtraInfo = GetMessageExtraInfo(),
			},
		};
	}

	private static INPUT MakeUnicodeKeyInput (char c, bool keyUp) {
		return new INPUT {
			type = INPUT_KEYBOARD,
			ki = new KEYBDINPUT {
				wVk = 0,
				wScan = c,
				dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0u),
				time = 0,
				dwExtraInfo = GetMessageExtraInfo(),
			},
		};
	}
}