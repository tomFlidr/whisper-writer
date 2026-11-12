namespace WhisperWriter.OrtRuntime.Contracts;

/// <summary>Session creation policy passed from the WPF host to an ORT provider plugin.</summary>
public class OrtSessionSettings {
	public bool AllowCpuFallback { get; set; }
	public bool TrackExecutionProviders { get; set; } = true;
	public int DeviceIndex { get; set; }
}