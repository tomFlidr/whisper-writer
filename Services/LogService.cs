using System.IO;
#if DEBUG
using Serilog;
using Serilog.Core;
using WhisperWriter.Utils.Interfaces;
#endif

namespace WhisperWriter.Services;

/// <summary>
/// Application-wide logging facade backed by Serilog.
/// All logging is active in DEBUG builds only; Release builds are no-ops.
/// </summary>
public class LogService: IService, ISingleton {
	/// <summary>Directory where log files are written.</summary>
	public string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

#if DEBUG
	private Logger _provider;
#endif

	public LogService () {
#if DEBUG
		Directory.CreateDirectory(this.LogDirectory);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.File(
				path: Path.Combine(this.LogDirectory, "whisperwriter-.log"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 14,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		Log.Information("WhisperWriter starting up");

		this._provider = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(
				path: Path.Combine(this.LogDirectory, "transcriptions.log"),
				rollingInterval: RollingInterval.Infinite,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Duration}] {Message:lj}{NewLine}")
			.CreateLogger();
#endif
	}

	public void Info (string message) {
#if DEBUG
		Log.Information(message);
#endif
	}

	public void Warning (string message, Exception? ex = null) {
#if DEBUG
		if (ex is null) Log.Warning(message);
		else Log.Warning(ex, message);
#endif
	}

	public void Error (string message, Exception? ex = null) {
#if DEBUG
		if (ex is null) {
			this._provider?.Error(message);
		} else {
			this._provider?.Error(ex, message);
		}
#endif
	}

	/// <summary>
	/// Logs a completed transcription entry (DEBUG builds only).
	/// Writes timestamp, transcription duration and the transcribed text to transcriptions.log.
	/// </summary>
	public void Transcription (string text, TimeSpan duration) {
#if DEBUG
		this._provider?.Information("[{Duration:hh\\:mm\\:ss\\.fff}] {Text}", duration, text);
#endif
	}

	public void CloseAndFlush () {
#if DEBUG
		Log.CloseAndFlush();
		this._provider?.Dispose();
#endif
	}
}