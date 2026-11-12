namespace WhisperWriter.Services.Whispers;

/// <summary>Boundary around model acquisition and Whisper.net runtime operations.</summary>
public interface IRuntime {
	string? ActiveLibrary => null;
	Task DownloadModelAsync (string modelPath);
	IRuntimeFactory CreateFactory (string modelPath);
	bool IsModelLoadException (Exception exception);
}