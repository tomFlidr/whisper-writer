namespace WhisperWriter.Services.OnnxRuntime;

/// <summary>Provider pack selected at process startup from the saved preference.</summary>
public class ProviderSelection {
	public string ProviderId { get; }
	public bool AllowCpuFallback { get; }

	public ProviderSelection (string providerId, bool allowCpuFallback) {
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		this.ProviderId = providerId.Trim().ToLowerInvariant();
		this.AllowCpuFallback = allowCpuFallback;
	}
}