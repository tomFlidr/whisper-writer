using NAudio.Wave;
using System.IO;

namespace WhisperWriter.Services;

/// <summary>
/// Records from the default microphone into an in-memory WAV buffer.
/// Call StartRecording() / StopRecording() around the push-to-talk period.
/// GetWavBytes() returns the complete 16 kHz mono WAV suitable for Whisper.
/// </summary>
public sealed class AudioRecorder: IDisposable {
	private const int SampleRate = 16000;
	private const int Channels = 1;
	private const int BitsPerSample = 16;

	private WaveInEvent? _waveIn;
	private MemoryStream? _buffer;
	private WaveFileWriter? _writer;
	private bool _recording;
	private readonly object _lock = new();

	// Fires periodically with the current amplitude (0–1) for the VU meter
	public event Action<float>? AmplitudeChanged;

	public void StartRecording () {
		lock (_lock) {
			if (_recording) return;

			_buffer = new MemoryStream();
			var format = new WaveFormat(SampleRate, BitsPerSample, Channels);
			_writer = new WaveFileWriter(_buffer, format);

			_waveIn = new WaveInEvent {
				WaveFormat = format,
				BufferMilliseconds = 50,
			};
			_waveIn.DataAvailable += OnDataAvailable;
			_waveIn.StartRecording();
			_recording = true;
		}
	}

	public byte[]? StopRecording () {
		lock (_lock) {
			if (!_recording) return null;
			_recording = false;

			_waveIn!.StopRecording();
			_waveIn.DataAvailable -= OnDataAvailable;
			_waveIn.Dispose();
			_waveIn = null;

			_writer!.Flush();
			_writer.Dispose();
			_writer = null;

			var bytes = _buffer!.ToArray();
			_buffer.Dispose();
			_buffer = null;
			return bytes;
		}
	}

	private void OnDataAvailable (object? sender, WaveInEventArgs e) {
		lock (_lock) {
			if (!_recording || _writer == null) return;
			_writer.Write(e.Buffer, 0, e.BytesRecorded);

			// Calculate RMS amplitude for VU meter
			float rms = CalculateRms(e.Buffer, e.BytesRecorded);
			AmplitudeChanged?.Invoke(rms);
		}
	}

	private static float CalculateRms (byte[] buffer, int length) {
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

	public void Dispose () {
		StopRecording();
	}
}
