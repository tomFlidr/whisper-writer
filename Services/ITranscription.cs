using WhisperWriter.Utils.Enums;

namespace WhisperWriter.Services;

/// <summary>
/// Abstraction over any speech-to-text backend (Whisper, Parakeet, …).
/// App and MainWindow depend only on this interface; the concrete engine is hidden behind it.
/// </summary>
public interface ITranscription: IAsyncDisposable {
	/// <summary>Raised when the backend state changes (loading, transcribing, done, error).</summary>
	event Action<TranscriptionState, string>? StateChanged;

	/// <summary>Initialises the model from disk. Call once at startup.</summary>
	Task InitializeAsync(string modelPath);

	/// <summary>Transcribes the given 16 kHz mono WAV bytes and returns the text.</summary>
	Task<string> TranscribeAsync(byte[] wavBytes, string language, string prompt);
}