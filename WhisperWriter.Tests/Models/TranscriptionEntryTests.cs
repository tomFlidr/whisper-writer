using WhisperWriter.Models;
using Xunit;

namespace WhisperWriter.Tests.Models;

public class TranscriptionEntryTests {
	[Fact]
	public void Constructor_SetsEmptyTextZeroDurationAndCurrentTimestamp () {
		var before = DateTime.Now;
		var entry = new TranscriptionEntry();
		var after = DateTime.Now;

		Assert.Equal(string.Empty, entry.Text);
		Assert.Equal(TimeSpan.Zero, entry.Duration);
		Assert.InRange(entry.Timestamp, before, after);
	}

	[Fact]
	public void Initializer_PreservesProvidedValues () {
		var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
		var duration = TimeSpan.FromMilliseconds(1234);
		var entry = new TranscriptionEntry {
			Timestamp = timestamp,
			Text = "Recognized words",
			Duration = duration,
		};

		Assert.Equal(timestamp, entry.Timestamp);
		Assert.Equal("Recognized words", entry.Text);
		Assert.Equal(duration, entry.Duration);
	}
}