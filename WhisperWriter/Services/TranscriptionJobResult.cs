namespace WhisperWriter.Services;

/// <summary>Terminal inference outcome for one immutable transcription job.</summary>
public sealed record TranscriptionJobResult(
	TranscriptionJob Job,
	string? Text,
	Exception? Error,
	TimeSpan ProcessingTime,
	bool WasRetried
);
