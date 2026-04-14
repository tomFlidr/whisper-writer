namespace WhisperWriter.Util;

public class AppSettings {
	// Path to the ggml model file
	public string ModelPath { get; set; } = "models/ggml-large-v2.bin";

	// Language hint passed to Whisper ("cs", "en", or "auto")
	public string Language { get; set; } = "auto";

	// System prompt prepended to every transcription request
	public string Prompt { get; set; } = "";

	// Bitmask of HotkeyModifiers (default: Ctrl + Win)
	public int HotkeyModifiers { get; set; } = 0x0002 | 0x0008; // Control | Win

	// How many transcriptions to keep in history
	public int HistorySize { get; set; } = 30;

	// If true, every transcription result is also copied to the clipboard
	public bool CopyToClipboard { get; set; } = true;

	// If true, the app is registered to start with Windows (HKCU Run key)
	public bool RunAtStartup { get; set; } = false;

	// Widget position on screen.
	// WindowLeft:   distance from the left edge of the primary screen's working area (DIP). Negative = use default.
	// WindowBottom: distance from the bottom edge of the primary screen's working area (DIP). Negative = use default.
	public double WindowLeft { get; set; } = -1;
	public double WindowBottom { get; set; } = -1;
}