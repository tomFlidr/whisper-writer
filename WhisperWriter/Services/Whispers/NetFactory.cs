using System.IO;
using Whisper.net;

namespace WhisperWriter.Services.Whispers;

/// <summary>Adapts Whisper.net inference to the application's runtime factory contract.</summary>
internal class NetFactory : IRuntimeFactory {
	private readonly WhisperFactory _factory;

	public NetFactory (WhisperFactory factory) {
		this._factory = factory;
	}

	public async IAsyncEnumerable<string> ProcessAsync (Stream audio, string language, int threadCount, string? prompt) {
		var builder = this._factory.CreateBuilder()
			.WithLanguage(language)
			.WithThreads(threadCount);

		if (prompt is not null)
			builder = builder.WithPrompt(prompt);

		await using var processor = builder.Build();
		await foreach (var segment in processor.ProcessAsync(audio))
			yield return segment.Text;
	}

	public void Dispose () => this._factory.Dispose();
}