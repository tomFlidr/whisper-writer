using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using WhisperWriter.OrtRuntime.Contracts;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Resolves a provider plugin's managed and native dependencies from its private pack.</summary>
internal class PluginLoadContext : AssemblyLoadContext {
	private readonly AssemblyDependencyResolver _resolver;
	private readonly Assembly _contractsAssembly;

	public PluginLoadContext (string pluginAssemblyPath) : base(isCollectible: false) {
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
		this._resolver = new AssemblyDependencyResolver(Path.GetFullPath(pluginAssemblyPath));
		this._contractsAssembly = typeof(IOnnxRuntimeProviderPlugin).Assembly;
	}

	protected override Assembly? Load (AssemblyName assemblyName) {
		if (assemblyName.Name == this._contractsAssembly.GetName().Name)
			return this._contractsAssembly;

		var assemblyPath = this._resolver.ResolveAssemblyToPath(assemblyName);
		return assemblyPath is null ? null : this.LoadFromAssemblyPath(assemblyPath);
	}

	protected override IntPtr LoadUnmanagedDll (string unmanagedDllName) {
		var libraryPath = this._resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return libraryPath is null ? IntPtr.Zero : this.LoadUnmanagedDllFromPath(libraryPath);
	}
}