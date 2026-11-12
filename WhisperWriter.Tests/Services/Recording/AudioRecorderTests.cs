using System.IO;
using NAudio.Wave;
using WhisperWriter.Services.Recording;
using WhisperWriter.Services.Recording.AudioRecorders;
using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services.Recording;

public class AudioRecorderTests {
	[Fact]
	public void StopRecording_ReturnsNullWhenNoRecordingIsActive () {
		using var recorder = new AudioRecorder((_, _) => new FakeAudioCapture());

		Assert.Null(recorder.StopRecording());
	}

	[Fact]
	public void StartRecording_CapturesWaveSamplesAndReportsRmsAmplitude () {
		var capture = new FakeAudioCapture();
		WaveFormat? captureFormat = null;
		int bufferMilliseconds = 0;
		using var recorder = new AudioRecorder((format, buffer) => {
			captureFormat = format;
			bufferMilliseconds = buffer;
			return capture;
		});
		float amplitude = -1;
		recorder.AmplitudeChanged += value => amplitude = value;
		recorder.StartRecording();
		recorder.StartRecording();

		byte[] samples = [0x00, 0x40, 0x00, 0xC0];
		capture.RaiseDataAvailable(samples);
		byte[] wav = recorder.StopRecording()!;

		Assert.Equal(1, capture.StartCount);
		Assert.Equal(1, capture.StopCount);
		Assert.True(capture.IsDisposed);
		Assert.Equal(0.5f, amplitude);
		var actualFormat = Assert.IsType<WaveFormat>(captureFormat);
		Assert.Equal(16000, actualFormat.SampleRate);
		Assert.Equal(1, actualFormat.Channels);
		Assert.Equal(16, actualFormat.BitsPerSample);
		Assert.Equal(50, bufferMilliseconds);

		using var reader = new WaveFileReader(new MemoryStream(wav));
		Assert.Equal(16000, reader.WaveFormat.SampleRate);
		Assert.Equal(1, reader.WaveFormat.Channels);
		Assert.Equal(16, reader.WaveFormat.BitsPerSample);

		byte[] recordedSamples = new byte[samples.Length];
		Assert.Equal(samples.Length, reader.Read(recordedSamples, 0, recordedSamples.Length));
		Assert.Equal(samples, recordedSamples);
	}

	[Fact]
	public void Dispose_StopsAndReleasesAnActiveCapture () {
		var capture = new FakeAudioCapture();
		var recorder = new AudioRecorder((_, _) => capture);
		recorder.StartRecording();

		recorder.Dispose();
		recorder.Dispose();

		Assert.Equal(1, capture.StopCount);
		Assert.True(capture.IsDisposed);
		Assert.Null(recorder.StopRecording());
	}

	private class FakeAudioCapture : IAudioCapture {
		public event Action<byte[], int>? DataAvailable;
		public int StartCount { get; private set; }
		public int StopCount { get; private set; }
		public bool IsDisposed { get; private set; }

		public void StartRecording () {
			this.StartCount++;
		}

		public void StopRecording () {
			this.StopCount++;
		}

		public void Dispose () {
			this.IsDisposed = true;
		}

		public void RaiseDataAvailable (byte[] data) {
			this.DataAvailable?.Invoke(data, data.Length);
		}
	}
}