namespace WhisperWriter.Services;

/// <summary>Per-process memory-budget values used to decide whether another inference can start.</summary>
public sealed record InferenceMemorySnapshot(
	string Provider,
	string DeviceKey,
	long? VideoMemoryBudgetBytes,
	long? VideoMemoryUsageBytes,
	long? AvailableSystemMemoryBytes,
	long? TotalSystemMemoryBytes
);
