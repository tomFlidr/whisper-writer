using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Models;

/// <summary>
/// Single transcription history entry containing timestamp, recognised text and processing duration.
/// Immutable once created.
/// </summary>
public class TranscriptionEntry {
	public DateTime Timestamp { get; init; } = DateTime.Now;
	public string Text { get; init; } = string.Empty;
	public TimeSpan Duration { get; init; }
}
