namespace WhisperWriter.Services;

/// <summary>Boundary for querying DXGI adapter budgets without coupling policy tests to COM.</summary>
public interface IDxgiMemoryBudgetSource {
	IReadOnlyList<DxgiAdapterMemoryBudget> GetAdapters();
}
