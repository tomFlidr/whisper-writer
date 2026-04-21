using System.IO;
using System.Runtime.InteropServices;
using Whisper.net;
using Whisper.net.Ggml;
using WhisperWriter.DI;
using WhisperWriter.Services.WhisperServices;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Whisper.net-backed transcription engine.
/// Implements ITranscriptionService; prefers CUDA, falls back to CPU automatically.
/// </summary>
public class WhisperService : ITranscriptionService, IService, ISingleton {
	// Minimum acceptable file size for any GGML model (tiny.en ≈ 78 MB).
	// A file smaller than this is certainly truncated or corrupted.
	public const long MinModelFileSizeBytes = 70 * 1024 * 1024; // 70 MB

	public event Action<TranscriptionState, string>? StateChanged;

	[Inject]
	protected SettingsService settingsService { get; set; } = null!;
	[Inject]
	protected LogService logService { get; set; } = null!;

	protected WhisperFactory? factory;
	protected bool initialized;

	/// <summary>
	/// Probes whether CUDA runtime DLLs required by ggml-cuda-whisper.dll are loadable.
	/// Tries CUDA major versions 13, 12, 11 in order.
	/// Returns the detected major version number, or null if no CUDA runtime was found.
	/// </summary>
	public static int? DetectCudaVersion() {
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

	/// <summary>Returns the number of CPU threads used for Whisper inference.</summary>
	public static int GetInferenceThreadCount() => Math.Max(1, Environment.ProcessorCount - 2);

	/// <summary>
	/// Initialises the Whisper model from disk. Call once at startup.
	/// Prefers CUDA; falls back to CPU automatically via Whisper.net.Runtime.Cuda.
	/// </summary>
	public async Task InitializeAsync(string modelPath) {
		this.StateChanged?.Invoke(TranscriptionState.Loading, "Loading model…");

		var cudaVersion = WhisperService.DetectCudaVersion();
		this.logService.Info(cudaVersion.HasValue
			? $"CUDA probe: cudart64_{cudaVersion}.dll + cublas64_{cudaVersion}.dll found — GPU will be used (CUDA {cudaVersion})"
			: "CUDA probe: no CUDA runtime found (tried versions 13, 12, 11) — falling back to CPU");

		try {
			// Delete and re-download if the file exists but is clearly truncated.
			if (File.Exists(modelPath) && new FileInfo(modelPath).Length < WhisperService.MinModelFileSizeBytes) {
				this.logService.Warning($"Model file '{modelPath}' is too small ({new FileInfo(modelPath).Length / 1024 / 1024} MB) — deleting and re-downloading.");
				File.Delete(modelPath);
			}

			// Download model if missing.
			if (!File.Exists(modelPath)) {
				var dir = Path.GetDirectoryName(modelPath)!;
				Directory.CreateDirectory(dir);
				this.StateChanged?.Invoke(TranscriptionState.Loading, "Downloading model…");
				using var httpClient = new System.Net.Http.HttpClient();
				var downloader = new WhisperGgmlDownloader(httpClient);
				await using var src = await downloader.GetGgmlModelAsync(GgmlType.Medium, QuantizationType.NoQuantization);
				await using var dst = File.Create(modelPath);
				await src.CopyToAsync(dst);
			}

			this.factory = WhisperFactory.FromPath(modelPath);
			this.initialized = true;
			this.StateChanged?.Invoke(TranscriptionState.Idle, "Ready");
		} catch (Exception ex) {
			this.initialized = false;
			this.factory = null;
			this.logService.Error("Failed to load Whisper model", ex);
			this.StateChanged?.Invoke(TranscriptionState.Error, $"Model load failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Transcribes the given WAV bytes (16 kHz mono PCM).
	/// Returns the transcribed text, or throws on error.
	/// </summary>
	public async Task<string> TranscribeAsync(byte[] wavBytes, string language, string prompt) {
		if (!this.initialized || this.factory == null)
			throw new InvalidOperationException("Model is not loaded. Check the model path in Settings.");

		string? modelPath = null;
		try {
			modelPath = this.settingsService.Settings.ModelPath;
		} catch { }

		this.StateChanged?.Invoke(TranscriptionState.Transcribing, "Transcribing…");

		// Build processor with current settings.
		// IMPORTANT: never call WithTranslate() – we want transcription only.
		// Always set an explicit language; fall back to "en" when "auto" is selected
		// to prevent whisper.cpp from silently translating non-English speech.
		var effectiveLanguage = (!string.IsNullOrWhiteSpace(language) && language != "auto")
			? language
			: "en";

		try {
			var builder = this.factory.CreateBuilder()
				.WithLanguage(effectiveLanguage)
				.WithThreads(WhisperService.GetInferenceThreadCount());

			if (!string.IsNullOrEmpty(prompt))
				builder = builder.WithPrompt(prompt);

			await using var processor = builder.Build();

			using var ms = new MemoryStream(wavBytes);
			var segments = new System.Text.StringBuilder();
			await foreach (var seg in processor.ProcessAsync(ms))
				segments.Append(seg.Text);

			var result = segments.ToString().Trim();
			this.StateChanged?.Invoke(TranscriptionState.Done, result);
			return result;
		} catch (WhisperModelLoadException ex) {
			// The factory accepted the file (only reads header) but the model is
			// corrupted or truncated. Delete it so InitializeAsync re-downloads it
			// on next startup, then surface a clear error to the user.
			this.logService.Error("Whisper model corrupted – deleting file for re-download", ex);
			this.initialized = false;
			this.factory.Dispose();
			this.factory = null;
			if (modelPath != null && File.Exists(modelPath)) {
				try { File.Delete(modelPath); } catch (Exception delEx) {
					this.logService.Warning($"Could not delete corrupted model file '{modelPath}'", delEx);
				}
			}
			var userMsg = "Model file is corrupted and has been deleted. Restart the app to re-download it.";
			this.StateChanged?.Invoke(TranscriptionState.Error, userMsg);
			throw new InvalidOperationException(userMsg, ex);
		}
	}

	public async ValueTask DisposeAsync() {
		this.factory?.Dispose();
		await ValueTask.CompletedTask;
	}
}