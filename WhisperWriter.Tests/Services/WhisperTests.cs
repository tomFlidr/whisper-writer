using System.IO;
using WhisperWriter.Services;
using WhisperWriter.Utils.Enums;
using WhisperService = WhisperWriter.Services.Whisper;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class WhisperTests {
	[Fact]
	public void DetectCudaVersion_ReturnsOnlySupportedMajorVersions () {
		int? version = WhisperService.DetectCudaVersion();

		Assert.True(version is null or 11 or 12 or 13, $"Unexpected detected CUDA version: {version}");
	}

	[Fact]
	public void GetInferenceThreadCount_LeavesTwoProcessorsFreeAndAtLeastOneActive() {
		int expected = Math.Max(1, Environment.ProcessorCount - 2);

		Assert.Equal(expected, WhisperService.GetInferenceThreadCount());
	}

	[Fact]
	public async Task TranscribeAsync_WhenNotInitializedThrowsInvalidOperationException() {
		var whisper = new WhisperService();

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => whisper.TranscribeAsync([], "en", ""));

		Assert.Equal("Data is not loaded. Check the model path in Data.", exception.Message);
	}

	[Fact]
	public async Task InitializeAsync_WhenModelIsMissingReportsLoadingDownloadAndReady() {
		using var model = new TemporaryModel();
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Loading, "Downloading model…"),
			(TranscriptionState.Idle, "Ready"),
		}, states);
		Assert.Equal(1, runtime.DownloadCount);
		Assert.Equal(model.Path, runtime.CreatedFactoryPath);
		Assert.True(File.Exists(model.Path));
	}

	[Fact]
	public async Task InitializeAsync_WhenExistingModelIsLargeEnoughSkipsDownload() {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(WhisperService.MinModelFileSizeBytes);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Idle, "Ready"),
		}, states);
		Assert.Equal(0, runtime.DownloadCount);
		Assert.Equal(model.Path, runtime.CreatedFactoryPath);
	}

	[Fact]
	public async Task InitializeAsync_DeletesAnUndersizedModelBeforeDownloadingReplacement () {
		using var model = new TemporaryModel();
		File.WriteAllText(model.Path, "truncated model");
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Loading, "Downloading model…"),
			(TranscriptionState.Idle, "Ready"),
		}, states);
		Assert.Equal(1, runtime.DownloadCount);
		Assert.False(runtime.ModelExistedAtDownloadStart);
		Assert.Equal("fake model data", File.ReadAllText(model.Path));
	}

	[Fact]
	public async Task InitializeAsync_WhenFactoryCreationFailsReportsErrorAndRemainsUninitialized() {
		using var model = new TemporaryModel();
		var runtime = new FakeWhisperRuntime {
			FactoryCreationException = new InvalidOperationException("factory failed"),
		};
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Loading, "Downloading model…"),
			(TranscriptionState.Error, "Data load failed: factory failed"),
		}, states);
		await Assert.ThrowsAsync<InvalidOperationException>(() => whisper.TranscribeAsync([], "en", ""));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" 	")]
	[InlineData("auto")]
	public async Task TranscribeAsync_UsesEnglishWhenLanguageIsNullBlankOrAuto(string? language) {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], language!, "");

		Assert.Equal("en", runtime.Factory.Language);
	}

	[Fact]
	public async Task TranscribeAsync_PassesNonAutoLanguageThroughUnchanged() {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "cs-CZ", "");

		Assert.Equal("cs-CZ", runtime.Factory.Language);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public async Task TranscribeAsync_OmitsNullOrEmptyPrompt(string? prompt) {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "en", prompt!);

		Assert.Null(runtime.Factory.Prompt);
	}

	[Fact]
	public async Task TranscribeAsync_PassesNonEmptyPromptThroughUnchanged() {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "en", "  domain vocabulary  ");

		Assert.Equal("  domain vocabulary  ", runtime.Factory.Prompt);
	}

	[Fact]
	public async Task TranscribeAsync_ConcatenatesSegmentsAndTrimsTheResult() {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		runtime.Factory.Segments = [" 	Hello", " world\n "];
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);
		await whisper.InitializeAsync(model.Path);
		states.Clear();

		string result = await whisper.TranscribeAsync([1, 2, 3], "en", "");

		Assert.Equal("Hello world", result);
		Assert.Equal(new[] {
			(TranscriptionState.Transcribing, "Transcribing…"),
			(TranscriptionState.Done, "Hello world"),
		}, states);
		Assert.Equal(3, runtime.Factory.AudioLength);
		Assert.Equal(WhisperService.GetInferenceThreadCount(), runtime.Factory.ThreadCount);
	}

	[Fact]
	public async Task TranscribeAsync_WhenModelIsCorruptReportsErrorDisposesFactoryAndDeletesFile() {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(WhisperService.MinModelFileSizeBytes);
		var runtime = new FakeWhisperRuntime();
		runtime.Factory.ProcessException = new FakeModelLoadException("corrupt");
		var whisper = new TestWhisper(runtime, model.Path);
		var states = captureStates(whisper);
		await whisper.InitializeAsync(model.Path);
		Assert.Equal(0, runtime.DownloadCount);
		states.Clear();

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => whisper.TranscribeAsync([], "en", ""));

		Assert.Equal("Data file is corrupted and has been deleted. Restart the app to re-download it.", exception.Message);
		Assert.IsType<FakeModelLoadException>(exception.InnerException);
		Assert.False(File.Exists(model.Path));
		Assert.True(runtime.Factory.IsDisposed);
		Assert.Equal(new[] {
			(TranscriptionState.Transcribing, "Transcribing…"),
			(TranscriptionState.Error, exception.Message),
		}, states);
	}

	[Fact]
	public async Task DisposeAsync_DisposesTheInitializedFactory() {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeWhisperRuntime();
		var whisper = new TestWhisper(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.DisposeAsync();

		Assert.True(runtime.Factory.IsDisposed);
	}

	private static List<(TranscriptionState State, string Message)> captureStates(WhisperService whisper) {
		var states = new List<(TranscriptionState, string)>();
		whisper.StateChanged += (state, message) => states.Add((state, message));
		return states;
	}

	private sealed class TestWhisper : WhisperService {
		public TestWhisper(IWhisperRuntime runtime, string modelPath) : base(runtime) {
			this.settings = new TestSettings(modelPath);
			this.log = WhisperTests._log;
		}
	}

	private sealed class TestSettings : Settings {
		public TestSettings(string modelPath) : base(Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.json")) {
			this.Data.ModelPath = modelPath;
			this.log = WhisperTests._log;
		}
	}

	private sealed class TemporaryModel : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			"phase0-tests",
			$"{Guid.NewGuid():N}.bin");

		public TemporaryModel(bool withModelFile = false) {
			if (withModelFile) {
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(this.Path)!);
				File.WriteAllText(this.Path, "fake model data");
			}
		}

		public void Dispose() {
			if (File.Exists(this.Path))
				File.Delete(this.Path);
		}
	}

	private sealed class FakeWhisperRuntime : IWhisperRuntime {
		public int DownloadCount { get; private set; }
		public bool ModelExistedAtDownloadStart { get; private set; }
		public string? CreatedFactoryPath { get; private set; }
		public Exception? FactoryCreationException { get; init; }
		public FakeWhisperFactory Factory { get; } = new();

		public int? DetectCudaVersion() => null;

		public Task DownloadModelAsync(string modelPath) {
			this.DownloadCount++;
			this.ModelExistedAtDownloadStart = File.Exists(modelPath);
			File.WriteAllText(modelPath, "fake model data");
			return Task.CompletedTask;
		}

		public IWhisperFactory CreateFactory(string modelPath) {
			if (this.FactoryCreationException is not null)
				throw this.FactoryCreationException;
			this.CreatedFactoryPath = modelPath;
			return this.Factory;
		}

		public bool IsModelLoadException(Exception exception) => exception is FakeModelLoadException;
	}

	private sealed class FakeWhisperFactory : IWhisperFactory {
		public IReadOnlyList<string> Segments { get; set; } = [];
		public Exception? ProcessException { get; set; }
		public string? Language { get; private set; }
		public int ThreadCount { get; private set; }
		public string? Prompt { get; private set; }
		public long AudioLength { get; private set; }
		public bool IsDisposed { get; private set; }

		public IAsyncEnumerable<string> ProcessAsync(Stream audio, string language, int threadCount, string? prompt) {
			this.Language = language;
			this.ThreadCount = threadCount;
			this.Prompt = prompt;
			this.AudioLength = audio.Length;
			return this.enumerateSegments();
		}

		private async IAsyncEnumerable<string> enumerateSegments() {
			await Task.Yield();
			if (this.ProcessException is not null)
				throw this.ProcessException;
			foreach (string segment in this.Segments)
				yield return segment;
		}

		public void Dispose() {
			this.IsDisposed = true;
		}
	}

	private sealed class FakeModelLoadException : Exception {
		public FakeModelLoadException(string message) : base(message) { }
	}

	private static readonly Log _log = new();
}
