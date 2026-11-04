using WhisperWriter.Models;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Views;

namespace WhisperWriter.Tests.TestSupport;

internal class FakeMainWindowRuntime : IMainWindowRuntime {
	public event Action<TranscriptionState, string>? StateChanged;
	public event Action? PushToTalkStarted;
	public event Action? PushToTalkStopped;
	public event Action<float>? AmplitudeChanged;

	public AppSettings Settings { get; set; } = new();
	public byte[]? Recording { get; set; }
	public string TranscribedText { get; set; } = "recognized text";
	public Exception? TranscriptionException { get; set; }
	public Exception? BackgroundException { get; set; }
	public double? EstimatedProcessingSeconds { get; set; }
	public Action<string, Exception?>? OnLogError { get; set; }
	public List<string> Calls { get; } = [];
	public List<IReadOnlyList<int>> SetHotkeyCalls { get; } = [];
	public List<IReadOnlyList<int>> UpdateHotkeyCalls { get; } = [];
	public List<TranscriptionEntry> HistoryEntries { get; } = [];
	public List<(string Text, TimeSpan Duration)> TranscriptionLogs { get; } = [];
	public List<(string Message, Exception? Exception)> Errors { get; } = [];
	public List<(string Text, IReadOnlyList<int> HotkeyCodes)> Injections { get; } = [];
	public List<(string ModelKey, double AudioSeconds, double ProcessingSeconds)> RecordedEstimates { get; } = [];
	public List<string> TranscriptionArguments { get; } = [];

	public void SetHotkeyCodes (IReadOnlyList<int> virtualKeyCodes) {
		this.Calls.Add("hotkey-set");
		this.SetHotkeyCalls.Add(virtualKeyCodes.ToArray());
	}

	public void UpdateHotkeyKeys (IReadOnlyList<int> virtualKeyCodes) {
		this.Calls.Add("hotkey-update");
		this.UpdateHotkeyCalls.Add(virtualKeyCodes.ToArray());
	}

	public void StartHotkey () => this.Calls.Add("hotkey-start");
	public void DisposeHotkey () => this.Calls.Add("hotkey-dispose");
	public void DisposeAudioRecorder () => this.Calls.Add("audio-dispose");
	public void StartRecording () => this.Calls.Add("recording-start");

	public byte[]? StopRecording () {
		this.Calls.Add("recording-stop");
		return this.Recording;
	}

	public void SaveFocus () => this.Calls.Add("focus-save");
	public void RestoreFocus () => this.Calls.Add("focus-restore");

	public void InjectText (string text, IReadOnlyList<int> virtualKeyCodes) {
		this.Calls.Add("text-inject");
		this.Injections.Add((text, virtualKeyCodes.ToArray()));
	}

	public Task<string> TranscribeAsync (byte[] wav, string language, string prompt) {
		this.Calls.Add("transcribe");
		this.TranscriptionArguments.Add($"{wav.Length}:{language}:{prompt}");
		return this.TranscriptionException is null
			? Task.FromResult(this.TranscribedText)
			: Task.FromException<string>(this.TranscriptionException);
	}

	public void AddHistory (TranscriptionEntry entry) {
		this.Calls.Add("history-add");
		this.HistoryEntries.Add(entry);
	}

	public void LogTranscription (string text, TimeSpan duration) {
		this.Calls.Add("transcription-log");
		this.TranscriptionLogs.Add((text, duration));
	}

	public void LogError (string message, Exception? exception) {
		this.Calls.Add("error-log");
		this.Errors.Add((message, exception));
		this.OnLogError?.Invoke(message, exception);
	}

	public double? EstimateProcessingSeconds (string modelKey, double audioSeconds) {
		this.Calls.Add("eta-estimate");
		this.TranscriptionArguments.Add($"eta:{modelKey}:{audioSeconds}");
		return this.EstimatedProcessingSeconds;
	}

	public void Record (string modelKey, double audioSeconds, double processingSeconds) {
		this.Calls.Add("eta-record");
		this.RecordedEstimates.Add((modelKey, audioSeconds, processingSeconds));
	}

	public Task RunBackground (Action action) {
		this.Calls.Add("background-run");
		if (this.BackgroundException is not null)
			return Task.FromException(this.BackgroundException);
		try {
			action();
			return Task.CompletedTask;
		} catch (Exception exception) {
			return Task.FromException(exception);
		}
	}

	public void RaiseStateChanged (TranscriptionState state, string message) => this.StateChanged?.Invoke(state, message);
	public void RaisePushToTalkStarted () => this.PushToTalkStarted?.Invoke();
	public void RaisePushToTalkStopped () => this.PushToTalkStopped?.Invoke();
	public void RaiseAmplitudeChanged (float rms) => this.AmplitudeChanged?.Invoke(rms);
}