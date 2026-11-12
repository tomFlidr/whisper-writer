using System.IO;
using System.Reflection;
using WhisperWriter.DI;
using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Loads one provider-specific ORT plugin for the process lifetime.</summary>
public class PluginLoader : IService, ISingleton, IDisposable {
	private IOnnxRuntimeProviderPlugin? _plugin;
	private PluginLoadContext? _loadContext;
	private string? _selectedProviderId;
	private bool _disposed;
	private readonly object _gate = new();

	public IOnnxRuntimeProviderPlugin Load (string providerId, string pluginAssemblyPath, string pluginTypeName) {
		lock (this._gate)
			return this.load(providerId, pluginAssemblyPath, pluginTypeName);
	}

	public void Dispose () {
		lock (this._gate) {
			if (this._disposed)
				return;
			this._disposed = true;
			this._plugin?.Dispose();
			this._plugin = null;
		}
	}

	private IOnnxRuntimeProviderPlugin load (string providerId, string pluginAssemblyPath, string pluginTypeName) {
		if (this._disposed)
			throw new InvalidOperationException("The ONNX Runtime plugin loader is disposed; restart the application to change provider.");
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginTypeName);

		string normalizedProviderId = providerId.Trim().ToLowerInvariant();
		if (this._selectedProviderId is not null) {
			if (this._selectedProviderId != normalizedProviderId)
				throw new InvalidOperationException("Changing the ONNX Runtime provider requires an application restart.");
			return this._plugin
				?? throw new InvalidOperationException("The selected ONNX Runtime provider failed to load; restart the application before retrying.");
		}

		string fullPluginPath = Path.GetFullPath(pluginAssemblyPath);
		if (!File.Exists(fullPluginPath))
			throw new FileNotFoundException("The selected ONNX Runtime provider plugin is not installed.", fullPluginPath);

		this._selectedProviderId = normalizedProviderId;
		var loadContext = new PluginLoadContext(fullPluginPath);
		var pluginAssembly = loadContext.LoadFromAssemblyPath(fullPluginPath);
		var pluginType = pluginAssembly.GetType(pluginTypeName, throwOnError: true)!;
		if (!typeof(IOnnxRuntimeProviderPlugin).IsAssignableFrom(pluginType))
			throw new InvalidCastException($"Plugin type '{pluginTypeName}' does not implement {nameof(IOnnxRuntimeProviderPlugin)}.");

		var plugin = (IOnnxRuntimeProviderPlugin?)Activator.CreateInstance(pluginType)
			?? throw new InvalidOperationException($"Could not create provider plugin '{pluginTypeName}'.");
		if (!StringComparer.Ordinal.Equals(plugin.ProviderId, normalizedProviderId)) {
			plugin.Dispose();
			throw new InvalidOperationException($"Provider pack '{plugin.ProviderId}' does not match the requested '{normalizedProviderId}'.");
		}

		this._plugin = plugin;
		this._loadContext = loadContext;
		return plugin;
	}
}