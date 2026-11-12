using System.Collections.Specialized;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Tests.TestSupport;
using Xunit;

namespace WhisperWriter.Tests.Services.Persistence;

public class TranscriptionHistoryTests {
	[Fact]
	public void Add_InsertsNewestFirstAndTrimsOldestEntries () {
		WpfTestHost.Invoke(() => {
			var history = new TranscriptionHistory { MaxSize = 2 };
			var oldest = new TranscriptionEntry { Text = "oldest" };
			var middle = new TranscriptionEntry { Text = "middle" };
			var newest = new TranscriptionEntry { Text = "newest" };

			history.Add(oldest);
			history.Add(middle);
			history.Add(newest);

			Assert.Equal(new[] { newest, middle }, history.Entries);
		});
	}

	[Fact]
	public async Task Add_RaisesCollectionChangeOnTheWpfDispatcherThread () {
		var history = WpfTestHost.Invoke(() => new TranscriptionHistory());
		int changedThreadId = 0;
		history.Entries.CollectionChanged += (_, _) => {
			changedThreadId = Environment.CurrentManagedThreadId;
		};

		await Task.Run(
			() => history.Add(new TranscriptionEntry { Text = "background" }),
			TestContext.Current.CancellationToken
		);

		Assert.Equal(WpfTestHost.ThreadId, changedThreadId);
		Assert.Equal("background", history.Entries.Single().Text);
	}
}