using System.IO;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Creates sessions from the provider pack selected in application settings.</summary>
public class BackendFactory : IService, ISingleton {
	[Inject]
	protected Settings settings { get; set; } = null!;

	[Inject]
	protected PluginLoader pluginLoader { get; set; } = null!;

	[Inject]
	protected PrivacyPolicy privacyPolicy { get; set; } = null!;

	private readonly ProviderCatalog _providerCatalog = new(AppContext.BaseDirectory);
	private readonly object _selectionGate = new();
	private ProviderSelection? _selectedSelection;
	private string? _selectedCudaRuntimePath;

	public IOnnxRuntimeSession CreateSession (string modelPath, int deviceIndex = 0) {
		this.privacyPolicy.DisableTelemetry();
		ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
		string fullModelPath = Path.GetFullPath(modelPath);
		if (!File.Exists(fullModelPath))
			throw new FileNotFoundException("The ONNX model file was not found.", fullModelPath);

		var selection = this.getSelection();
		var pack = this._providerCatalog.GetProviderPack(selection.ProviderId)
			?? throw new InvalidOperationException($"The selected ONNX Runtime provider pack '{selection.ProviderId}' is not installed.");
		this.prepareNativeSearchPath(selection.ProviderId, pack);

		var plugin = this.pluginLoader.Load(selection.ProviderId, pack.PluginAssemblyPath, pack.PluginTypeName);
		return plugin.CreateSession(fullModelPath, new OrtSessionSettings {
			AllowCpuFallback = selection.AllowCpuFallback,
			TrackExecutionProviders = true,
			DeviceIndex = deviceIndex,
		});
	}

	public ActiveBackendInfo? GetVerifiedBackendInfo (IOnnxRuntimeSession session) {
		ArgumentNullException.ThrowIfNull(session);
		return ActiveBackendInfo.FromOnnxRuntime("parakeet", session.ActiveExecutionProviders, session.CudaVersion);
	}

	private ProviderSelection getSelection () {
		var selection = ProviderSelectionPolicy.Select(
			this.settings.Data.OrtBackend,
			this._providerCatalog.GetInstalledProviderIds());
		if (selection is null)
			throw new InvalidOperationException($"No installed ONNX Runtime provider pack matches '{this.settings.Data.OrtBackend}'.");

		string? cudaRuntimePath = selection.ProviderId == "cuda"
			? this.settings.Data.OrtCudaRuntimePath?.Trim()
			: null;
		if (!string.IsNullOrWhiteSpace(cudaRuntimePath))
			cudaRuntimePath = Path.GetFullPath(cudaRuntimePath);

		lock (this._selectionGate) {
			if (this._selectedSelection is not null) {
				bool sameSelection = this._selectedSelection.ProviderId == selection.ProviderId
					&& this._selectedSelection.AllowCpuFallback == selection.AllowCpuFallback
					&& StringComparer.OrdinalIgnoreCase.Equals(this._selectedCudaRuntimePath, cudaRuntimePath);
				if (!sameSelection)
					throw new InvalidOperationException("Changing the ONNX Runtime provider requires an application restart.");
				return this._selectedSelection;
			}

			this._selectedSelection = selection;
			this._selectedCudaRuntimePath = cudaRuntimePath;
			return selection;
		}
	}

	private void prepareNativeSearchPath (string providerId, ProviderPack pack) {
		var directories = new List<string>();
		if (providerId == "cuda") {
			if (!string.IsNullOrWhiteSpace(this._selectedCudaRuntimePath)) {
				if (!Directory.Exists(this._selectedCudaRuntimePath))
					throw new DirectoryNotFoundException("The configured CUDA/cuDNN runtime directory does not exist.");
				directories.Add(this._selectedCudaRuntimePath);
			}

			string bundledCudaDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");
			if (Directory.Exists(bundledCudaDirectory))
				directories.Add(bundledCudaDirectory);
		}

		directories.AddRange(pack.NativeSearchDirectories);
		string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		string updatedPath = NativeSearchPath.PrependDirectories(currentPath, directories);
		Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
	}
}