using System.Collections.ObjectModel;
using WhisperWriter.Models;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

public class TranscriptionHistory: IService, ISingleton {
	/// <summary>
	/// In-memory observable collection holding the most recent transcription entries.
	/// Thread-safe Add ensures UI updates occur on the Dispatcher and the collection size
	/// is capped to MaxSize.
	/// </summary>
	public ObservableCollection<TranscriptionEntry> Entries { get; } = new();

	public int MaxSize { get; set; } = 30;

	private readonly object _lock = new();

	/// <summary>
	/// Adds a new entry to the top of the history, trims excess entries and marshals
	/// the collection change to the UI thread.
	/// </summary>
	public void Add (TranscriptionEntry entry) {
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
