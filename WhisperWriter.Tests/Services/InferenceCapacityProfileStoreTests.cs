using System.IO;
using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class InferenceCapacityProfileStoreTests {
	[Fact]
	public void SetProfile_PersistsTheSafeSlotLimitAndMemoryEstimateAcrossInstances() {
		string directory = Path.Combine(AppContext.BaseDirectory, "phase2-capacity", Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "profiles.json");
		var profile = new InferenceCapacityProfile(2, 593_494_016, DateTimeOffset.UtcNow);
		try {
			var first = new InferenceCapacityProfileStore(path);
			first.Set("cuda|model-medium|adapter-a", profile);

			var reopened = new InferenceCapacityProfileStore(path);
			Assert.True(reopened.TryGet("cuda|model-medium|adapter-a", out InferenceCapacityProfile? loaded));
			Assert.Equal(profile.MaxConcurrentJobs, loaded!.MaxConcurrentJobs);
			Assert.Equal(profile.EstimatedSlotBytes, loaded.EstimatedSlotBytes);
			Assert.Equal(profile.UpdatedAt, loaded.UpdatedAt);
		} finally {
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}
}
