using System.Collections.ObjectModel;

namespace WhisperWriter.Util;

public class TranscriptionHistory {
	private readonly object _lock = new();
	public ObservableCollection<TranscriptionEntry> Entries { get; } = new();

	public int MaxSize { get; set; } = 30;

	public void Add (TranscriptionEntry entry) {
		lock (_lock) {
			// Insert newest at the top
			System.Windows.Application.Current.Dispatcher.Invoke(() => {
				Entries.Insert(0, entry);
				while (Entries.Count > MaxSize)
					Entries.RemoveAt(Entries.Count - 1);
			});
		}
	}
}
