using System.IO;

namespace WhisperWriter.Services.Whispers;

/// <summary>Loaded model factory used to run transcription requests.</summary>
public interface IRuntimeFactory : IDisposable {
	IAsyncEnumerable<string> ProcessAsync (Stream audio, string language, int threadCount, string? prompt);
}