namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Boundary for querying global physical-memory headroom.</summary>
public interface ISystemBudgetSource {
	SystemBudget GetSnapshot ();
}