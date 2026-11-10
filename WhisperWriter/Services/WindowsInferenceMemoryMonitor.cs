using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>Combines live DXGI adapter budgets and system RAM availability for inference admission.</summary>
public sealed class WindowsInferenceMemoryMonitor : IInferenceMemoryMonitor, IService, ISingleton {
	private const uint _nvidiaVendorId = 0x10DE;
	private const ulong _integratedVideoMemoryThresholdBytes = 512UL * 1024UL * 1024UL;
	private readonly IDxgiMemoryBudgetSource _dxgi;
	private readonly ISystemMemoryBudgetSource _system;

	public WindowsInferenceMemoryMonitor () : this(new WindowsDxgiMemoryBudgetSource(), new WindowsSystemMemoryBudgetSource()) {
	}

	public WindowsInferenceMemoryMonitor(IDxgiMemoryBudgetSource dxgi, ISystemMemoryBudgetSource system) {
		ArgumentNullException.ThrowIfNull(dxgi);
		ArgumentNullException.ThrowIfNull(system);
		this._dxgi = dxgi;
		this._system = system;
	}

	public InferenceMemorySnapshot GetSnapshot(string? provider) {
		string activeProvider = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider;
		SystemMemoryBudget system = this._system.GetSnapshot();
		if (activeProvider.Equals("Cpu", StringComparison.OrdinalIgnoreCase) ||
			activeProvider.Equals("CpuNoAvx", StringComparison.OrdinalIgnoreCase))
			return WindowsInferenceMemoryMonitor.systemSnapshot(activeProvider, system);

		IReadOnlyList<DxgiAdapterMemoryBudget> adapters;
		try {
			adapters = this._dxgi.GetAdapters().Where(adapter => !adapter.IsSoftware).ToArray();
		} catch {
			return WindowsInferenceMemoryMonitor.systemSnapshot(activeProvider, system);
		}

		IEnumerable<DxgiAdapterMemoryBudget> candidates = activeProvider.Equals("Cuda", StringComparison.OrdinalIgnoreCase)
			? adapters.Where(adapter => adapter.VendorId == WindowsInferenceMemoryMonitor._nvidiaVendorId)
			: adapters;
		DxgiAdapterMemoryBudget? selected = candidates
			.OrderBy(adapter => Math.Max(0, adapter.BudgetBytes - Math.Min(adapter.BudgetBytes, adapter.CurrentUsageBytes)))
			.FirstOrDefault();
		if (selected is null)
			return WindowsInferenceMemoryMonitor.systemSnapshot(activeProvider, system);

		ulong budget = selected.BudgetBytes;
		ulong usage = selected.CurrentUsageBytes;
		if (selected.DedicatedVideoMemoryBytes < WindowsInferenceMemoryMonitor._integratedVideoMemoryThresholdBytes &&
			system.AvailablePhysicalBytes is ulong availableSystemMemory) {
			budget = Math.Min(budget, usage + availableSystemMemory);
		}

		return new InferenceMemorySnapshot(
			activeProvider,
			selected.DeviceKey,
			WindowsInferenceMemoryMonitor.toLong(budget),
			WindowsInferenceMemoryMonitor.toLong(usage),
			WindowsInferenceMemoryMonitor.toLong(system.AvailablePhysicalBytes),
			WindowsInferenceMemoryMonitor.toLong(system.TotalPhysicalBytes));
	}

	private static InferenceMemorySnapshot systemSnapshot(string provider, SystemMemoryBudget memory) =>
		new(provider, "system-ram", null, null,
			WindowsInferenceMemoryMonitor.toLong(memory.AvailablePhysicalBytes),
			WindowsInferenceMemoryMonitor.toLong(memory.TotalPhysicalBytes));

	private static long? toLong(ulong? value) => value is null ? null : (long)Math.Min((ulong)long.MaxValue, value.Value);
}
