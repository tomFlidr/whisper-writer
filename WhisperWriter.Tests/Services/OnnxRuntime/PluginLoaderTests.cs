using WhisperWriter.OrtRuntime.Contracts;
using WhisperWriter.Services;
using WhisperWriter.Services.OnnxRuntime;
using Xunit;

namespace WhisperWriter.Tests.Services.OnnxRuntime;

public class PluginLoaderTests {
	[Fact]
	public void LoadUsesTheHostContractAssemblyForTheIsolatedPlugin () {
		var loader = new PluginLoader();
		var plugin = loader.Load(
			"cpu", typeof(PluginLoaderTests).Assembly.Location, typeof(FakeCpuProviderPlugin).FullName!);

		Assert.Equal("cpu", plugin.ProviderId);
	}

	[Fact]
	public void LoadCannotSwitchProviderCoreAfterTheFirstPluginIsDisposed () {
		var loader = new PluginLoader();
		string pluginAssemblyPath = typeof(PluginLoaderTests).Assembly.Location;
		loader.Load("cpu", pluginAssemblyPath, typeof(FakeCpuProviderPlugin).FullName!);
		loader.Dispose();

		Assert.Throws<InvalidOperationException>(() => loader.Load(
			"directml", pluginAssemblyPath, typeof(FakeDirectMlProviderPlugin).FullName!));
	}

	public class FakeCpuProviderPlugin : IOnnxRuntimeProviderPlugin {
		public string ProviderId => "cpu";

		public IOnnxRuntimeSession CreateSession (string modelPath, OrtSessionSettings settings) =>
			throw new NotSupportedException();

		public void Dispose () { }
	}

	public class FakeDirectMlProviderPlugin : IOnnxRuntimeProviderPlugin {
		public string ProviderId => "directml";

		public IOnnxRuntimeSession CreateSession (string modelPath, OrtSessionSettings settings) =>
			throw new NotSupportedException();

		public void Dispose () { }
	}
}