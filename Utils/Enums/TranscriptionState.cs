namespace WhisperWriter.Utils.Enums;

/// <summary>
/// Logical state of the transcription engine exposed to the UI.
/// </summary>
public enum TranscriptionState {
	Idle,
	Loading,
	Transcribing,
	Done,
	Error,
}
