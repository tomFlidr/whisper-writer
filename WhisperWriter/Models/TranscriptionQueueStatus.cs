namespace WhisperWriter.Models;

/// <summary>Aggregate state exposed to the compact status and ETA widgets.</summary>
public record TranscriptionQueueStatus (
	int ActiveCount,
	int QueuedCount,
	int OutstandingCount,
	long BufferedAudioBytes,
	long BufferedAudioBudgetBytes,
	int MaxConcurrentJobs,
	IReadOnlyList<TimeSpan?> NextResultReadyEtas,
	int AdditionalEtaCount
) {
	public bool AdmissionPaused { get; init; }
}