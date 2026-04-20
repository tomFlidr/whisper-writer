using System.Collections.ObjectModel;

namespace WhisperWriter.Util;

public class TranscriptionHistory {
	public ObservableCollection<TranscriptionEntry> Entries { get; } = new();

	public int MaxSize { get; set; } = 30;

	private readonly object _lock = new();

	public void Add(TranscriptionEntry entry) {
		lock (this._lock) {
			// Insert newest at the top
			System.Windows.Application.Current.Dispatcher.Invoke(() => {
				this.Entries.Insert(0, entry);
				while (this.Entries.Count > this.MaxSize)
					this.Entries.RemoveAt(this.Entries.Count - 1);
			});
		}
	}
}
