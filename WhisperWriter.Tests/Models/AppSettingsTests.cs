using WhisperWriter.Models;
using Xunit;

namespace WhisperWriter.Tests.Models;

public class AppSettingsTests {
	[Fact]
	public void Defaults_MatchTheInitialApplicationConfiguration() {
		var settings = new AppSettings();

		Assert.Equal("llms/ggml-large-v2.bin", settings.ModelPath);
		Assert.Equal("auto", settings.Language);
		Assert.Equal(string.Empty, settings.Prompt);
		Assert.Equal(0x0009, settings.HotkeyModifiers);
		Assert.Equal(new[] { 0xA4, 0x5B }, settings.HotkeyCodes);
		Assert.Equal(30, settings.HistorySize);
		Assert.True(settings.CopyToClipboard);
		Assert.False(settings.RunAtStartup);
		Assert.Equal(-1, settings.WindowLeft);
		Assert.Equal(-1, settings.WindowBottom);
	}
}
