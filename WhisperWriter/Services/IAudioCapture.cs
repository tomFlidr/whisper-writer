namespace WhisperWriter.Services;

/// <summary>
/// Platform boundary for receiving microphone PCM data.
/// </summary>
public interface IAudioCapture : IDisposable {
	event Action<byte[], int>? DataAvailable;
	void StartRecording();
	void StopRecording();
}
