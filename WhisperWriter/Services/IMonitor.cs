using WhisperWriter.Models;

namespace WhisperWriter.Services;

/// <summary>Provides a live resource snapshot for the currently loaded inference provider.</summary>
public interface IMonitor {
	InferenceMemorySnapshot GetSnapshot (string? provider);
}