using System.IO;
using WhisperWriter.Contracts;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.Whispers;
using WhisperWriter.Utils.Enums;
using Transcriber = WhisperWriter.Services.Whisper;
using Xunit;

namespace WhisperWriter.Tests.Services.Whispers;

public class TranscriberTests {
	private static readonly Log _log = new();

	private static List<(TranscriptionState State, string Message)> captureStates (Transcriber whisper) {
		var states = new List<(TranscriptionState, string)>();
		whisper.StateChanged += (state, message) => states.Add((state, message));
		return states;
	}

	[Fact]
	public void GetInferenceThreadCount_LeavesTwoProcessorsFreeAndAtLeastOneActive () {
		int expected = Math.Max(1, Environment.ProcessorCount - 2);

		Assert.Equal(expected, Transcriber.GetInferenceThreadCount());
	}

	[Fact]
	public async Task TranscribeAsync_WhenNotInitializedThrowsInvalidOperationException () {
		var whisper = new Transcriber();

		InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => whisper.TranscribeAsync([], "en", ""));

		Assert.Equal("Data is not loaded. Check the model path in Data.", exception.Message);
	}

	[Fact]
	public async Task InitializeAsync_WhenModelIsMissingReportsLoadingDownloadAndReady () {
		using var model = new TemporaryModel();
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
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
	public async Task InitializeAsync_WhenExistingModelIsLargeEnoughSkipsDownload () {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(Transcriber.MinModelFileSizeBytes);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
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
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
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
	public async Task InitializeAsync_WhenFactoryCreationFailsReportsErrorAndRemainsUninitialized () {
		using var model = new TemporaryModel();
		var runtime = new FakeRuntime {
			FactoryCreationException = new InvalidOperationException("factory failed"),
		};
		var whisper = new TestTranscriber(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Loading, "Downloading model…"),
			(TranscriptionState.Error, "Data load failed: factory failed"),
		}, states);
		await Assert.ThrowsAsync<InvalidOperationException>(() => whisper.TranscribeAsync([], "en", ""));
	}

	[Fact]
	public async Task InitializeAsync_UsesNeutralLoadingStatusWhileTheModelLoads () {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(Transcriber.MinModelFileSizeBytes);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		var states = captureStates(whisper);

		await whisper.InitializeAsync(model.Path);

		Assert.Equal(new[] {
			(TranscriptionState.Loading, "Loading model…"),
			(TranscriptionState.Idle, "Ready"),
		}, states);
	}

	[Fact]
	public async Task InitializeAsync_ReportsTheBackendFromTheLoadedWhisperLibrary () {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(Transcriber.MinModelFileSizeBytes);
		var runtime = new FakeRuntime { ActiveLibrary = "Vulkan" };
		var whisper = new TestTranscriber(runtime, model.Path);
		ITranscription transcription = whisper;

		await whisper.InitializeAsync(model.Path);

		ActiveBackendInfo backend = Assert.IsType<ActiveBackendInfo>(transcription.ActiveBackend);
		Assert.Equal("gpu", backend.Device);
		Assert.Equal("whisper", backend.Engine);
		Assert.Equal("whisper.net", backend.Runtime);
		Assert.Equal(new[] { "vulkan" }, backend.Providers);
		Assert.Null(backend.CudaVersion);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" 	")]
	[InlineData("auto")]
	public async Task TranscribeAsync_UsesEnglishWhenLanguageIsNullBlankOrAuto (string? language) {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], language!, "");

		Assert.Equal("en", runtime.Factory.Language);
	}

	[Fact]
	public async Task TranscribeAsync_PassesNonAutoLanguageThroughUnchanged () {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "cs-CZ", "");

		Assert.Equal("cs-CZ", runtime.Factory.Language);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public async Task TranscribeAsync_OmitsNullOrEmptyPrompt (string? prompt) {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "en", prompt!);

		Assert.Null(runtime.Factory.Prompt);
	}

	[Fact]
	public async Task TranscribeAsync_PassesNonEmptyPromptThroughUnchanged () {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.TranscribeAsync([], "en", "  domain vocabulary  ");

		Assert.Equal("  domain vocabulary  ", runtime.Factory.Prompt);
	}

	[Fact]
	public async Task TranscribeAsync_ConcatenatesSegmentsAndTrimsTheResult () {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		runtime.Factory.Segments = [" 	Hello", " world\n "];
		var whisper = new TestTranscriber(runtime, model.Path);
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
		Assert.Equal(Transcriber.GetInferenceThreadCount(), runtime.Factory.ThreadCount);
	}

	[Fact]
	public async Task TranscribeAsync_WhenModelIsCorruptReportsErrorDisposesFactoryAndDeletesFile () {
		using var model = new TemporaryModel();
		using (var file = File.Create(model.Path))
			file.SetLength(Transcriber.MinModelFileSizeBytes);
		var runtime = new FakeRuntime();
		runtime.Factory.ProcessException = new FakeModelLoadException("corrupt");
		var whisper = new TestTranscriber(runtime, model.Path);
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
	public async Task DisposeAsync_DisposesTheInitializedFactory () {
		using var model = new TemporaryModel(withModelFile: true);
		var runtime = new FakeRuntime();
		var whisper = new TestTranscriber(runtime, model.Path);
		await whisper.InitializeAsync(model.Path);

		await whisper.DisposeAsync();

		Assert.True(runtime.Factory.IsDisposed);
	}

	private class TestTranscriber : Transcriber {
		public TestTranscriber (IRuntime runtime, string modelPath) : base(runtime) {
			this.settings = new TestSettings(modelPath);
			this.log = TranscriberTests._log;
		}
	}

	private class TestSettings : Settings {
		public TestSettings (string modelPath) : base(Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.json")) {
			this.Data.ModelPath = modelPath;
			this.log = TranscriberTests._log;
		}
	}

	private class TemporaryModel : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			"phase0-tests",
			$"{Guid.NewGuid():N}.bin");

		public TemporaryModel (bool withModelFile = false) {
			if (withModelFile) {
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(this.Path)!);
				File.WriteAllText(this.Path, "fake model data");
			}
		}

		public void Dispose () {
			if (File.Exists(this.Path))
				File.Delete(this.Path);
		}
	}

	private class FakeRuntime : IRuntime {
		public int DownloadCount { get; private set; }
		public bool ModelExistedAtDownloadStart { get; private set; }
		public string? CreatedFactoryPath { get; private set; }
		public Exception? FactoryCreationException { get; init; }
		public string? ActiveLibrary { get; init; }
		public FakeFactory Factory { get; } = new();

		public Task DownloadModelAsync (string modelPath) {
			this.DownloadCount++;
			this.ModelExistedAtDownloadStart = File.Exists(modelPath);
			File.WriteAllText(modelPath, "fake model data");
			return Task.CompletedTask;
		}

		public IRuntimeFactory CreateFactory (string modelPath) {
			if (this.FactoryCreationException is not null)
				throw this.FactoryCreationException;
			this.CreatedFactoryPath = modelPath;
			return this.Factory;
		}

		public bool IsModelLoadException (Exception exception) => exception is FakeModelLoadException;
	}

	private class FakeFactory : IRuntimeFactory {
		public IReadOnlyList<string> Segments { get; set; } = [];
		public Exception? ProcessException { get; set; }
		public string? Language { get; private set; }
		public int ThreadCount { get; private set; }
		public string? Prompt { get; private set; }
		public long AudioLength { get; private set; }
		public bool IsDisposed { get; private set; }

		public IAsyncEnumerable<string> ProcessAsync (Stream audio, string language, int threadCount, string? prompt) {
			this.Language = language;
			this.ThreadCount = threadCount;
			this.Prompt = prompt;
			this.AudioLength = audio.Length;
			return this.enumerateSegments();
		}

		public void Dispose () {
			this.IsDisposed = true;
		}

		private async IAsyncEnumerable<string> enumerateSegments () {
			await Task.Yield();
			if (this.ProcessException is not null)
				throw this.ProcessException;
			foreach (string segment in this.Segments)
				yield return segment;
		}
	}

	private class FakeModelLoadException : Exception {
		public FakeModelLoadException (string message) : base(message) { }
	}

}