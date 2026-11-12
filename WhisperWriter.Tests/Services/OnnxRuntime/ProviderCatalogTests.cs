using System.IO;
using WhisperWriter.Services;
using WhisperWriter.Services.OnnxRuntime;
using Xunit;

namespace WhisperWriter.Tests.Services.OnnxRuntime;

public class ProviderCatalogTests {
	[Fact]
	public void CatalogListsOnlyPacksWithBothPluginAssemblyAndDependencyManifest () {
		string root = Path.Combine(Path.GetTempPath(), "ort-catalog-" + Guid.NewGuid().ToString("N"));
		string cpuDirectory = Path.Combine(root, "runtimes", "onnx", "cpu");
		Directory.CreateDirectory(cpuDirectory);
		string assemblyPath = Path.Combine(cpuDirectory, "WhisperWriter.OrtRuntime.Cpu.dll");
		string depsPath = Path.Combine(cpuDirectory, "WhisperWriter.OrtRuntime.Cpu.deps.json");
		File.WriteAllBytes(assemblyPath, Array.Empty<byte>());

		try {
			var catalog = new ProviderCatalog(root);
			Assert.Empty(catalog.GetInstalledProviderIds());

			File.WriteAllText(depsPath, "{}");
			Assert.Equal(new[] { "cpu" }, catalog.GetInstalledProviderIds());
		} finally {
			Directory.Delete(root, recursive: true);
		}
	}
}