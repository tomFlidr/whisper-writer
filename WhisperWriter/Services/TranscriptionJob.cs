using WhisperWriter.Services.TextInjectors;

namespace WhisperWriter.Services;

/// <summary>Immutable audio and routing context for one completed push-to-talk recording.</summary>
public sealed record TranscriptionJob {
	public Guid Id { get; init; } = Guid.NewGuid();
	public long Sequence { get; init; }
	public long TargetSequence { get; init; }
	public DateTimeOffset CapturedAt { get; init; }
	public byte[] AudioBytes { get; init; } = [];
	public FocusTarget Target { get; init; }
	public string Language { get; init; } = "en";
	public string Prompt { get; init; } = string.Empty;
	public string ModelPath { get; init; } = string.Empty;
	public string ModelKey { get; init; } = string.Empty;
	public IReadOnlyList<int> HotkeyCodes { get; init; } = Array.Empty<int>();
	public bool CopyToClipboard { get; init; }
	public double AudioSeconds { get; init; }
	public double? EstimatedProcessingSeconds { get; init; }
	public int RetryCount { get; init; }
}
