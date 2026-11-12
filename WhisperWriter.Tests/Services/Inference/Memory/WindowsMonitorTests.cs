using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.Inference.Memory;
using Xunit;

namespace WhisperWriter.Tests.Services.Inference.Memory;

public class WindowsMonitorTests {
	[Fact]
	public void GetSnapshot_CudaUsesTheMostConstrainedNvidiaAdapter () {
		var dxgi = new FakeDxgiMemoryBudgetSource([
			new DxgiAdapterBudget("intel-luid", "Intel UHD", 0x8086, 0x9BC4, 134_217_728, 50_440_003_584, 500_000_000, false),
			new DxgiAdapterBudget("nvidia-busy", "Quadro T2000", 0x10DE, 0x1FB8, 4_126_146_560, 3_507_224_576, 3_372_220_416, false),
			new DxgiAdapterBudget("nvidia-idle", "Second NVIDIA", 0x10DE, 0x2684, 8_589_934_592, 7_500_000_000, 500_000_000, false),
			new DxgiAdapterBudget("software", "Microsoft Basic Render Driver", 0x1414, 0x008C, 0, 50_440_003_584, 0, true),
		]);
		var system = new FakeSystemBudgetSource(new SystemBudget(32_000_000_000, 24_000_000_000));
		var monitor = new WindowsMonitor(dxgi, system);

		InferenceMemorySnapshot snapshot = monitor.GetSnapshot("Cuda");

		Assert.Equal("Cuda", snapshot.Provider);
		Assert.Equal("nvidia-busy", snapshot.DeviceKey);
		Assert.Equal(3_507_224_576, snapshot.VideoMemoryBudgetBytes);
		Assert.Equal(3_372_220_416, snapshot.VideoMemoryUsageBytes);
		Assert.Equal(24_000_000_000, snapshot.AvailableSystemMemoryBytes);
	}

	[Fact]
	public void GetSnapshot_CpuUsesSystemMemoryInsteadOfGpuAdapterBudget () {
		var dxgi = new FakeDxgiMemoryBudgetSource([]);
		var system = new FakeSystemBudgetSource(new SystemBudget(16_000_000_000, 7_000_000_000));
		var monitor = new WindowsMonitor(dxgi, system);

		InferenceMemorySnapshot snapshot = monitor.GetSnapshot("Cpu");

		Assert.Equal("Cpu", snapshot.Provider);
		Assert.Equal(7_000_000_000, snapshot.AvailableSystemMemoryBytes);
		Assert.Equal(16_000_000_000, snapshot.TotalSystemMemoryBytes);
		Assert.Null(snapshot.VideoMemoryBudgetBytes);
	}

	[Fact]
	public void GetSnapshot_CpuNoAvxStableProviderIdUsesSystemMemoryInsteadOfGpuAdapterBudget () {
		var dxgi = new FakeDxgiMemoryBudgetSource([
			new DxgiAdapterBudget("nvidia", "NVIDIA GPU", 0x10DE, 0x1FB8, 4_000_000_000,
				3_000_000_000, 500_000_000, false),
		]);
		var system = new FakeSystemBudgetSource(new SystemBudget(16_000_000_000, 7_000_000_000));
		var monitor = new WindowsMonitor(dxgi, system);

		InferenceMemorySnapshot snapshot = monitor.GetSnapshot("cpu-no-avx");

		Assert.Equal("cpu-no-avx", snapshot.Provider);
		Assert.Equal("system-ram", snapshot.DeviceKey);
		Assert.Equal(7_000_000_000, snapshot.AvailableSystemMemoryBytes);
		Assert.Null(snapshot.VideoMemoryBudgetBytes);
	}

	private class FakeDxgiMemoryBudgetSource (IReadOnlyList<DxgiAdapterBudget> adapters) : IDxgiBudgetSource {
		public IReadOnlyList<DxgiAdapterBudget> GetAdapters () => adapters;
	}

	private class FakeSystemBudgetSource (SystemBudget snapshot) : ISystemBudgetSource {
		public SystemBudget GetSnapshot () => snapshot;
	}
}