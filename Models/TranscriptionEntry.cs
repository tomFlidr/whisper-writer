using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WhisperWriter.Models;

public class TranscriptionEntry {
	public DateTime Timestamp { get; init; } = DateTime.Now;
	public string Text { get; init; } = string.Empty;
	public TimeSpan Duration { get; init; }
}