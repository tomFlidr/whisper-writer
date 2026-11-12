namespace WhisperWriter.OrtRuntime.Contracts;

/// <summary>Version-neutral ONNX Runtime session surface shared across provider load contexts.</summary>
public interface IOnnxRuntimeSession : IDisposable {
	IReadOnlyList<OrtTensorMetadata> Inputs { get; }
	IReadOnlyList<OrtTensorMetadata> Outputs { get; }
	IReadOnlyList<string> ActiveExecutionProviders { get; }
	bool SupportsConcurrentRuns { get; }
	string? FallbackReason { get; }
	int? CudaVersion { get; }
	IReadOnlyList<OrtTensorValue> Run (IReadOnlyList<OrtTensorValue> inputs);
}