using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class InferenceCapacityPolicyTests {
	private const long MiB = 1024L * 1024L;
	private const long GiB = 1024L * MiB;

	[Fact]
	public void GetMaximumConcurrentJobs_BlocksThirdSlotAtTheMeasuredQuadroBudget() {
		var policy = new InferenceCapacityPolicy();
		var memory = new InferenceMemorySnapshot(
			Provider: "Cuda",
			DeviceKey: "10DE:1FB8:local",
			VideoMemoryBudgetBytes: 3_507_224_576,
			VideoMemoryUsageBytes: 2_778_726_400,
			AvailableSystemMemoryBytes: null,
			TotalSystemMemoryBytes: null
		);

		int limit = policy.GetMaximumConcurrentJobs(
			activeJobs: 2,
			estimatedSlotBytes: 593_494_016,
			memory,
			profileLimit: 3);

		Assert.Equal(2, limit);
	}

	[Fact]
	public void GetMaximumConcurrentJobs_AllowsThirdSlotWhenOtherJobsLeaveBudgetHeadroom() {
		var policy = new InferenceCapacityPolicy();
		var memory = new InferenceMemorySnapshot(
			Provider: "Cuda",
			DeviceKey: "10DE:1FB8:high-memory",
			VideoMemoryBudgetBytes: 8 * GiB,
			VideoMemoryUsageBytes: 2_778_726_400,
			AvailableSystemMemoryBytes: null,
			TotalSystemMemoryBytes: null
		);

		int limit = policy.GetMaximumConcurrentJobs(
			activeJobs: 2,
			estimatedSlotBytes: 593_494_016,
			memory,
			profileLimit: 3);

		Assert.Equal(3, limit);
	}

	[Fact]
	public void GetMaximumConcurrentJobs_UsesAvailableSystemMemoryForCpuInference() {
		var policy = new InferenceCapacityPolicy();
		var memory = new InferenceMemorySnapshot(
			Provider: "Cpu",
			DeviceKey: "cpu-x64",
			VideoMemoryBudgetBytes: null,
			VideoMemoryUsageBytes: null,
			AvailableSystemMemoryBytes: 12 * GiB,
			TotalSystemMemoryBytes: 16 * GiB
		);

		int limit = policy.GetMaximumConcurrentJobs(
			activeJobs: 2,
			estimatedSlotBytes: 593_494_016,
			memory,
			profileLimit: 3);

		Assert.Equal(3, limit);
	}

	[Fact]
	public void GetMaximumConcurrentJobs_DoesNotCancelActiveJobsWhenUsageExceedsBudget() {
		var policy = new InferenceCapacityPolicy();
		var memory = new InferenceMemorySnapshot(
			Provider: "Cuda",
			DeviceKey: "10DE:1FB8:local",
			VideoMemoryBudgetBytes: 3_507_224_576,
			VideoMemoryUsageBytes: 3_800_000_000,
			AvailableSystemMemoryBytes: null,
			TotalSystemMemoryBytes: null
		);

		int limit = policy.GetMaximumConcurrentJobs(
			activeJobs: 2,
			estimatedSlotBytes: 593_494_016,
			memory,
			profileLimit: 3);

		Assert.Equal(2, limit);
	}

	[Fact]
	public void GetMaximumConcurrentJobs_UsesConservativeFallbackWhenNoMemorySignalExists() {
		var policy = new InferenceCapacityPolicy();
		var memory = new InferenceMemorySnapshot(
			Provider: "Unknown",
			DeviceKey: "unknown",
			VideoMemoryBudgetBytes: null,
			VideoMemoryUsageBytes: null,
			AvailableSystemMemoryBytes: null,
			TotalSystemMemoryBytes: null
		);

		int limit = policy.GetMaximumConcurrentJobs(
			activeJobs: 0,
			estimatedSlotBytes: 593_494_016,
			memory,
			profileLimit: 3);

		Assert.Equal(2, limit);
	}
}
