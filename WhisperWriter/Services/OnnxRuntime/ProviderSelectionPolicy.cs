namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Selects one ONNX Runtime plugin pack without treating it as an initialized provider.</summary>
public static class ProviderSelectionPolicy {
	public static ProviderSelection? Select (string? preference, IEnumerable<string>? installedProviderIds) {
		if (string.IsNullOrWhiteSpace(preference) || installedProviderIds is null)
			return null;

		var installed = installedProviderIds
			.Select(providerId => providerId?.Trim().ToLowerInvariant())
			.Where(providerId => providerId is "cpu" or "directml" or "cuda")
			.Select(providerId => providerId!)
			.ToHashSet(StringComparer.Ordinal);

		return preference.Trim().ToLowerInvariant() switch {
			"auto" when installed.Contains("directml") => new ProviderSelection("directml", allowCpuFallback: true),
			"auto" when installed.Contains("cpu") => new ProviderSelection("cpu", allowCpuFallback: false),
			"cpu" when installed.Contains("cpu") => new ProviderSelection("cpu", allowCpuFallback: false),
			"directml" when installed.Contains("directml") => new ProviderSelection("directml", allowCpuFallback: false),
			"cuda" when installed.Contains("cuda") => new ProviderSelection("cuda", allowCpuFallback: false),
			_ => null,
		};
	}
}