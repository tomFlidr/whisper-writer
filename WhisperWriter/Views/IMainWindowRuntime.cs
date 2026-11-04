using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Enums;

namespace WhisperWriter.Views;

public interface IMainWindowRuntime {
	event Action<TranscriptionState, string>? StateChanged;
	event Action? PushToTalkStarted;
	event Action? PushToTalkStopped;
	event Action<float>? AmplitudeChanged;
	AppSettings Settings { get; }
	void SetHotkeyCodes (IReadOnlyList<int> virtualKeyCodes);
	void UpdateHotkeyKeys (IReadOnlyList<int> virtualKeyCodes);
	void StartHotkey ();
	void DisposeHotkey ();
	void DisposeAudioRecorder ();
	void StartRecording ();
	byte[]? StopRecording ();
	void SaveFocus ();
	void RestoreFocus ();
	void InjectText (string text, IReadOnlyList<int> virtualKeyCodes);
	Task<string> TranscribeAsync (byte[] wav, string language, string prompt);
	void AddHistory (TranscriptionEntry entry);
	void LogTranscription (string text, TimeSpan duration);
	void LogError (string message, Exception? exception);
	double? EstimateProcessingSeconds (string modelKey, double audioSeconds);
	void Record (string modelKey, double audioSeconds, double processingSeconds);
	Task RunBackground (Action action);
}