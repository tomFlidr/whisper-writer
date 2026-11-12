using WhisperWriter.Models;

namespace WhisperWriter.Views.Screens.MainWindows;

internal record RecordingContext (
	FocusTarget Target,
	string Language,
	string Prompt,
	string ModelPath,
	IReadOnlyList<int> HotkeyCodes,
	bool CopyToClipboard,
	DateTimeOffset CapturedAt,
	long Sequence
);