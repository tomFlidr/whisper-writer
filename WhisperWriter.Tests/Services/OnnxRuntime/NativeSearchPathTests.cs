using System.IO;
using WhisperWriter.Services;
using WhisperWriter.Services.OnnxRuntime;
using Xunit;

namespace WhisperWriter.Tests.Services.OnnxRuntime;

public class NativeSearchPathTests {
	[Fact]
	public void PrependDirectoriesPrioritizesNewFoldersAndDeduplicatesExistingEntries () {
		string root = Path.Combine(Path.GetTempPath(), "ort-path-" + Guid.NewGuid().ToString("N"));
		string providerDirectory = Path.Combine(root, "provider");
		string runtimeDirectory = Path.Combine(root, "runtime");
		string existingDirectory = Path.Combine(root, "existing");
		Directory.CreateDirectory(providerDirectory);
		Directory.CreateDirectory(runtimeDirectory);

		try {
			string currentPath = string.Join(Path.PathSeparator, new[] { providerDirectory, existingDirectory });
			string result = NativeSearchPath.PrependDirectories(
				currentPath,
				new[] { providerDirectory, runtimeDirectory, providerDirectory, Path.Combine(root, "missing") });

			Assert.Equal(new[] {
				Path.GetFullPath(providerDirectory),
				Path.GetFullPath(runtimeDirectory),
				existingDirectory,
			}, result.Split(Path.PathSeparator));
		} finally {
			Directory.Delete(root, recursive: true);
		}
	}
}