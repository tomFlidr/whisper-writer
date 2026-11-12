using System.IO;
using WhisperWriter.Contracts;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services.Whispers;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Whisper.net-backed transcription engine.
/// Implements ITranscription; prefers CUDA, falls back to CPU automatically.
/// </summary>
public class Whisper : ITranscription, IService, ISingleton {
	public event Action<TranscriptionState, string>? StateChanged;

	public ActiveBackendInfo? ActiveBackend => this.activeBackend;

	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected Log log { get; set; } = null!;

	// Minimum acceptable file size for any GGML model (tiny.en ≈ 78 MB).
	// A file smaller than this is certainly truncated or corrupted.
	public const long MinModelFileSizeBytes = 70 * 1024 * 1024; // 70 MB

	protected IRuntimeFactory? factory;
	protected bool initialized;
	protected ActiveBackendInfo? activeBackend;
	private readonly IRuntime _runtime;

	public Whisper () : this(new NetRuntime()) { }

	protected Whisper (IRuntime runtime) {
		this._runtime = runtime;
	}

	/// <summary>Returns the number of CPU threads used for Whisper inference.</summary>
	/// <summary>
	/// Number of CPU threads to use for inference. The heuristic leaves two cores free.
	/// </summary>
	public static int GetInferenceThreadCount () => Math.Max(1, Environment.ProcessorCount - 2);

	/// <summary>
	/// Initialises the Whisper model from disk. Call once at startup.
	/// Prefers CUDA; falls back to CPU automatically via Whisper.net.Runtime.Cuda.
	/// </summary>
	/// <summary>
	/// Loads or downloads the GGML model and initializes the WhisperFactory.
	/// Emits StateChanged events for UI feedback and records failures to the log.
	/// </summary>
	public async Task InitializeAsync (string modelPath) {
		this.activeBackend = null;
		this.StateChanged?.Invoke(TranscriptionState.Loading, "Loading model…");

		try {
			// Delete and re-download if the file exists but is clearly truncated.
			if (File.Exists(modelPath) && new FileInfo(modelPath).Length < Whisper.MinModelFileSizeBytes) {
				this.log.Warning($"Data file '{modelPath}' is too small ({new FileInfo(modelPath).Length / 1024 / 1024} MB) — deleting and re-downloading.");
				File.Delete(modelPath);
			}

			// Download model if missing.
			if (!File.Exists(modelPath)) {
				var dir = Path.GetDirectoryName(modelPath)!;
				Directory.CreateDirectory(dir);
				this.StateChanged?.Invoke(TranscriptionState.Loading, "Downloading model…");
				await this._runtime.DownloadModelAsync(modelPath);
			}

			this.factory = this._runtime.CreateFactory(modelPath);
			this.activeBackend = ActiveBackendInfo.FromWhisperNet(this._runtime.ActiveLibrary);
			this.initialized = true;
			this.StateChanged?.Invoke(TranscriptionState.Idle, "Ready");
		} catch (Exception ex) {
			this.initialized = false;
			this.factory = null;
			this.activeBackend = null;
			this.log.Error("Failed to load Whisper model", ex);
			this.StateChanged?.Invoke(TranscriptionState.Error, $"Data load failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Transcribes the given WAV bytes (16 kHz mono PCM).
	/// Returns the transcribed text, or throws on error.
	/// </summary>
	/// <summary>
	/// Transcribes 16 kHz mono WAV bytes using the loaded model and returns the text.
	/// Throws when the model is not initialized or when the model file is corrupted.
	/// </summary>
	public async Task<string> TranscribeAsync (byte[] wavBytes, string language, string prompt) {
		if (!this.initialized || this.factory == null)
			throw new InvalidOperationException("Data is not loaded. Check the model path in Data.");

		string? modelPath = null;
		try {
			modelPath = this.settings.Data.ModelPath;
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
			using var ms = new MemoryStream(wavBytes);
			var segments = new System.Text.StringBuilder();
			await foreach (var segment in this.factory.ProcessAsync(
				ms,
				effectiveLanguage,
				Whisper.GetInferenceThreadCount(),
				string.IsNullOrEmpty(prompt) ? null : prompt))
				segments.Append(segment);

			var result = segments.ToString().Trim();
			this.StateChanged?.Invoke(TranscriptionState.Done, result);
			return result;

		} catch (Exception exception) when (this._runtime.IsModelLoadException(exception)) {
			throw this.createModelLoadException(modelPath, exception);
		}
	}

	public async ValueTask DisposeAsync () {
		this.factory?.Dispose();
		this.activeBackend = null;
		await ValueTask.CompletedTask;
	}

	private InvalidOperationException createModelLoadException (string? modelPath, Exception exception) {
		// Delete a file that passed the header check but failed during processor creation.
		this.log.Error("Whisper model corrupted – deleting file for re-download", exception);
		this.initialized = false;
		this.factory!.Dispose();
		this.factory = null;
		this.activeBackend = null;
		if (modelPath != null && File.Exists(modelPath)) {
			try { File.Delete(modelPath); } catch (Exception deleteException) {
				this.log.Warning($"Could not delete corrupted model file '{modelPath}'", deleteException);
			}
		}
		var userMessage = "Data file is corrupted and has been deleted. Restart the app to re-download it.";
		this.StateChanged?.Invoke(TranscriptionState.Error, userMessage);
		return new InvalidOperationException(userMessage, exception);
	}
}