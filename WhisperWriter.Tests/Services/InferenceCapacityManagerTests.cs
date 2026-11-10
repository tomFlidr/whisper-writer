using System.IO;
using WhisperWriter.Services;
using WhisperWriter.Utils.Enums;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class InferenceCapacityManagerTests {
	[Fact]
	public void ResourceFailureFollowedBySuccessfulRetry_PersistsTheLowerLimitForTheSameProfile() {
		string path = Path.Combine(AppContext.BaseDirectory, "phase2-capacity-manager", $"{Guid.NewGuid():N}.json");
		try {
			var store = new InferenceCapacityProfileStore(path);
			var monitor = new FakeMemoryMonitor(new InferenceMemorySnapshot("Cuda", "device-a", null, null, null, null));
			var transcription = new FakeTranscription();
			var manager = new InferenceCapacityManager(
				transcription,
				monitor,
				new InferenceCapacityPolicy(),
				store);
			var job = new TranscriptionJob {
				Id = Guid.NewGuid(),
				AudioBytes = [1, 2, 3],
				ModelPath = "medium.bin",
			};

			Assert.Equal(1, manager.ReportResourceFailure(job, 2));
			manager.ReportSuccess(job with { RetryCount = 1 }, 1);

			Assert.Equal(1, manager.GetMaximumConcurrentJobs(job, 0));
	} finally {
		string? directory = Path.GetDirectoryName(path);
		if (directory is not null && Directory.Exists(directory))
			Directory.Delete(directory, recursive: true);
		}
	}

	private sealed class FakeMemoryMonitor(InferenceMemorySnapshot snapshot) : IInferenceMemoryMonitor {
		public InferenceMemorySnapshot GetSnapshot(string? provider) => snapshot;
	}

	private sealed class FakeTranscription : ITranscription {
		public event Action<TranscriptionState, string>? StateChanged {
			add { }
			remove { }
		}
		public string? ActiveProvider => "Cuda";
		public Task InitializeAsync(string modelPath) => Task.CompletedTask;
		public Task<string> TranscribeAsync(byte[] wavBytes, string language, string prompt) => Task.FromResult(string.Empty);
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
