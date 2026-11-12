namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Resolved paths and entry point for one optional ONNX Runtime provider pack.</summary>
public class ProviderPack {
	public string ProviderId { get; }
	public string PluginAssemblyPath { get; }
	public string PluginTypeName { get; }
	public IReadOnlyList<string> NativeSearchDirectories { get; }

	public ProviderPack (
		string providerId,
		string pluginAssemblyPath,
		string pluginTypeName,
		IEnumerable<string> nativeSearchDirectories
	) {
		this.ProviderId = providerId;
		this.PluginAssemblyPath = pluginAssemblyPath;
		this.PluginTypeName = pluginTypeName;
		this.NativeSearchDirectories = Array.AsReadOnly(nativeSearchDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
	}
}