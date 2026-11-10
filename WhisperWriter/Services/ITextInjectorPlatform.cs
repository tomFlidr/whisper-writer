namespace WhisperWriter.Services;

/// <summary>
/// Boundary for the native window, keyboard, timing, and SendInput operations used by TextInjector.
/// </summary>
public interface ITextInjectorPlatform {
	public readonly record struct InputEvent (
		uint Type,
		ushort VirtualKey,
		ushort ScanCode,
		uint Flags,
		uint Time,
		IntPtr ExtraInfo
	);

	IntPtr GetForegroundWindow ();
	IntPtr GetFocusedWindow (IntPtr foregroundWindow);
	bool SetForegroundWindow (IntPtr hWnd);
	void SetFocus (IntPtr hWnd);
	bool BringWindowToTop (IntPtr hWnd);
	bool AttachThreadInput (uint idAttach, uint idAttachTo, bool attach);
	uint GetWindowThreadProcessId (IntPtr hWnd);
	uint GetCurrentThreadId ();
	int InputSize { get; }
	uint SendInput (uint inputCount, IReadOnlyList<InputEvent> inputs, int inputSize);
	IntPtr GetMessageExtraInfo ();
	short GetAsyncKeyState (int virtualKey);
	void Delay (int milliseconds);
	int GetLastError ();
}