using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.TextInjectors;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Views;

namespace WhisperWriter.Tests.TestSupport;

internal class FakeMainWindowRuntime : IMainWindowRuntime {
	public event Action<TranscriptionState, string>? StateChanged;
	public event Action? PushToTalkStarted;
	public event Action? PushToTalkStopped;
	public event Action<float>? AmplitudeChanged;
	public event Action<TranscriptionQueueStatus>? QueueStatusChanged;
	public event Action<TranscriptionJobResult>? JobCompleted;

	public AppSettings Settings { get; set; } = new();
	public string? ActiveProvider { get; set; } = "Cpu";
	public byte[]? Recording { get; set; }
	public bool CanReserveRecording { get; set; } = true;
	public TranscriptionQueueStatus QueueStatus { get; set; } = new(0, 0, 0, 0, 128L * 1024 * 1024, 2, Array.Empty<TimeSpan?>(), 0);
	public string TranscribedText { get; set; } = "recognized text";
	public Exception? TranscriptionException { get; set; }
	public Exception? BackgroundException { get; set; }
	public double? EstimatedProcessingSeconds { get; set; }
	public Action<string, Exception?>? OnLogError { get; set; }
	public Action? OnInjected { get; set; }
	public List<string> Calls { get; } = [];
	public List<IReadOnlyList<int>> SetHotkeyCalls { get; } = [];
	public List<IReadOnlyList<int>> UpdateHotkeyCalls { get; } = [];
	public List<TranscriptionEntry> HistoryEntries { get; } = [];
	public List<(string Text, TimeSpan Duration)> TranscriptionLogs { get; } = [];
	public List<(string Message, Exception? Exception)> Errors { get; } = [];
	public List<(string Text, IReadOnlyList<int> HotkeyCodes)> Injections { get; } = [];
	public List<(string ModelKey, double AudioSeconds, double ProcessingSeconds)> RecordedEstimates { get; } = [];
	public List<string> TranscriptionArguments { get; } = [];
	public Queue<FocusTarget> FocusTargets { get; } = new();
	public List<FocusTarget> CapturedFocusTargets { get; } = [];
	public List<FocusTarget> RestoredFocusTargets { get; } = [];
	public List<FocusTarget> InjectionTargets { get; } = [];
	public List<TranscriptionJob> EnqueuedJobs { get; } = [];
	public int ReservationCount { get; private set; }

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

	public FocusTarget CaptureFocus () {
		this.Calls.Add("focus-capture");
		FocusTarget target = this.FocusTargets.Count > 0 ? this.FocusTargets.Dequeue() : default;
		this.CapturedFocusTargets.Add(target);
		return target;
	}

	public bool TryReserveRecording () {
		this.Calls.Add("recording-reserve");
		if (this.CanReserveRecording)
			this.ReservationCount++;
		return this.CanReserveRecording;
	}

	public void CancelRecordingReservation () {
		this.Calls.Add("recording-reservation-cancel");
		this.ReservationCount = Math.Max(0, this.ReservationCount - 1);
	}

	public void EnqueueTranscription (TranscriptionJob job) {
		this.Calls.Add("job-enqueue");
		this.EnqueuedJobs.Add(job);
		this.ReservationCount = Math.Max(0, this.ReservationCount - 1);
		this.QueueStatus = new TranscriptionQueueStatus(
			0, this.EnqueuedJobs.Count, this.EnqueuedJobs.Count, this.EnqueuedJobs.Sum(item => item.AudioBytes.LongLength),
			128L * 1024 * 1024, 2, [TimeSpan.FromSeconds(1)], 0);
		this.QueueStatusChanged?.Invoke(this.QueueStatus);
	}

	public TranscriptionQueueStatus GetQueueStatus () => this.QueueStatus;

	public void RestoreFocus (FocusTarget target) {
		this.Calls.Add("focus-restore-target");
		this.RestoredFocusTargets.Add(target);
	}

	public void InjectText (string text, IReadOnlyList<int> virtualKeyCodes, FocusTarget target) {
		this.Calls.Add("text-inject-target");
		this.InjectionTargets.Add(target);
		this.Injections.Add((text, virtualKeyCodes.ToArray()));
		this.OnInjected?.Invoke();
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
	public void RaiseQueueStatusChanged (TranscriptionQueueStatus status) {
		this.QueueStatus = status;
		this.QueueStatusChanged?.Invoke(status);
	}
	public void RaiseJobCompleted (TranscriptionJobResult result) {
		this.QueueStatus = new TranscriptionQueueStatus(0, 0, 0, 0, 128L * 1024 * 1024, 2, Array.Empty<TimeSpan?>(), 0);
		this.QueueStatusChanged?.Invoke(this.QueueStatus);
		this.JobCompleted?.Invoke(result);
	}
}