namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Current local-video-memory budget for one DXGI adapter.</summary>
public record DxgiAdapterBudget (
	string DeviceKey,
	string Description,
	uint VendorId,
	uint DeviceId,
	ulong DedicatedVideoMemoryBytes,
	ulong BudgetBytes,
	ulong CurrentUsageBytes,
	bool IsSoftware
);