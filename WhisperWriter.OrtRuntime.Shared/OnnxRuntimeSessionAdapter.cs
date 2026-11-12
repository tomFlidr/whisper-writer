using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WhisperWriter.OrtRuntime.Contracts;

namespace WhisperWriter.OrtRuntime.Shared;

/// <summary>Adapts one provider-specific ONNX Runtime session to the shared host contract.</summary>
internal class OnnxRuntimeSessionAdapter : IOnnxRuntimeSession {
	public IReadOnlyList<OrtTensorMetadata> Inputs { get; }
	public IReadOnlyList<OrtTensorMetadata> Outputs { get; }
	public IReadOnlyList<string> ActiveExecutionProviders => this._activeExecutionProviders;
	public bool SupportsConcurrentRuns { get; }
	public string? FallbackReason { get; }
	public int? CudaVersion { get; }

	private readonly InferenceSession _session;
	private readonly bool _trackExecutionProviders;
	private readonly SemaphoreSlim _runSemaphore = new(1, 1);
	private IReadOnlyList<string> _activeExecutionProviders = Array.Empty<string>();
	private bool _profileCaptured;
	private bool _disposed;

	public OnnxRuntimeSessionAdapter (
		InferenceSession session,
		bool supportsConcurrentRuns,
		bool trackExecutionProviders,
		string? fallbackReason = null,
		int? cudaVersion = null
	) {
		this._session = session;
		this.SupportsConcurrentRuns = supportsConcurrentRuns;
		this._trackExecutionProviders = trackExecutionProviders;
		this.FallbackReason = fallbackReason;
		this.CudaVersion = cudaVersion;
		this.Inputs = OnnxRuntimeSessionAdapter.toMetadata(session.InputMetadata);
		this.Outputs = OnnxRuntimeSessionAdapter.toMetadata(session.OutputMetadata);
	}

	private static IReadOnlyList<OrtTensorMetadata> toMetadata (
		IEnumerable<KeyValuePair<string, NodeMetadata>> metadata
	) => Array.AsReadOnly(metadata.Select(pair => new OrtTensorMetadata(
		pair.Key,
		OnnxRuntimeSessionAdapter.toDataType(pair.Value.ElementType),
		pair.Value.Dimensions)).ToArray());

	private static NamedOnnxValue toNamedValue (OrtTensorValue value) => value.DataType switch {
		OrtTensorDataType.Float32 => NamedOnnxValue.CreateFromTensor(
			value.Name, new DenseTensor<float>((float[])value.Values, value.Dimensions)),
		OrtTensorDataType.Int32 => NamedOnnxValue.CreateFromTensor(
			value.Name, new DenseTensor<int>((int[])value.Values, value.Dimensions)),
		OrtTensorDataType.Int64 => NamedOnnxValue.CreateFromTensor(
			value.Name, new DenseTensor<long>((long[])value.Values, value.Dimensions)),
		_ => throw new NotSupportedException($"ONNX tensor type '{value.DataType}' is not supported by the host contract."),
	};

	private static OrtTensorDataType toDataType (Type elementType) => elementType == typeof(float)
		? OrtTensorDataType.Float32
		: elementType == typeof(int)
			? OrtTensorDataType.Int32
			: elementType == typeof(long)
				? OrtTensorDataType.Int64
				: throw new NotSupportedException($"ONNX tensor type '{elementType.Name}' is not supported by the host contract.");

	public IReadOnlyList<OrtTensorValue> Run (IReadOnlyList<OrtTensorValue> inputs) {
		if (this._disposed)
			throw new ObjectDisposedException(nameof(OnnxRuntimeSessionAdapter));
		if (!this.SupportsConcurrentRuns)
			this._runSemaphore.Wait();
		try {
			var namedInputs = inputs.Select(OnnxRuntimeSessionAdapter.toNamedValue).ToArray();
			using var results = this._session.Run(namedInputs);
			var values = results.Select(this.toTensorValue).ToArray();
			if (this._trackExecutionProviders && !this._profileCaptured)
				this.captureExecutionProviders();
			return values;
		} finally {
			if (!this.SupportsConcurrentRuns)
				this._runSemaphore.Release();
		}
	}

	public void Dispose () {
		if (this._disposed)
			return;
		this._disposed = true;
		this._session.Dispose();
		this._runSemaphore.Dispose();
	}

	private void captureExecutionProviders () {
		this._profileCaptured = true;
		string profilePath = this._session.EndProfiling();
		try {
			using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
			var providers = new SortedSet<string>(StringComparer.Ordinal);
			foreach (JsonElement entry in profile.RootElement.EnumerateArray()) {
				if (!entry.TryGetProperty("args", out JsonElement args)
					|| !args.TryGetProperty("provider", out JsonElement provider)
					|| provider.ValueKind != JsonValueKind.String) {
					continue;
				}
				string? providerName = provider.GetString();
				if (!string.IsNullOrWhiteSpace(providerName))
					providers.Add(providerName);
			}
			this._activeExecutionProviders = Array.AsReadOnly(providers.ToArray());
		} finally {
			File.Delete(profilePath);
		}
	}

	private OrtTensorValue toTensorValue (DisposableNamedOnnxValue output) {
		Type elementType = this._session.OutputMetadata[output.Name].ElementType;
		if (elementType == typeof(float)) {
			var tensor = output.AsTensor<float>();
			return new OrtTensorValue(output.Name, OrtTensorDataType.Float32, tensor.Dimensions.ToArray(), tensor.ToArray());
		}
		if (elementType == typeof(int)) {
			var tensor = output.AsTensor<int>();
			return new OrtTensorValue(output.Name, OrtTensorDataType.Int32, tensor.Dimensions.ToArray(), tensor.ToArray());
		}
		if (elementType == typeof(long)) {
			var tensor = output.AsTensor<long>();
			return new OrtTensorValue(output.Name, OrtTensorDataType.Int64, tensor.Dimensions.ToArray(), tensor.ToArray());
		}
		throw new NotSupportedException($"ONNX tensor type '{elementType.Name}' is not supported by the host contract.");
	}
}