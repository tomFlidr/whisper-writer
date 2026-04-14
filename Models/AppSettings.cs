namespace WhisperWriter.Models;

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

	// Widget position on screen (negative = use default bottom-center)
	public double WindowLeft { get; set; } = -1;
	public double WindowTop { get; set; } = -1;
}

[Flags]
public enum HotkeyModifiers {
	None = 0,
	Alt = 0x0001,
	Control = 0x0002,
	Shift = 0x0004,
	Win = 0x0008,
}
