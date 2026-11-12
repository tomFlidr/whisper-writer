namespace WhisperWriter.Models;

/// <summary>Terminal inference outcome for one immutable transcription job.</summary>
public record TranscriptionJobResult (
	TranscriptionJob Job,
	string? Text,
	Exception? Error,
	TimeSpan ProcessingTime,
	bool WasRetried
);