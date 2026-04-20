using System.Runtime.InteropServices;
using WhisperWriter.Services.TextInjectors;

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
	private static extern uint SendInput (uint nInputs, Input[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern IntPtr GetMessageExtraInfo ();

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState (int vKey);

	private static readonly uint _inputKeyboard				= 1;
	private static readonly uint _keyEventFExtendKey		= 0x0001;
	private static readonly uint _keyEventFUnicode			= 0x0004;
	private static readonly uint _keyEventFKeyUp			= 0x0002;

	// Virtual key codes for modifier keys that must be released before injecting text
	private static readonly ushort _virtualKeyWinLeft		= 0x5B;
	private static readonly ushort _virtualKeyWinRight		= 0x5C;
	private static readonly ushort _virtualKeyCtrlLeft		= 0xA2;
	private static readonly ushort _virtualKeyCtrlRight		= 0xA3;
	private static readonly ushort _virtualKeyMenuLeft		= 0xA4;
	private static readonly ushort _virtualKeyMenuRight		= 0xA5;
	private static readonly ushort _virtualKeyShiftLeft		= 0xA0;
	private static readonly ushort _virtualKeyShiftRight	= 0xA1;
	#endregion

	private static IntPtr _savedHwnd = IntPtr.Zero;

	/// <summary>Call this just before we steal focus (when PTT key is pressed).</summary>
	public static void SaveFocus () {
		TextInjector._savedHwnd = TextInjector.GetForegroundWindow();
		// Verify struct layout matches Win32 at runtime (40 bytes on 64-bit, 28 on 32-bit).
		int expected = IntPtr.Size == 8 ? 40 : 28;
		int actual = Marshal.SizeOf<Input>();
		if (actual != expected)
			LogService.Error(
				$"Input struct size mismatch: got {actual}, expected {expected}. Text injection will fail."
			);
	}

	/// <summary>
	/// Restores keyboard focus to the saved window.
	/// Must be called from the UI thread (which owns a message pump), because
	/// AttachThreadInput requires the calling thread to have a message queue.
	/// </summary>
	public static void RestoreFocus () {
		if (TextInjector._savedHwnd == IntPtr.Zero) return;
		uint targetThread = TextInjector.GetWindowThreadProcessId(TextInjector._savedHwnd, out _);
		uint currentThread = TextInjector.GetCurrentThreadId();
		bool attached = (
			targetThread != currentThread &&
			TextInjector.AttachThreadInput(
				currentThread, targetThread, true
			)
		);
		TextInjector.SetForegroundWindow(TextInjector._savedHwnd);
		TextInjector.BringWindowToTop(TextInjector._savedHwnd);
		if (attached)
			TextInjector.AttachThreadInput(currentThread, targetThread, false);
	}

	/// <summary>
	/// Waits for PTT modifier keys to be physically released, then injects text.
	/// Must be called from a background thread (NOT the UI/Dispatcher thread) so
	/// that Thread.Sleep does not block the message pump.
	/// </summary>
	/// <param name="text">Text to inject via SendInput.</param>
	/// <param name="pttVkCodes">Virtual key codes of the active PTT hotkey combination.</param>
	public static void InjectText (string text, IReadOnlyList<int> pttVkCodes) {
		if (string.IsNullOrEmpty(text)) return;

		// Wait until Win and Ctrl are physically released (up to 2 s).
		// SendInput keyup events sent while a key is still physically held are
		// silently ignored by Windows – the Win shell hook stays active and
		// permanently breaks mouse input until reboot.
		TextInjector._waitForPhysicalRelease(pttVkCodes);

		// Release all modifier keys via synthetic keyup events.
		// Win keys require _keyEventFExtendKey or the keyup is ignored.
		TextInjector._releaseModifierKeys();

		// Small pause so the target window has time to process the focus change
		// that RestoreFocus() already requested on the UI thread.
		Thread.Sleep(80);

		// Build Input array: each Unicode codepoint = keydown + keyup
		var inputs = new List<Input>();
		foreach (char c in text) {
			inputs.Add(TextInjector._makeUnicodeKeyInput(c, false));
			inputs.Add(TextInjector._makeUnicodeKeyInput(c, true));
		}

		uint sent = TextInjector.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
		if (sent != (uint)inputs.Count) {
			LogService.Warning($"SendInput: sent {sent}/{inputs.Count} events. LastError={Marshal.GetLastWin32Error()}");
		} else {
			LogService.Info($"SendInput: OK, {sent} events for {text.Length} chars");
		}
	}

	/// <summary>
	/// Blocks (max 2 s) until all PTT hotkey VK codes are physically released.
	/// This is necessary because HotkeyService polling fires PushToTalkStopped
	/// the moment it sees the combo released, but the Win key kernel shell hook may
	/// still register them as held for a few milliseconds. Sending a keyup via
	/// SendInput while a key is still physically down is silently ignored by
	/// Windows – the shell hook stays active and permanently breaks mouse input.
	/// </summary>
	private static void _waitForPhysicalRelease (IReadOnlyList<int> hotkeyVks) {
		const int timeoutMs = 2000;
		const int stepMs = 10;
		int elapsed = 0;
		while (elapsed < timeoutMs) {
			bool anyHeld = false;
			// Check all configured PTT keys
			foreach (var vk in hotkeyVks) {
				if ((TextInjector.GetAsyncKeyState(vk) & 0x8000) != 0) { anyHeld = true; break; }
			}
			// Always check both Win keys – the shell hook specifically reacts to them
			if (!anyHeld) {
				anyHeld = (
					(TextInjector.GetAsyncKeyState(TextInjector._virtualKeyWinLeft) & 0x8000) != 0 ||
					(TextInjector.GetAsyncKeyState(TextInjector._virtualKeyWinRight) & 0x8000) != 0
				);
			}
			if (!anyHeld)
				return;
			Thread.Sleep(stepMs);
			elapsed += stepMs;
		}
		LogService.Warning("_waitForPhysicalRelease: timed out after 2s, keys still held");
	}

	/// <summary>
	/// Sends synthetic key-up events for all common modifier keys so that no
	/// physically-held key (Win, Ctrl, Alt, Shift) interferes with SendInput.
	/// Win keys require _keyEventFExtendKey – without it the keyup is ignored
	/// and the Win shell hook stays active, permanently breaking mouse buttons.
	/// </summary>
	private static void _releaseModifierKeys () {
		// (vk, needsExtendedKey)
		(ushort vk, bool ext)[] modifiers = [
			(TextInjector._virtualKeyWinLeft,		true),
			(TextInjector._virtualKeyWinRight,		true),
			(TextInjector._virtualKeyCtrlLeft,		false),
			(TextInjector._virtualKeyCtrlRight,		false),
			(TextInjector._virtualKeyMenuLeft,		false),
			(TextInjector._virtualKeyMenuRight,		false),
			(TextInjector._virtualKeyShiftLeft,		false),
			(TextInjector._virtualKeyShiftRight,	false),
		];
		var inputs = modifiers
			.Select(m => TextInjector._makeVirtualKeyUp(m.vk, m.ext))
			.ToArray();
		TextInjector.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
	}

	private static Input _makeVirtualKeyUp (ushort vk, bool extendedKey = false) {
		return new Input {
			type = TextInjector._inputKeyboard,
			ki = new InputKeyboard {
				wVk = vk,
				wScan = 0,
				dwFlags = TextInjector._keyEventFKeyUp | (
					extendedKey
						? TextInjector._keyEventFExtendKey
						: 0u
				),
				time = 0,
				dwExtraInfo = TextInjector.GetMessageExtraInfo(),
			},
		};
	}

	private static Input _makeUnicodeKeyInput (char c, bool keyUp) {
		return new Input {
			type = TextInjector._inputKeyboard,
			ki = new InputKeyboard {
				wVk = 0,
				wScan = c,
				dwFlags = TextInjector._keyEventFUnicode | (
					keyUp
						? TextInjector._keyEventFKeyUp
						: 0u
				),
				time = 0,
				dwExtraInfo = TextInjector.GetMessageExtraInfo(),
			},
		};
	}
}