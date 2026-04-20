using NAudio.Wave;
using System.IO;

namespace WhisperWriter.Services;

/// <summary>
/// Records from the default microphone into an in-memory WAV buffer.
/// Call StartRecording() / StopRecording() around the push-to-talk period.
/// GetWavBytes() returns the complete 16 kHz mono WAV suitable for Whisper.
/// </summary>
public class AudioRecorder : IDisposable {
	private static readonly int _sampleRate = 16000;
	private static readonly int _channels = 1;
	private static readonly int _bitsPerSample = 16;

	// Fires periodically with the current amplitude (0–1) for the VU meter
	public event Action<float>? AmplitudeChanged;

	private WaveInEvent? _waveIn;
	private MemoryStream? _buffer;
	private WaveFileWriter? _writer;
	private bool _recording;
	private readonly object _lock = new();

	public void StartRecording () {
		lock (this._lock) {
			if (this._recording) return;

			this._buffer = new MemoryStream();
			var format = new WaveFormat(
				AudioRecorder._sampleRate, 
				AudioRecorder._bitsPerSample, 
				AudioRecorder._channels
			);
			this._writer = new WaveFileWriter(this._buffer, format);

			this._waveIn = new WaveInEvent {
				WaveFormat = format,
				BufferMilliseconds = 50,
			};
			this._waveIn.DataAvailable += this._handleDataAvailable;
			this._waveIn.StartRecording();
			this._recording = true;
		}
	}

	public byte[]? StopRecording () {
		lock (this._lock) {
			if (!this._recording) return null;
			this._recording = false;

			this._waveIn!.StopRecording();
			this._waveIn.DataAvailable -= this._handleDataAvailable;
			this._waveIn.Dispose();
			this._waveIn = null;

			this._writer!.Flush();
			this._writer.Dispose();
			this._writer = null;

			var bytes = this._buffer!.ToArray();
			this._buffer.Dispose();
			this._buffer = null;
			return bytes;
		}
	}

	public void Dispose () {
		this.StopRecording();
	}

	private void _handleDataAvailable (object? sender, WaveInEventArgs e) {
		lock (this._lock) {
			if (!this._recording || this._writer == null) return;
			this._writer.Write(e.Buffer, 0, e.BytesRecorded);

			// Calculate RMS amplitude for VU meter
			float rms = AudioRecorder._calculateRms(e.Buffer, e.BytesRecorded);
			this.AmplitudeChanged?.Invoke(rms);
		}
	}

	private static float _calculateRms (byte[] buffer, int length) {
		if (length < 2) return 0f;
		double sum = 0;
		int samples = length / 2;
		for (int i = 0; i < length - 1; i += 2) {
			short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
			double norm = sample / 32768.0;
			sum += norm * norm;
		}
		return (float)Math.Sqrt(sum / samples);
	}
}
