namespace WhisperWriter.Services.Inference.Capacity;

/// <summary>Persisted concurrency and per-inference memory estimate for one runtime profile.</summary>
public record Profile (
	int MaxConcurrentJobs,
	long EstimatedSlotBytes,
	DateTimeOffset UpdatedAt,
	DateTimeOffset? ExpiresAt = null
);