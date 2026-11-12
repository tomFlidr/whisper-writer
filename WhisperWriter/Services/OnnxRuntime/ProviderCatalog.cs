using System.IO;
using System.Linq;
using WhisperWriter.Services.OnnxRuntime.ProviderCatalogs;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Finds provider packs without claiming that their execution providers are active.</summary>
public class ProviderCatalog {
	private static readonly ProviderDefinition[] _providerDefinitions = [
		new("cpu", "WhisperWriter.OrtRuntime.Cpu.dll", "WhisperWriter.OrtRuntime.Cpu.ProviderPlugin"),
		new("directml", "WhisperWriter.OrtRuntime.DirectML.dll", "WhisperWriter.OrtRuntime.DirectML.ProviderPlugin"),
		new("cuda", "WhisperWriter.OrtRuntime.Cuda.dll", "WhisperWriter.OrtRuntime.Cuda.ProviderPlugin"),
	];

	private readonly string _applicationBaseDirectory;

	public ProviderCatalog (string applicationBaseDirectory) {
		ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
		this._applicationBaseDirectory = Path.GetFullPath(applicationBaseDirectory);
	}

	public IReadOnlyList<string> GetInstalledProviderIds () => ProviderCatalog._providerDefinitions
		.Where(definition => this.isPackInstalled(definition))
		.Select(definition => definition.ProviderId)
		.ToArray();

	public ProviderPack? GetProviderPack (string providerId) {
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		var definition = ProviderCatalog._providerDefinitions.FirstOrDefault(candidate =>
			StringComparer.OrdinalIgnoreCase.Equals(candidate.ProviderId, providerId.Trim()));
		if (definition is null || !this.isPackInstalled(definition))
			return null;

		string packDirectory = this.getPackDirectory(definition);
		string assemblyPath = Path.Combine(packDirectory, definition.AssemblyName);
		var nativeDirectories = Directory.EnumerateFiles(packDirectory, "*.dll", SearchOption.AllDirectories)
			.Select(Path.GetDirectoryName)
			.Where(directory => !string.IsNullOrWhiteSpace(directory))
			.Cast<string>()
			.Append(packDirectory);

		return new ProviderPack(
			definition.ProviderId,
			assemblyPath,
			definition.PluginTypeName,
			nativeDirectories);
	}

	private bool isPackInstalled (ProviderDefinition definition) {
		string packDirectory = this.getPackDirectory(definition);
		string assemblyPath = Path.Combine(packDirectory, definition.AssemblyName);
		string depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
		return File.Exists(assemblyPath) && File.Exists(depsPath);
	}

	private string getPackDirectory (ProviderDefinition definition) => Path.Combine(
		this._applicationBaseDirectory,
		"runtimes",
		"onnx",
		definition.ProviderId);

}