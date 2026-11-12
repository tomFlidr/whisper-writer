using System.IO;
using WhisperWriter.Contracts;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.Inference.Capacity;
using WhisperWriter.Utils.Enums;
using Xunit;

namespace WhisperWriter.Tests.Services.Inference.Capacity;

public class ManagerTests {
	[Fact]
	public void ResourceFailureFollowedBySuccessfulRetry_PersistsTheLowerLimitForTheSameProfile () {
		string path = Path.Combine(AppContext.BaseDirectory, "phase2-capacity-manager", $"{Guid.NewGuid():N}.json");
		try {
			var store = new ProfileStore(path);
			var monitor = new FakeMemoryMonitor(new InferenceMemorySnapshot("Cuda", "device-a", null, null, null, null));
			var transcription = new FakeTranscription();
			var manager = new Manager(
				transcription,
				monitor,
				new Policy(),
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

	[Fact]
	public void CapacityProfiles_AreSeparatedByTheLoadedCudaRuntimeVersion () {
		var store = new RecordingProfileStore();
		var monitor = new FakeMemoryMonitor(new InferenceMemorySnapshot("Cuda", "device-a", null, null, null, null));
		var transcription = new FakeTranscription {
			ActiveBackend = ActiveBackendInfo.FromWhisperNet("Cuda12"),
		};
		var manager = new Manager(
			transcription,
			monitor,
			new Policy(),
			store);
		var firstJob = new TranscriptionJob {
			Id = Guid.NewGuid(),
			AudioBytes = [1, 2, 3],
			ModelPath = "medium.bin",
		};

		manager.ReportResourceFailure(firstJob, 2);
		manager.ReportSuccess(firstJob with { RetryCount = 1 }, 1);
		string firstFingerprint = Assert.Single(store.Profiles.Keys);

		transcription.ActiveBackend = ActiveBackendInfo.FromWhisperNet("Cuda");
		var secondJob = firstJob with { Id = Guid.NewGuid() };
		manager.ReportResourceFailure(secondJob, 2);
		manager.ReportSuccess(secondJob with { RetryCount = 1 }, 1);

		Assert.Equal(2, store.Profiles.Count);
		Assert.DoesNotContain(firstFingerprint, store.Profiles.Keys.Skip(1));
	}

	private class FakeMemoryMonitor (InferenceMemorySnapshot snapshot) : IMonitor {
		public InferenceMemorySnapshot GetSnapshot (string? provider) => snapshot;
	}

	private class RecordingProfileStore : IProfileStore {
		public Dictionary<string, Profile> Profiles { get; } = new(StringComparer.Ordinal);

		public bool TryGet (string fingerprint, out Profile? profile) =>
			this.Profiles.TryGetValue(fingerprint, out profile);

		public void Set (string fingerprint, Profile profile) =>
			this.Profiles[fingerprint] = profile;
	}

	private class FakeTranscription : ITranscription {
		public event Action<TranscriptionState, string>? StateChanged {
			add { }
			remove { }
		}
		public ActiveBackendInfo? ActiveBackend { get; set; } = ActiveBackendInfo.FromWhisperNet("Cuda");
		public Task InitializeAsync (string modelPath) => Task.CompletedTask;
		public Task<string> TranscribeAsync (byte[] wavBytes, string language, string prompt) => Task.FromResult(string.Empty);
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
	}
}