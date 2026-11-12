using System.IO;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace WhisperWriter.Services.Whispers;

/// <summary>Production adapter retaining the Whisper.net downloader and inference path.</summary>
internal class NetRuntime : IRuntime {
	public string? ActiveLibrary => RuntimeOptions.LoadedLibrary?.ToString();

	public async Task DownloadModelAsync (string modelPath) {
		using var httpClient = new System.Net.Http.HttpClient();
		var downloader = new WhisperGgmlDownloader(httpClient);
		await using var source = await downloader.GetGgmlModelAsync(GgmlType.Medium, QuantizationType.NoQuantization);
		await using var destination = File.Create(modelPath);
		await source.CopyToAsync(destination);
	}

	public IRuntimeFactory CreateFactory (string modelPath) => new NetFactory(WhisperFactory.FromPath(modelPath));

	public bool IsModelLoadException (Exception exception) => exception is WhisperModelLoadException;
}