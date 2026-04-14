using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;

namespace WhisperWriter.Services;

/// <summary>
/// Application-wide logging facade backed by Serilog.
/// Writes daily rolling log files to the "logs" folder next to the exe.
/// Call LogService.Initialize() once at startup before using Log methods.
/// </summary>
public static class LogService {
	/// <summary>Directory where log files are written.</summary>
	public static string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

#if DEBUG
	private static Logger? _transcriptionLog;
#endif

	public static void Initialize () {
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

#if DEBUG
		_transcriptionLog = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(
				path: Path.Combine(LogDirectory, "transcriptions.log"),
				rollingInterval: RollingInterval.Infinite,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Duration}] {Message:lj}{NewLine}")
			.CreateLogger();
#endif
	}

	public static void Info (string message) =>
		Log.Information(message);

	public static void Warning (string message, Exception? ex = null) {
		if (ex is null) Log.Warning(message);
		else Log.Warning(ex, message);
	}

	public static void Error (string message, Exception? ex = null) {
		if (ex is null) Log.Error(message);
		else Log.Error(ex, message);
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
		Log.CloseAndFlush();
#if DEBUG
		_transcriptionLog?.Dispose();
#endif
	}
}