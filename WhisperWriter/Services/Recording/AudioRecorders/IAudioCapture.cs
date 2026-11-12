namespace WhisperWriter.Services.Recording.AudioRecorders;

/// <summary>
/// Platform boundary for receiving microphone PCM data.
/// </summary>
public interface IAudioCapture : IDisposable {
	event Action<byte[], int>? DataAvailable;
	void StartRecording ();
	void StopRecording ();
}