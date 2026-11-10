namespace WhisperWriter.Services.TextInjectors;

/// <summary>
/// Captured foreground window and focused child control for one recording job.
/// </summary>
public readonly record struct FocusTarget(IntPtr ForegroundWindow, IntPtr FocusedWindow);
