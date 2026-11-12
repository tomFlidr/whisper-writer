using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Combines live DXGI adapter budgets and system RAM availability for inference admission.</summary>
public class WindowsMonitor : IMonitor, IService, ISingleton {
	private const uint NvidiaVendorId = 0x10DE;
	private const ulong IntegratedVideoMemoryThresholdBytes = 512UL * 1024UL * 1024UL;
	private readonly IDxgiBudgetSource _dxgi;
	private readonly ISystemBudgetSource _system;

	public WindowsMonitor () : this(new WindowsDxgiBudgetSource(), new WindowsSystemBudgetSource()) {
	}

	public WindowsMonitor (IDxgiBudgetSource dxgi, ISystemBudgetSource system) {
		ArgumentNullException.ThrowIfNull(dxgi);
		ArgumentNullException.ThrowIfNull(system);
		this._dxgi = dxgi;
		this._system = system;
	}

	private static InferenceMemorySnapshot systemSnapshot (string provider, SystemBudget memory) =>
		new(provider, "system-ram", null, null,
			WindowsMonitor.toLong(memory.AvailablePhysicalBytes),
			WindowsMonitor.toLong(memory.TotalPhysicalBytes));

	private static long? toLong (ulong? value) => value is null ? null : (long)Math.Min((ulong)long.MaxValue, value.Value);

	public InferenceMemorySnapshot GetSnapshot (string? provider) {
		string activeProvider = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider;
		SystemBudget system = this._system.GetSnapshot();
		if (activeProvider.Equals("cpu", StringComparison.OrdinalIgnoreCase) ||
			activeProvider.Equals("cpu-no-avx", StringComparison.OrdinalIgnoreCase) ||
			activeProvider.Equals("CpuNoAvx", StringComparison.OrdinalIgnoreCase))
			return WindowsMonitor.systemSnapshot(activeProvider, system);

		IReadOnlyList<DxgiAdapterBudget> adapters;
		try {
			adapters = this._dxgi.GetAdapters().Where(adapter => !adapter.IsSoftware).ToArray();
		} catch {
			return WindowsMonitor.systemSnapshot(activeProvider, system);
		}

		IEnumerable<DxgiAdapterBudget> candidates = activeProvider.Equals("cuda", StringComparison.OrdinalIgnoreCase)
			? adapters.Where(adapter => adapter.VendorId == WindowsMonitor.NvidiaVendorId)
			: adapters;
		DxgiAdapterBudget? selected = candidates
			.OrderBy(adapter => Math.Max(0, adapter.BudgetBytes - Math.Min(adapter.BudgetBytes, adapter.CurrentUsageBytes)))
			.FirstOrDefault();
		if (selected is null)
			return WindowsMonitor.systemSnapshot(activeProvider, system);

		ulong budget = selected.BudgetBytes;
		ulong usage = selected.CurrentUsageBytes;
		if (selected.DedicatedVideoMemoryBytes < WindowsMonitor.IntegratedVideoMemoryThresholdBytes &&
			system.AvailablePhysicalBytes is ulong availableSystemMemory) {
			budget = Math.Min(budget, usage + availableSystemMemory);
		}

		return new InferenceMemorySnapshot(
			activeProvider,
			selected.DeviceKey,
			WindowsMonitor.toLong(budget),
			WindowsMonitor.toLong(usage),
			WindowsMonitor.toLong(system.AvailablePhysicalBytes),
			WindowsMonitor.toLong(system.TotalPhysicalBytes));
	}
}