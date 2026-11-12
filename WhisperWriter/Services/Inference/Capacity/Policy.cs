using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.Inference.Capacity;

/// <summary>Converts live memory-budget readings into a conservative inference-slot limit.</summary>
public class Policy : IService, ISingleton {
	private const int MaximumSlots = 3;
	private const int UnknownMemoryFallbackSlots = 2;
	private const long MinimumGpuReserveBytes = 512L * 1024L * 1024L;
	private const long MinimumSystemReserveBytes = 1024L * 1024L * 1024L;
	private const long DefaultEstimatedSlotBytes = 512L * 1024L * 1024L;

	private static int calculateLimit (int activeJobs, int configuredLimit, long freeBytes, long reserveBytes, long slotBytes) {
		long spendableBytes = Math.Max(0, freeBytes - reserveBytes);
		long additionalSlots = spendableBytes / slotBytes;
		return (int)Math.Min(configuredLimit, activeJobs + additionalSlots);
	}

	public int GetMaximumConcurrentJobs (
		int activeJobs,
		long estimatedSlotBytes,
		InferenceMemorySnapshot memory,
		int profileLimit = Policy.MaximumSlots
	) {
		ArgumentNullException.ThrowIfNull(memory);
		if (activeJobs < 0)
			throw new ArgumentOutOfRangeException(nameof(activeJobs));

		int configuredLimit = Math.Clamp(profileLimit, 1, Policy.MaximumSlots);
		if (activeJobs >= configuredLimit)
			return activeJobs;

		long slotBytes = Math.Max(estimatedSlotBytes, Policy.DefaultEstimatedSlotBytes);
		if (memory.VideoMemoryBudgetBytes is long gpuBudget &&
			memory.VideoMemoryUsageBytes is long gpuUsage && gpuBudget > 0) {
			long freeBytes = Math.Max(0, gpuBudget - Math.Min(gpuBudget, gpuUsage));
			long reserveBytes = Math.Max(Policy.MinimumGpuReserveBytes, gpuBudget / 10);
			return Policy.calculateLimit(activeJobs, configuredLimit, freeBytes, reserveBytes, slotBytes);
		}

		if (memory.AvailableSystemMemoryBytes is long availableRam &&
			memory.TotalSystemMemoryBytes is long totalRam && totalRam > 0) {
			long reserveBytes = Math.Max(Policy.MinimumSystemReserveBytes, totalRam / 10);
			return Policy.calculateLimit(activeJobs, configuredLimit, availableRam, reserveBytes, slotBytes);
		}

		return Math.Min(configuredLimit, Math.Max(activeJobs, Policy.UnknownMemoryFallbackSlots));
	}
}