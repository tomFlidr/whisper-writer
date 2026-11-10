using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>Converts live memory-budget readings into a conservative inference-slot limit.</summary>
public sealed class InferenceCapacityPolicy : IService, ISingleton {
	private const int _maximumSlots = 3;
	private const int _unknownMemoryFallbackSlots = 2;
	private const long _minimumGpuReserveBytes = 512L * 1024L * 1024L;
	private const long _minimumSystemReserveBytes = 1024L * 1024L * 1024L;
	private const long _defaultEstimatedSlotBytes = 512L * 1024L * 1024L;

	public int GetMaximumConcurrentJobs (
		int activeJobs,
		long estimatedSlotBytes,
		InferenceMemorySnapshot memory,
		int profileLimit = InferenceCapacityPolicy._maximumSlots
	) {
		ArgumentNullException.ThrowIfNull(memory);
		if (activeJobs < 0)
			throw new ArgumentOutOfRangeException(nameof(activeJobs));

		int configuredLimit = Math.Clamp(profileLimit, 1, InferenceCapacityPolicy._maximumSlots);
		if (activeJobs >= configuredLimit)
			return activeJobs;

		long slotBytes = Math.Max(estimatedSlotBytes, InferenceCapacityPolicy._defaultEstimatedSlotBytes);
		if (memory.VideoMemoryBudgetBytes is long gpuBudget &&
			memory.VideoMemoryUsageBytes is long gpuUsage && gpuBudget > 0) {
			long freeBytes = Math.Max(0, gpuBudget - Math.Min(gpuBudget, gpuUsage));
			long reserveBytes = Math.Max(InferenceCapacityPolicy._minimumGpuReserveBytes, gpuBudget / 10);
			return InferenceCapacityPolicy.calculateLimit(activeJobs, configuredLimit, freeBytes, reserveBytes, slotBytes);
		}

		if (memory.AvailableSystemMemoryBytes is long availableRam &&
			memory.TotalSystemMemoryBytes is long totalRam && totalRam > 0) {
			long reserveBytes = Math.Max(InferenceCapacityPolicy._minimumSystemReserveBytes, totalRam / 10);
			return InferenceCapacityPolicy.calculateLimit(activeJobs, configuredLimit, availableRam, reserveBytes, slotBytes);
		}

		return Math.Min(configuredLimit, Math.Max(activeJobs, InferenceCapacityPolicy._unknownMemoryFallbackSlots));
	}

	private static int calculateLimit (int activeJobs, int configuredLimit, long freeBytes, long reserveBytes, long slotBytes) {
		long spendableBytes = Math.Max(0, freeBytes - reserveBytes);
		long additionalSlots = spendableBytes / slotBytes;
		return (int)Math.Min(configuredLimit, activeJobs + additionalSlots);
	}
}
