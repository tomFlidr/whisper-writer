using System.IO;

namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Builds a process-local PATH value for loading provider-native dependencies.</summary>
public static class NativeSearchPath {
	public static string PrependDirectories (string? currentPath, IEnumerable<string>? directories) {
		var result = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (directories is not null) {
			foreach (string directory in directories) {
				if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
					continue;
				string fullPath = Path.GetFullPath(directory.Trim());
				if (seen.Add(fullPath))
					result.Add(fullPath);
			}
		}

		foreach (string existingDirectory in (currentPath ?? string.Empty).Split(
			Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (seen.Add(existingDirectory))
				result.Add(existingDirectory);
		}

		return string.Join(Path.PathSeparator, result);
	}
}