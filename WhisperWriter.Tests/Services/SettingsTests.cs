using System.IO;
using WhisperWriter.Models;
using WhisperWriter.Services;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class SettingsTests {
	private static readonly Log _log = new();

	[Fact]
	public void Load_MissingFileCreatesDefaultSettings() {
		SettingsTests.withSettingsFile(null, path => {
			var settings = new TestSettings(path);
			settings.Load();

			Assert.Equal("auto", settings.Data.Language);
			Assert.Equal(30, settings.Data.HistorySize);
			Assert.True(File.Exists(path));
		});
	}

	[Fact]
	public void SaveAndLoad_RoundTripConfiguredSettings() {
		SettingsTests.withSettingsFile(null, path => {
			var settings = new TestSettings(path);
			settings.Data.ModelPath = "llms/test-model.bin";
			settings.Data.Language = "cs";
			settings.Data.Prompt = "domain vocabulary";
			settings.Data.HotkeyCodes = [0xA4, 0x5B, 0x20];
			settings.Data.HistorySize = 7;
			settings.Data.MaxOutstandingTranscriptionJobs = 8;
			settings.Data.MaxBufferedAudioMiB = 64;
			settings.Data.CopyToClipboard = false;
			settings.Data.RunAtStartup = true;
			settings.Data.WindowLeft = 321.5;
			settings.Data.WindowBottom = 54.25;
			settings.Save();

			var loaded = new TestSettings(path);
			loaded.Load();

			Assert.Equal("llms/test-model.bin", loaded.Data.ModelPath);
			Assert.Equal("cs", loaded.Data.Language);
			Assert.Equal("domain vocabulary", loaded.Data.Prompt);
			Assert.Equal(new[] { 0xA4, 0x5B, 0x20 }, loaded.Data.HotkeyCodes);
			Assert.Equal(7, loaded.Data.HistorySize);
			Assert.Equal(8, loaded.Data.MaxOutstandingTranscriptionJobs);
			Assert.Equal(64, loaded.Data.MaxBufferedAudioMiB);
			Assert.False(loaded.Data.CopyToClipboard);
			Assert.True(loaded.Data.RunAtStartup);
			Assert.Equal(321.5, loaded.Data.WindowLeft);
			Assert.Equal(54.25, loaded.Data.WindowBottom);
		});
	}

	[Fact]
	public void Load_LegacySettingsUsesConfiguredQueueDefaults () {
		SettingsTests.withSettingsFile("{\"ModelPath\":\"llms/test-model.bin\"}", path => {
			var settings = new TestSettings(path);
			settings.Load();

			Assert.Equal(10, settings.Data.MaxOutstandingTranscriptionJobs);
			Assert.Equal(128, settings.Data.MaxBufferedAudioMiB);
		});
	}

	[Fact]
	public void Load_InvalidJsonResetsToDefaults() {
		SettingsTests.withSettingsFile("{ invalid json", path => {
			var settings = new TestSettings(path);

			settings.Load();

			Assert.Equal("llms/ggml-large-v2.bin", settings.Data.ModelPath);
			Assert.Equal("auto", settings.Data.Language);
			Assert.Equal(30, settings.Data.HistorySize);
		});
	}

	[Fact]
	public void Save_WhenPathIsDirectoryDoesNotThrow() {
		SettingsTests.withSettingsDirectory(path => {
			var settings = new TestSettings(path);

			Exception? exception = Record.Exception(settings.Save);

			Assert.Null(exception);
		});
	}

	private static void withSettingsFile(string? contents, Action<string> action) {
		string path = SettingsTests.createSettingsPath();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		try {
			if (contents != null)
				File.WriteAllText(path, contents);
			action(path);
		} finally {
			if (File.Exists(path))
				File.Delete(path);
		}
	}

	private static void withSettingsDirectory(Action<string> action) {
		string path = SettingsTests.createSettingsPath();
		Directory.CreateDirectory(path);
		try {
			action(path);
		} finally {
			if (Directory.Exists(path))
				Directory.Delete(path);
		}
	}

	private static string createSettingsPath() {
		return Path.Combine(
			AppContext.BaseDirectory,
			"phase0-settings-tests",
			$"{Guid.NewGuid():N}.json"
		);
	}

	private class TestSettings : Settings {
		public TestSettings(string path) : base(path) {
			this.log = SettingsTests._log;
		}
	}
}
