using Serilog;
using Serilog.Events;
using System.IO;

namespace WhisperWriter.Services;

/// <summary>
/// Application-wide logging facade backed by Serilog.
/// Writes daily rolling log files to the "logs" folder next to the exe.
/// Call LogService.Initialize() once at startup before using Log methods.
/// </summary>
public static class LogService
{
	/// <summary>Directory where log files are written.</summary>
	public static string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

	public static void Initialize()
	{
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
	}

	public static void Info(string message) =>
		Log.Information(message);

	public static void Warning(string message, Exception? ex = null)
	{
		if (ex is null) Log.Warning(message);
		else Log.Warning(ex, message);
	}

	public static void Error(string message, Exception? ex = null)
	{
		if (ex is null) Log.Error(message);
		else Log.Error(ex, message);
	}

	public static void CloseAndFlush() =>
		Log.CloseAndFlush();
}