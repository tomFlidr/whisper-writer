using System.IO;
#if DEBUG
using Serilog;
using Serilog.Core;
#endif

namespace WhisperWriter.Services;

/// <summary>
/// Application-wide logging facade backed by Serilog.
/// All logging is active in DEBUG builds only; Release builds are no-ops.
/// </summary>
public static class LogService {
	/// <summary>Directory where log files are written.</summary>
	public static string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

#if DEBUG
	private static Logger? _transcriptionLog;
#endif

	public static void Initialize () {
#if DEBUG
		Directory.CreateDirectory(LogDirectory);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.File(
				path: Path.Combine(LogDirectory, "whisperwriter-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 14,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		Log.Information("WhisperWriter starting up");

		_transcriptionLog = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(
				path: Path.Combine(LogDirectory, "transcriptions.log"),
				rollingInterval: RollingInterval.Infinite,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Duration}] {Message:lj}{NewLine}")
			.CreateLogger();
#endif
	}

	public static void Info (string message) {
#if DEBUG
		Log.Information(message);
#endif
	}

	public static void Warning (string message, Exception? ex = null) {
#if DEBUG
		if (ex is null) Log.Warning(message);
		else Log.Warning(ex, message);
#endif
	}

	public static void Error (string message, Exception? ex = null) {
#if DEBUG
		if (ex is null) Log.Error(message);
		else Log.Error(ex, message);
#endif
	}

	/// <summary>
	/// Logs a completed transcription entry (DEBUG builds only).
	/// Writes timestamp, transcription duration and the transcribed text to transcriptions.log.
	/// </summary>
	public static void Transcription (string text, TimeSpan duration) {
#if DEBUG
		_transcriptionLog?.Information("[{Duration:hh\\:mm\\:ss\\.fff}] {Text}", duration, text);
#endif
	}

	public static void CloseAndFlush () {
#if DEBUG
		Log.CloseAndFlush();
		_transcriptionLog?.Dispose();
#endif
	}
}