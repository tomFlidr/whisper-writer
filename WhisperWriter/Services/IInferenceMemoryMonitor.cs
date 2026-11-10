namespace WhisperWriter.Services;

/// <summary>Provides a live resource snapshot for the currently loaded inference provider.</summary>
public interface IInferenceMemoryMonitor {
	InferenceMemorySnapshot GetSnapshot(string? provider);
}
