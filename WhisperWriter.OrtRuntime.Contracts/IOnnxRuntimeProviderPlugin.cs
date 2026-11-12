namespace WhisperWriter.OrtRuntime.Contracts;

/// <summary>Provider plugin that creates ONNX Runtime sessions using its private ORT package version.</summary>
public interface IOnnxRuntimeProviderPlugin : IDisposable {
	string ProviderId { get; }
	IOnnxRuntimeSession CreateSession (string modelPath, OrtSessionSettings settings);
}