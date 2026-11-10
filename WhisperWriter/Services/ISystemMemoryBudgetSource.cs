namespace WhisperWriter.Services;

/// <summary>Boundary for querying global physical-memory headroom.</summary>
public interface ISystemMemoryBudgetSource {
	SystemMemoryBudget GetSnapshot();
}
