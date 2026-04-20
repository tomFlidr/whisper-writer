namespace WhisperWriter.Models;

public class AppSettings {
	// Path to the ggml model file
	public string ModelPath { get; set; } = "llms/ggml-large-v2.bin";

	// Language hint passed to Whisper ("cs", "en", or "auto")
	public string Language { get; set; } = "auto";

	// System prompt prepended to every transcription request
	public string Prompt { get; set; } = "";

	// Bitmask of HotkeyModifiers – kept for potential future use, not used for polling
	public int HotkeyModifiers { get; set; } = 0x0001 | 0x0008; // Alt | Win

	// VK codes that must all be held simultaneously to trigger push-to-talk.
	// Default: Left Alt (0xA4) + Left Win (0x5B).
	// An empty list means "use HotkeyModifiers bitmask" (legacy fallback).
	public List<int> HotkeyVkCodes { get; set; } = [0xA4, 0x5B]; // VK_LMENU + _VK_LWIN

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