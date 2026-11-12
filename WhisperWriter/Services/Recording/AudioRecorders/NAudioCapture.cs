using NAudio.Wave;

namespace WhisperWriter.Services.Recording.AudioRecorders;

/// <summary>
/// NAudio implementation of the microphone capture boundary.
/// </summary>
internal class NAudioCapture : IAudioCapture {
	public event Action<byte[], int>? DataAvailable;

	private readonly WaveInEvent _waveIn;

	public NAudioCapture (WaveFormat format, int bufferMilliseconds) {
		this._waveIn = new WaveInEvent {
			WaveFormat = format,
			BufferMilliseconds = bufferMilliseconds,
		};
		this._waveIn.DataAvailable += this._handleDataAvailable;
	}

	public void StartRecording () {
		this._waveIn.StartRecording();
	}

	public void StopRecording () {
		this._waveIn.StopRecording();
	}

	public void Dispose () {
		this._waveIn.DataAvailable -= this._handleDataAvailable;
		this._waveIn.Dispose();
	}

	private void _handleDataAvailable (object? sender, WaveInEventArgs e) {
		this.DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
	}
}