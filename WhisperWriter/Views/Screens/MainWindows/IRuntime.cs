using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Enums;

namespace WhisperWriter.Views.Screens.MainWindows;

public interface IRuntime {
	event Action<TranscriptionState, string>? StateChanged;
	event Action? PushToTalkStarted;
	event Action? PushToTalkStopped;
	event Action<float>? AmplitudeChanged;
	event Action<TranscriptionQueueStatus>? QueueStatusChanged;
	event Action<TranscriptionJobResult>? JobCompleted;
	AppSettings Settings { get; }
	ActiveBackendInfo? ActiveBackend { get; }
	void SetHotkeyCodes (IReadOnlyList<int> virtualKeyCodes);
	void UpdateHotkeyKeys (IReadOnlyList<int> virtualKeyCodes);
	void StartHotkey ();
	void DisposeHotkey ();
	void DisposeAudioRecorder ();
	void StartRecording ();
	byte[]? StopRecording ();
	FocusTarget CaptureFocus ();
	bool TryReserveRecording ();
	void CancelRecordingReservation ();
	void EnqueueTranscription (TranscriptionJob job);
	TranscriptionQueueStatus GetQueueStatus ();
	void RestoreFocus (FocusTarget target);
	void InjectText (string text, IReadOnlyList<int> virtualKeyCodes, FocusTarget target);
	void SaveFocus ();
	void RestoreFocus ();
	void InjectText (string text, IReadOnlyList<int> virtualKeyCodes);
	Task<string> TranscribeAsync (byte[] wav, string language, string prompt);
	void AddHistory (TranscriptionEntry entry);
	void LogTranscription (string text, TimeSpan duration);
	void LogError (string message, Exception? exception);
	double? EstimateProcessingSeconds (string modelKey, double audioSeconds, ActiveBackendInfo? activeBackend);
	void Record (string modelKey, double audioSeconds, double processingSeconds, ActiveBackendInfo? activeBackend);
	Task RunBackground (Action action);
}