namespace WhisperWriter.Services;

/// <summary>Persisted concurrency and per-inference memory estimate for one runtime profile.</summary>
public sealed record InferenceCapacityProfile(
	int MaxConcurrentJobs,
	long EstimatedSlotBytes,
	DateTimeOffset UpdatedAt,
	DateTimeOffset? ExpiresAt = null
);
