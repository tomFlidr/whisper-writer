using System.IO;
using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.Ggml;

namespace WhisperWriter.Services;

public enum TranscriptionState {
	Idle,
	Loading,
	Transcribing,
	Done,
	Error,
}

public sealed class WhisperService: IAsyncDisposable {
	private WhisperFactory? _factory;
	private bool _initialized;

	public event Action<TranscriptionState, string>? StateChanged;

	/// <summary>
	/// Probes whether CUDA runtime DLLs required by ggml-cuda-whisper.dll are loadable.
	/// Tries CUDA major versions 13, 12, 11 in order.
	/// Returns the detected major version number, or null if no CUDA runtime was found.
	/// </summary>
	public static int? DetectCudaVersion () {
		foreach (var version in new[] { 13, 12, 11 }) {
			var cudart = $"cudart64_{version}.dll";
			var cublas = $"cublas64_{version}.dll";
			var hCudart = NativeMethods.LoadLibraryEx(cudart, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
			if (hCudart == IntPtr.Zero)
				continue;
			NativeMethods.FreeLibrary(hCudart);
			var hCublas = NativeMethods.LoadLibraryEx(cublas, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
			if (hCublas == IntPtr.Zero)
				continue;
			NativeMethods.FreeLibrary(hCublas);
			return version;
		}
		return null;
	}

	private static class NativeMethods {
		internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		internal static extern IntPtr LoadLibraryEx (string lpFileName, IntPtr hFile, uint dwFlags);
		[DllImport("kernel32.dll", SetLastError = true)]
		internal static extern bool FreeLibrary (IntPtr hModule);
	}

	/// <summary>
	/// Initializes the Whisper model from disk. Call once at startup.
	/// Prefers CUDA; falls back to CPU automatically via Whisper.net.Runtime.Cuda.
	/// </summary>
	public async Task InitializeAsync (string modelPath) {
		StateChanged?.Invoke(TranscriptionState.Loading, "Loading model…");

		var cudaVersion = DetectCudaVersion();
		LogService.Info(cudaVersion.HasValue
			? $"CUDA probe: cudart64_{cudaVersion}.dll + cublas64_{cudaVersion}.dll found — GPU will be used (CUDA {cudaVersion})"
			: "CUDA probe: no CUDA runtime found (tried versions 13, 12, 11) — falling back to CPU");

		try {
			// Download model if missing
			if (!File.Exists(modelPath)) {
				var dir = Path.GetDirectoryName(modelPath)!;
				Directory.CreateDirectory(dir);
				StateChanged?.Invoke(TranscriptionState.Loading, "Downloading model…");
				using var httpClient = new System.Net.Http.HttpClient();
				var downloader = new WhisperGgmlDownloader(httpClient);
				await downloader.GetGgmlModelAsync(GgmlType.Medium, QuantizationType.NoQuantization)
					.ContinueWith(async t => {
						await using var src = await t;
						await using var dst = File.Create(modelPath);
						await src.CopyToAsync(dst);
					}).Unwrap();
			}

			_factory = WhisperFactory.FromPath(modelPath);
			_initialized = true;
			StateChanged?.Invoke(TranscriptionState.Idle, "Ready");
		} catch (Exception ex) {
			_initialized = false;
			_factory = null;
			LogService.Error("Failed to load Whisper model", ex);
			StateChanged?.Invoke(TranscriptionState.Error, $"Model load failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Transcribes the given WAV bytes (16 kHz mono PCM).
	/// Returns the transcribed text, or throws on error.
	/// </summary>
	public async Task<string> TranscribeAsync (byte[] wavBytes, string language, string prompt) {
		if (!_initialized || _factory == null)
			throw new InvalidOperationException("Model is not loaded. Check the model path in Settings.");

		StateChanged?.Invoke(TranscriptionState.Transcribing, "Transcribing…");

		// Build processor with current settings.
		// IMPORTANT: never call WithTranslate() – we want transcription only.
		// Always set an explicit language; fall back to "cs" when "auto" is selected
		// to prevent whisper.cpp from silently translating non-English speech.
		var effectiveLanguage = (!string.IsNullOrWhiteSpace(language) && language != "auto")
			? language
			: "cs";

		var builder = _factory.CreateBuilder()
			.WithLanguage(effectiveLanguage)
			.WithThreads(Math.Max(1, Environment.ProcessorCount - 2));

		if (!string.IsNullOrEmpty(prompt))
			builder = builder.WithPrompt(prompt);

		await using var processor = builder.Build();

		using var ms = new MemoryStream(wavBytes);
		var segments = new System.Text.StringBuilder();
		await foreach (var seg in processor.ProcessAsync(ms))
			segments.Append(seg.Text);

		var result = segments.ToString().Trim();
		StateChanged?.Invoke(TranscriptionState.Done, result);
		return result;
	}

	public async ValueTask DisposeAsync () {
		_factory?.Dispose();
		await ValueTask.CompletedTask;
	}
}