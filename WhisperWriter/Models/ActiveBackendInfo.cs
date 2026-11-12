using System.Collections.ObjectModel;

namespace WhisperWriter.Models;

/// <summary>Immutable identity of the runtime and execution providers confirmed to perform inference work.</summary>
public class ActiveBackendInfo {
	public string Device { get; }
	public string Engine { get; }
	public string Runtime { get; }
	public IReadOnlyList<string> Providers => this._providers;
	public int? CudaVersion { get; }
	public string? PrimaryProvider => this.Device == "gpu"
		? this._providers.FirstOrDefault(provider => !ActiveBackendInfo.isCpuProvider(provider))
			?? this._providers.FirstOrDefault()
		: this._providers.FirstOrDefault(ActiveBackendInfo.isCpuProvider)
			?? this._providers.FirstOrDefault();
	public string DisplayName => string.Join(" + ", this._providers.Select(ActiveBackendInfo.toDisplayName));

	private readonly ReadOnlyCollection<string> _providers;

	public ActiveBackendInfo (
		string device,
		string engine,
		string runtime,
		IEnumerable<string> providers,
		int? cudaVersion = null
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(device);
		ArgumentException.ThrowIfNullOrWhiteSpace(engine);
		ArgumentException.ThrowIfNullOrWhiteSpace(runtime);
		ArgumentNullException.ThrowIfNull(providers);

		string normalizedDevice = device.Trim().ToLowerInvariant();
		if (normalizedDevice is not ("cpu" or "gpu"))
			throw new ArgumentOutOfRangeException(nameof(device), "Device must be cpu or gpu.");

		string[] normalizedProviders = providers
			.Select(provider => provider?.Trim().ToLowerInvariant())
			.Where(provider => !string.IsNullOrWhiteSpace(provider))
			.Select(provider => provider!)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(provider => provider, StringComparer.Ordinal)
			.ToArray();
		if (normalizedProviders.Length == 0)
			throw new ArgumentException("At least one active provider is required.", nameof(providers));
		if (cudaVersion is < 1)
			throw new ArgumentOutOfRangeException(nameof(cudaVersion));
		if (cudaVersion.HasValue && !normalizedProviders.Contains("cuda", StringComparer.Ordinal))
			throw new ArgumentException("CUDA version requires an active CUDA provider.", nameof(cudaVersion));

		this.Device = normalizedDevice;
		this.Engine = engine.Trim().ToLowerInvariant();
		this.Runtime = runtime.Trim().ToLowerInvariant();
		this._providers = Array.AsReadOnly(normalizedProviders);
		this.CudaVersion = cudaVersion;
	}

	public static ActiveBackendInfo? FromWhisperNet (string? loadedLibrary) {
		return loadedLibrary switch {
			"Cuda" => new ActiveBackendInfo("gpu", "whisper", "whisper.net", ["cuda"], 13),
			"Cuda12" => new ActiveBackendInfo("gpu", "whisper", "whisper.net", ["cuda"], 12),
			"Vulkan" => new ActiveBackendInfo("gpu", "whisper", "whisper.net", ["vulkan"]),
			"Cpu" => new ActiveBackendInfo("cpu", "whisper", "whisper.net", ["cpu"]),
			"CpuNoAvx" => new ActiveBackendInfo("cpu", "whisper", "whisper.net", ["cpu-no-avx"]),
			_ => null,
		};
	}

	public static ActiveBackendInfo? FromOnnxRuntime (
		string engine,
		IEnumerable<string>? executionProviders,
		int? cudaVersion = null
	) {
		if (string.IsNullOrWhiteSpace(engine) || executionProviders is null)
			return null;

		var providers = new List<string>();
		foreach (string executionProvider in executionProviders) {
			var provider = ActiveBackendInfo.mapOnnxProvider(executionProvider);
			if (provider is null)
				return null;
			providers.Add(provider);
		}

		if (providers.Count == 0 || (cudaVersion.HasValue && !providers.Contains("cuda", StringComparer.Ordinal)))
			return null;

		string device = providers.Any(provider => !ActiveBackendInfo.isCpuProvider(provider)) ? "gpu" : "cpu";
		return new ActiveBackendInfo(device, engine, "onnxruntime", providers, cudaVersion);
	}

	private static bool isCpuProvider (string provider) => provider is "cpu" or "cpu-no-avx";

	private static string? mapOnnxProvider (string provider) => provider.Trim().ToLowerInvariant() switch {
		"cpu" or "cpuexecutionprovider" => "cpu",
		"cuda" or "cudaexecutionprovider" => "cuda",
		"directml" or "dmlexecutionprovider" or "directmlexecutionprovider" => "directml",
		_ => null,
	};

	private static string toDisplayName (string provider) => provider switch {
		"cuda" => "Cuda",
		"vulkan" => "Vulkan",
		"cpu" => "Cpu",
		"cpu-no-avx" => "CpuNoAvx",
		"directml" => "DirectML",
		_ => provider,
	};
}