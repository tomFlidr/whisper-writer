using System.IO;
using Whisper.net;
using Whisper.net.Ggml;

namespace WhisperWriter.Services;

/// <summary>
/// Boundary around model acquisition and Whisper.net runtime operations.
/// </summary>
public interface IWhisperRuntime {
	int? DetectCudaVersion();
	Task DownloadModelAsync(string modelPath);
	IWhisperFactory CreateFactory(string modelPath);
	bool IsModelLoadException(Exception exception);
}

/// <summary>
/// Loaded model factory used to run one or more transcription requests.
/// </summary>
public interface IWhisperFactory : IDisposable {
	IAsyncEnumerable<string> ProcessAsync(Stream audio, string language, int threadCount, string? prompt);
}

/// <summary>
/// Production adapter retaining the Whisper.net downloader and inference path.
/// </summary>
internal sealed class WhisperNetRuntime : IWhisperRuntime {
	public int? DetectCudaVersion() => global::WhisperWriter.Services.Whisper.DetectCudaVersion();

	public async Task DownloadModelAsync(string modelPath) {
		using var httpClient = new System.Net.Http.HttpClient();
		var downloader = new WhisperGgmlDownloader(httpClient);
		await using var source = await downloader.GetGgmlModelAsync(GgmlType.Medium, QuantizationType.NoQuantization);
		await using var destination = File.Create(modelPath);
		await source.CopyToAsync(destination);
	}

	public IWhisperFactory CreateFactory(string modelPath) => new WhisperNetFactory(WhisperFactory.FromPath(modelPath));

	public bool IsModelLoadException(Exception exception) => exception is WhisperModelLoadException;
}

internal sealed class WhisperNetFactory : IWhisperFactory {
	private readonly WhisperFactory factory;

	public WhisperNetFactory(WhisperFactory factory) {
		this.factory = factory;
	}

	public async IAsyncEnumerable<string> ProcessAsync(Stream audio, string language, int threadCount, string? prompt) {
		var builder = this.factory.CreateBuilder()
			.WithLanguage(language)
			.WithThreads(threadCount);

		if (prompt is not null)
			builder = builder.WithPrompt(prompt);

		await using var processor = builder.Build();
		await foreach (var segment in processor.ProcessAsync(audio))
			yield return segment.Text;
	}

	public void Dispose() => this.factory.Dispose();
}
