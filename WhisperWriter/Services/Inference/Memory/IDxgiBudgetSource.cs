namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Boundary for querying DXGI adapter budgets without coupling policy tests to COM.</summary>
public interface IDxgiBudgetSource {
	IReadOnlyList<DxgiAdapterBudget> GetAdapters ();
}