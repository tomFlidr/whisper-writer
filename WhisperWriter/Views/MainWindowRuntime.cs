using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.TextInjectors;
using WhisperWriter.Utils.Enums;

namespace WhisperWriter.Views;

public class MainWindowRuntime : IMainWindowRuntime {
	private readonly TranscriptionHistory _history;
	private readonly ITranscription _whisper;
	private readonly EtaCalc _etaCalc;
	private readonly Log _log;
	private readonly TextInjector _textInjector;
	private readonly Settings _settings;
	private readonly AudioRecorder _audioRecorder;
	private readonly Hotkey _hotkey;
	private readonly TranscriptionQueue? _transcriptionQueue;

	public MainWindowRuntime (
		TranscriptionHistory history,
		ITranscription whisper,
		EtaCalc etaCalc,
		Log log,
		TextInjector textInjector,
		Settings settings,
		AudioRecorder audioRecorder,
		Hotkey hotkey,
		TranscriptionQueue? transcriptionQueue = null
	) {
		this._history = history;
		this._whisper = whisper;
		this._etaCalc = etaCalc;
		this._log = log;
		this._textInjector = textInjector;
		this._settings = settings;
		this._audioRecorder = audioRecorder;
		this._hotkey = hotkey;
		this._transcriptionQueue = transcriptionQueue;
	}

	public event Action<TranscriptionState, string>? StateChanged {
		add => this._whisper.StateChanged += value;
		remove => this._whisper.StateChanged -= value;
	}

	public event Action? PushToTalkStarted {
		add => this._hotkey.Push2TalkStarted += value;
		remove => this._hotkey.Push2TalkStarted -= value;
	}

	public event Action? PushToTalkStopped {
		add => this._hotkey.Push2TalkStopped += value;
		remove => this._hotkey.Push2TalkStopped -= value;
	}

	public event Action<float>? AmplitudeChanged {
		add => this._audioRecorder.AmplitudeChanged += value;
		remove => this._audioRecorder.AmplitudeChanged -= value;
	}

	public event Action<TranscriptionQueueStatus>? QueueStatusChanged {
		add {
			if (this._transcriptionQueue is not null)
				this._transcriptionQueue.StatusChanged += value;
		}
		remove {
			if (this._transcriptionQueue is not null)
				this._transcriptionQueue.StatusChanged -= value;
		}
	}

	public event Action<TranscriptionJobResult>? JobCompleted {
		add {
			if (this._transcriptionQueue is not null)
				this._transcriptionQueue.JobCompleted += value;
		}
		remove {
			if (this._transcriptionQueue is not null)
				this._transcriptionQueue.JobCompleted -= value;
		}
	}

	public AppSettings Settings => this._settings.Data;
	public string? ActiveProvider => this._whisper.ActiveProvider;

	public void SetHotkeyCodes (IReadOnlyList<int> virtualKeyCodes) => this._hotkey.SetVirtualKeyCodes(virtualKeyCodes);
	public void UpdateHotkeyKeys (IReadOnlyList<int> virtualKeyCodes) => this._hotkey.UpdateKeys(virtualKeyCodes);
	public void StartHotkey () => this._hotkey.Start();
	public void DisposeHotkey () => this._hotkey.Dispose();
	public void DisposeAudioRecorder () => this._audioRecorder.Dispose();
	public void StartRecording () => this._audioRecorder.StartRecording();
	public byte[]? StopRecording () => this._audioRecorder.StopRecording();
	public FocusTarget CaptureFocus () => this._textInjector.CaptureFocus();
	public bool TryReserveRecording () => this._transcriptionQueue?.TryReserveRecording() ?? false;
	public void CancelRecordingReservation () => this._transcriptionQueue?.CancelRecordingReservation();
	public void EnqueueTranscription (TranscriptionJob job) =>
		(this._transcriptionQueue ?? throw new InvalidOperationException("Transcription queue is not configured.")).Enqueue(job);
	public TranscriptionQueueStatus GetQueueStatus () => this._transcriptionQueue?.GetStatus()
		?? new TranscriptionQueueStatus(0, 0, 0, 0, 0, 1, Array.Empty<TimeSpan?>(), 0);
	public void RestoreFocus (FocusTarget target) => this._textInjector.RestoreFocus(target);
	public void InjectText (string text, IReadOnlyList<int> virtualKeyCodes, FocusTarget target) =>
		this._textInjector.InjectText(text, virtualKeyCodes, target);
	public void SaveFocus () => this._textInjector.SaveFocus();
	public void RestoreFocus () => this._textInjector.RestoreFocus();
	public void InjectText (string text, IReadOnlyList<int> virtualKeyCodes) => this._textInjector.InjectText(text, virtualKeyCodes);
	public Task<string> TranscribeAsync (byte[] wav, string language, string prompt) => this._whisper.TranscribeAsync(wav, language, prompt);
	public void AddHistory (TranscriptionEntry entry) => this._history.Add(entry);
	public void LogTranscription (string text, TimeSpan duration) => this._log.Transcription(text, duration);
	public void LogError (string message, Exception? exception) => this._log.Error(message, exception);
	public double? EstimateProcessingSeconds (string modelKey, double audioSeconds) => this._etaCalc.EstimateProcessingSeconds(modelKey, audioSeconds);
	public void Record (string modelKey, double audioSeconds, double processingSeconds) => this._etaCalc.Record(modelKey, audioSeconds, processingSeconds);
	public Task RunBackground (Action action) => Task.Run(action);
}