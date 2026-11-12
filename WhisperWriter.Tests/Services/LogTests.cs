using System.IO;
using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class LogTests {
	private static string createLogDirectory () {
		return Path.Combine(Path.GetTempPath(), $"WhisperWriter.LogTests-{Guid.NewGuid():N}");
	}

#if DEBUG
	[Fact]
	public void LoggingMethods_WriteToExpectedIsolatedSinks_AndFlushesOnClose () {
		string logDirectory = LogTests.createLogDirectory();
		var log = new TestLog(logDirectory);
		string infoMessage = $"info-{Guid.NewGuid():N}";
		string warningMessage = $"warning-{Guid.NewGuid():N}";
		string warningMessageWithoutException = $"warning-no-exception-{Guid.NewGuid():N}";
		string warningException = $"warning-exception-{Guid.NewGuid():N}";
		string errorMessage = $"error-{Guid.NewGuid():N}";
		string errorMessageWithoutException = $"error-no-exception-{Guid.NewGuid():N}";
		string errorException = $"error-exception-{Guid.NewGuid():N}";
		string transcription = $"transcription-{Guid.NewGuid():N}";

		try {
			Assert.Equal(logDirectory, log.LogDirectory);
			Assert.True(Directory.Exists(logDirectory));

			log.Info(infoMessage);
			log.Warning(warningMessage, new InvalidOperationException(warningException));
			log.Warning(warningMessageWithoutException);
			log.Error(errorMessage, new InvalidOperationException(errorException));
			log.Error(errorMessageWithoutException);
			log.Transcription(transcription, TimeSpan.FromMilliseconds(2500));
			log.CloseAndFlush();
			log.CloseAndFlush();

			string[] applicationLogs = Directory.GetFiles(logDirectory, "whisperwriter-*.log");
			Assert.Single(applicationLogs);
			string applicationLog = File.ReadAllText(applicationLogs[0]);
			Assert.Contains(infoMessage, applicationLog);
			Assert.Contains(warningMessage, applicationLog);
			Assert.Contains(warningMessageWithoutException, applicationLog);
			Assert.Contains(warningException, applicationLog);

			string transcriptionLogPath = Path.Combine(logDirectory, "transcriptions.log");
			Assert.True(File.Exists(transcriptionLogPath));
			string transcriptionLog = File.ReadAllText(transcriptionLogPath);
			Assert.Contains(errorMessage, transcriptionLog);
			Assert.Contains(errorMessageWithoutException, transcriptionLog);
			Assert.Contains(transcription, transcriptionLog);
			Assert.Contains("00:00:02.500", transcriptionLog);
		} finally {
			log.CloseAndFlush();
			if (Directory.Exists(logDirectory))
				Directory.Delete(logDirectory, recursive: true);
		}
	}
#else
	[Fact]
	public void LoggingMethods_AreNoOpsWithoutCreatingOutputDirectory () {
		string logDirectory = LogTests.createLogDirectory();
		var log = new TestLog(logDirectory);

		try {
			Assert.Equal(logDirectory, log.LogDirectory);
			Assert.False(Directory.Exists(logDirectory));

			log.Info("release-info");
			log.Warning("release-warning", new InvalidOperationException("release-warning-exception"));
			log.Warning("release-warning-no-exception");
			log.Error("release-error", new InvalidOperationException("release-error-exception"));
			log.Error("release-error-no-exception");
			log.Transcription("release-transcription", TimeSpan.FromSeconds(1));
			log.CloseAndFlush();

			Assert.False(Directory.Exists(logDirectory));
		} finally {
			if (Directory.Exists(logDirectory))
				Directory.Delete(logDirectory, recursive: true);
		}
	}
#endif

	private class TestLog : Log {
		public TestLog (string logDirectory) : base(logDirectory) {
		}
	}
}