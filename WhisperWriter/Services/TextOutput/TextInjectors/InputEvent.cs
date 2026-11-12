namespace WhisperWriter.Services.TextOutput.TextInjectors;

/// <summary>One Win32 INPUT event in the application's platform-neutral injection boundary.</summary>
public readonly record struct InputEvent (
	uint Type,
	ushort VirtualKey,
	ushort ScanCode,
	uint Flags,
	uint Time,
	IntPtr ExtraInfo
);