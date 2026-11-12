using System.IO;
using WhisperWriter.Services;
using WhisperWriter.Services.Inference.Capacity;
using Xunit;

namespace WhisperWriter.Tests.Services.Inference.Capacity;

public class ProfileStoreTests {
	[Fact]
	public void SetProfile_PersistsTheSafeSlotLimitAndMemoryEstimateAcrossInstances () {
		string directory = Path.Combine(AppContext.BaseDirectory, "phase2-capacity", Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "profiles.json");
		var profile = new Profile(2, 593_494_016, DateTimeOffset.UtcNow);
		try {
			var first = new ProfileStore(path);
			first.Set("cuda|model-medium|adapter-a", profile);

			var reopened = new ProfileStore(path);
			Assert.True(reopened.TryGet("cuda|model-medium|adapter-a", out Profile? loaded));
			Assert.Equal(profile.MaxConcurrentJobs, loaded!.MaxConcurrentJobs);
			Assert.Equal(profile.EstimatedSlotBytes, loaded.EstimatedSlotBytes);
			Assert.Equal(profile.UpdatedAt, loaded.UpdatedAt);
		} finally {
			if (Directory.Exists(directory))
				Directory.Delete(directory, recursive: true);
		}
	}
}