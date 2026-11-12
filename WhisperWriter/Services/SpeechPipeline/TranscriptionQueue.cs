using System.Diagnostics;
using WhisperWriter.Contracts;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.SpeechPipeline.TranscriptionQueues;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services.SpeechPipeline;

/// <summary>Runs independent transcription jobs with a bounded active-inference count.</summary>
public class TranscriptionQueue : IService, ISingleton {
	public event Action<TranscriptionJobResult>? JobCompleted;
	public event Action<TranscriptionQueueStatus>? StatusChanged;

	private static readonly TimeSpan _memoryRecheckInterval = TimeSpan.FromMilliseconds(250);
	private const long BytesPerMiB = 1024L * 1024L;
	private readonly ITranscription _transcription;
	private readonly IManager _capacityManager;
	private readonly Settings _settings;
	private readonly IMonitor? _memoryMonitor;
	private readonly Func<DateTimeOffset> _utcNow;
	private readonly Queue<TranscriptionJob> _pending = new();
	private readonly object _gate = new();
	private readonly SemaphoreSlim _pumpLock = new(1, 1);
	private readonly Dictionary<Guid, ActiveJob> _activeJobs = new();
	private readonly Dictionary<IntPtr, long> _nextTargetSequenceToCommit = new();
	private readonly Dictionary<IntPtr, long> _lastTargetSequenceAssigned = new();
	private readonly Dictionary<IntPtr, SortedDictionary<long, TranscriptionJobResult>> _completedByTarget = new();
	private int _activeCount;
	private int _recordingReservations;
	private long _bufferedAudioBytes;
	private long _lastQueueBudgetBytes;
	private bool _audioBudgetDrainRequired;
	private int _lastMaxConcurrentJobs = 2;

	public TranscriptionQueue (
		ITranscription transcription,
		IManager capacityManager,
		Settings settings,
		IMonitor? memoryMonitor = null,
		Func<DateTimeOffset>? utcNow = null
	) {
		ArgumentNullException.ThrowIfNull(transcription);
		ArgumentNullException.ThrowIfNull(capacityManager);
		ArgumentNullException.ThrowIfNull(settings);
		this._transcription = transcription;
		this._capacityManager = capacityManager;
		this._settings = settings;
		this._memoryMonitor = memoryMonitor;
		this._utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
		this._lastQueueBudgetBytes = this.getConfiguredAudioBudgetBytes();
	}

	public (int Active, int Queued) GetCounts () {
		lock (this._gate)
			return (this._activeCount, this._pending.Count);
	}

	public TranscriptionQueueStatus GetStatus () {
		lock (this._gate) {
			ActiveJob[] activeJobs = this._activeJobs.Values.ToArray();
			TranscriptionJob[] pendingJobs = this._pending.ToArray();
			TranscriptionJobResult[] completedJobs = this._completedByTarget.Values.SelectMany(results => results.Values).ToArray();
			var etas = this.calculateEtas(activeJobs, pendingJobs, completedJobs, this._lastMaxConcurrentJobs, this._utcNow());
			int outstanding = this.getOutstandingJobCount();
			bool admissionPaused = this._audioBudgetDrainRequired || this._lastQueueBudgetBytes <= 0 ||
				this._bufferedAudioBytes >= this._lastQueueBudgetBytes || outstanding >= this.getMaxOutstandingJobs();
			return new TranscriptionQueueStatus(
				this._activeCount,
				this._pending.Count,
				outstanding,
				this._bufferedAudioBytes,
				this._lastQueueBudgetBytes,
				this._lastMaxConcurrentJobs,
				etas.NextEtas,
				etas.AdditionalCount) {
				AdmissionPaused = admissionPaused,
			};
		}
	}

	public bool TryReserveRecording () {
		long memoryBudget = this.getBufferedAudioBudget();
		bool reserved;
		lock (this._gate) {
			this._lastQueueBudgetBytes = memoryBudget;
			int outstanding = this.getOutstandingJobCount();
			if (memoryBudget > 0 && this._bufferedAudioBytes >= memoryBudget && outstanding > 0)
				this._audioBudgetDrainRequired = true;
			reserved = memoryBudget > 0 &&
				!this._audioBudgetDrainRequired &&
				this.getOutstandingJobCount() < this.getMaxOutstandingJobs() &&
				this._bufferedAudioBytes < memoryBudget;
			if (reserved)
				this._recordingReservations++;
		}
		this.notifyStatusChanged();
		return reserved;
	}

	public void CancelRecordingReservation () {
		lock (this._gate) {
			if (this._recordingReservations <= 0)
				return;
			this._recordingReservations--;
		}
		this.notifyStatusChanged();
	}

	/// <summary>Queues a job and takes ownership of its audio buffer.</summary>
	/// <remarks>The caller must not mutate or reuse <see cref="TranscriptionJob.AudioBytes"/> after this call.</remarks>
	public void Enqueue (TranscriptionJob job) {
		ArgumentNullException.ThrowIfNull(job);
		if (job.AudioBytes.Length == 0)
			throw new ArgumentException("A transcription job must contain audio.", nameof(job));
		long memoryBudget = this.getBufferedAudioBudget();
		lock (this._gate) {
			this._lastQueueBudgetBytes = memoryBudget;
			if (this._recordingReservations > 0) {
				this._recordingReservations--;
			} else if (this.getOutstandingJobCount() >= this.getMaxOutstandingJobs()) {
				throw new InvalidOperationException("The transcription queue is full; reserve a recording slot before capture.");
			}
			IntPtr targetWindow = job.Target.ForegroundWindow;
			this._lastTargetSequenceAssigned.TryGetValue(targetWindow, out long lastAssigned);
			long targetSequence = job.TargetSequence > 0 ? job.TargetSequence : lastAssigned + 1;
			this._lastTargetSequenceAssigned[targetWindow] = Math.Max(lastAssigned, targetSequence);
			if (!this._nextTargetSequenceToCommit.ContainsKey(targetWindow))
				this._nextTargetSequenceToCommit[targetWindow] = targetSequence;
			this._pending.Enqueue(job with {
				TargetSequence = targetSequence,
				AudioBytes = job.AudioBytes,
				HotkeyCodes = job.HotkeyCodes.ToArray(),
			});
			this._bufferedAudioBytes += job.AudioBytes.LongLength;
			if (this._bufferedAudioBytes >= memoryBudget)
				this._audioBudgetDrainRequired = true;
		}
		this.notifyStatusChanged();
		_ = Task.Run(this.pumpAsync);
	}

	private async Task pumpAsync () {
		await this._pumpLock.WaitAsync().ConfigureAwait(false);
		try {
			while (true) {
				TranscriptionJob job;
				int activeCount;
				lock (this._gate) {
					if (this._pending.Count == 0)
						return;
					activeCount = this._activeCount;
					job = this._pending.Peek();
				}

				int maxConcurrent = this._capacityManager.GetMaximumConcurrentJobs(job, activeCount);
				lock (this._gate)
					this._lastMaxConcurrentJobs = Math.Clamp(maxConcurrent, 1, 3);
				if (activeCount >= maxConcurrent) {
					if (activeCount == 0) {
						await Task.Delay(TranscriptionQueue._memoryRecheckInterval).ConfigureAwait(false);
						continue;
					}
					return;
				}

				lock (this._gate) {
					if (this._pending.Count == 0 || activeCount != this._activeCount)
						continue;
					job = this._pending.Dequeue();
					this._activeCount++;
					this._activeJobs[job.Id] = new ActiveJob(job, this._utcNow());
				}
				this.notifyStatusChanged();

				var timer = Stopwatch.StartNew();
				Task<string> transcription;
				try {
					transcription = this._transcription.TranscribeAsync(job.AudioBytes, job.Language, job.Prompt);
				} catch (Exception exception) {
					transcription = Task.FromException<string>(exception);
				}
				_ = this.finishJobAsync(job, transcription, timer, activeCount + 1);
				await Task.Delay(TranscriptionQueue._memoryRecheckInterval).ConfigureAwait(false);
			}
		} finally {
			this._pumpLock.Release();
		}
	}

	private async Task finishJobAsync (TranscriptionJob job, Task<string> transcription, Stopwatch timer, int concurrentJobs) {
		TranscriptionJobResult? result = null;
		try {
			string text = await transcription.ConfigureAwait(false);
			timer.Stop();
			this._capacityManager.ReportSuccess(job, concurrentJobs);
			result = new TranscriptionJobResult(job, text, null, timer.Elapsed, job.RetryCount > 0);
		} catch (Exception exception) {
			timer.Stop();
			if (!this.tryRetryAfterResourceFailure(job, exception))
				result = new TranscriptionJobResult(job, null, exception, timer.Elapsed, job.RetryCount > 0);
		}
		if (result is not null) {
			lock (this._gate)
				this._bufferedAudioBytes = Math.Max(0, this._bufferedAudioBytes - job.AudioBytes.LongLength);
			result = result with { Job = result.Job with { AudioBytes = [] } };
		}

		try {
			if (result is not null)
				this.publishInTargetOrder(result);
		} finally {
			lock (this._gate) {
				this._activeCount--;
				this._activeJobs.Remove(job.Id);
				if (this._audioBudgetDrainRequired && this.getOutstandingJobCount() == 0)
					this._audioBudgetDrainRequired = false;
			}
			this.notifyStatusChanged();
			_ = Task.Run(this.pumpAsync);
		}
	}

	private bool tryRetryAfterResourceFailure (TranscriptionJob job, Exception exception) {
		if (job.RetryCount > 0 || exception is not (OutOfMemoryException or InsufficientMemoryException))
			return false;

		int activeJobs;
		lock (this._gate)
			activeJobs = this._activeCount;
		if (activeJobs <= 1)
			return false;

		int reducedLimit = this._capacityManager.ReportResourceFailure(job, activeJobs);
		if (reducedLimit >= activeJobs)
			return false;

		lock (this._gate) {
			TranscriptionJob[] laterJobs = this._pending.ToArray();
			this._pending.Clear();
			this._pending.Enqueue(job with { RetryCount = job.RetryCount + 1 });
			foreach (TranscriptionJob laterJob in laterJobs)
				this._pending.Enqueue(laterJob);
		}
		return true;
	}

	private void publishInTargetOrder (TranscriptionJobResult result) {
		List<TranscriptionJobResult> ready = [];
		lock (this._gate) {
			IntPtr targetWindow = result.Job.Target.ForegroundWindow;
			if (!this._completedByTarget.TryGetValue(targetWindow, out SortedDictionary<long, TranscriptionJobResult>? completed)) {
				completed = new SortedDictionary<long, TranscriptionJobResult>();
				this._completedByTarget.Add(targetWindow, completed);
			}
			completed[result.Job.TargetSequence] = result;
			long nextSequence = this._nextTargetSequenceToCommit[targetWindow];
			while (completed.Remove(nextSequence, out TranscriptionJobResult? nextResult)) {
				ready.Add(nextResult);
				nextSequence++;
			}
			this._nextTargetSequenceToCommit[targetWindow] = nextSequence;
		}

		foreach (TranscriptionJobResult readyResult in ready)
			this.JobCompleted?.Invoke(readyResult);
	}

	private long getBufferedAudioBudget () {
		long configuredBudget = this.getConfiguredAudioBudgetBytes();
		if (this._memoryMonitor is null)
			return configuredBudget;
		InferenceMemorySnapshot memory;
		try {
			memory = this._memoryMonitor.GetSnapshot(this._transcription.ActiveBackend?.PrimaryProvider);
		} catch {
			return 0;
		}
		if (memory.AvailableSystemMemoryBytes is not long available || available <= 0)
			return 0;
		return Math.Min(configuredBudget, available / 20);
	}

	private long getConfiguredAudioBudgetBytes () =>
		Math.Max(1, this._settings.Data.MaxBufferedAudioMiB) * TranscriptionQueue.BytesPerMiB;

	private int getMaxOutstandingJobs () => Math.Max(1, this._settings.Data.MaxOutstandingTranscriptionJobs);

	private int getOutstandingJobCount () {
		var outstandingIds = new HashSet<Guid>(this._activeJobs.Keys);
		foreach (TranscriptionJob job in this._pending)
			outstandingIds.Add(job.Id);
		foreach (TranscriptionJobResult result in this._completedByTarget.Values.SelectMany(results => results.Values))
			outstandingIds.Add(result.Job.Id);
		return outstandingIds.Count + this._recordingReservations;
	}

	private void notifyStatusChanged () {
		try {
			this.StatusChanged?.Invoke(this.GetStatus());
		} catch { }
	}

	private (IReadOnlyList<TimeSpan?> NextEtas, int AdditionalCount) calculateEtas (
		IReadOnlyList<ActiveJob> activeJobs,
		IReadOnlyList<TranscriptionJob> pendingJobs,
		IReadOnlyList<TranscriptionJobResult> completedJobs,
		int maxConcurrentJobs,
		DateTimeOffset now
	) {
		var finishTimes = this.calculateFinishTimes(activeJobs, pendingJobs, completedJobs, maxConcurrentJobs, now);
		return this.calculateTargetEtas(activeJobs, pendingJobs, completedJobs, finishTimes);
	}

	private Dictionary<Guid, double?> calculateFinishTimes (
		IReadOnlyList<ActiveJob> activeJobs,
		IReadOnlyList<TranscriptionJob> pendingJobs,
		IReadOnlyList<TranscriptionJobResult> completedJobs,
		int maxConcurrentJobs,
		DateTimeOffset now
	) {
		var finishTimes = new Dictionary<Guid, double?>();
		var slotAvailability = new List<double>();
		foreach (ActiveJob active in activeJobs) {
			double? estimated = active.Job.EstimatedProcessingSeconds;
			double elapsed = Math.Max(0, (now - active.StartedAt).TotalSeconds);
			double remaining = Math.Max(0, (estimated ?? active.Job.AudioSeconds) - elapsed);
			slotAvailability.Add(remaining);
			finishTimes[active.Job.Id] = estimated.HasValue ? remaining : null;
		}
		foreach (TranscriptionJobResult completed in completedJobs)
			finishTimes[completed.Job.Id] = 0;

		int slotCount = Math.Max(activeJobs.Count, maxConcurrentJobs);
		while (slotAvailability.Count < slotCount)
			slotAvailability.Add(0);

		foreach (TranscriptionJob pending in pendingJobs) {
			int slotIndex = 0;
			for (int index = 1; index < slotAvailability.Count; index++) {
				if (slotAvailability[index] < slotAvailability[slotIndex])
					slotIndex = index;
			}
			double duration = Math.Max(0, pending.EstimatedProcessingSeconds ?? pending.AudioSeconds);
			double finish = slotAvailability[slotIndex] + duration;
			slotAvailability[slotIndex] = finish;
			finishTimes[pending.Id] = pending.EstimatedProcessingSeconds.HasValue ? finish : null;
		}
		return finishTimes;
	}

	private (IReadOnlyList<TimeSpan?> NextEtas, int AdditionalCount) calculateTargetEtas (
		IReadOnlyList<ActiveJob> activeJobs,
		IReadOnlyList<TranscriptionJob> pendingJobs,
		IReadOnlyList<TranscriptionJobResult> completedJobs,
		Dictionary<Guid, double?> finishTimes
	) {
		var targetReadyTimes = new Dictionary<IntPtr, double?>();
		var orderedJobs = activeJobs.Select(active => active.Job)
			.Concat(pendingJobs)
			.Concat(completedJobs.Select(completed => completed.Job))
			.GroupBy(job => job.Id)
			.Select(group => group.First())
			.OrderBy(job => job.Sequence)
			.ToArray();
		var resultEtas = new List<TimeSpan?>();
		foreach (TranscriptionJob job in orderedJobs) {
			double? finish = finishTimes.GetValueOrDefault(job.Id);
			IntPtr target = job.Target.ForegroundWindow;
			if (targetReadyTimes.TryGetValue(target, out double? previousFinish)) {
				if (finish.HasValue && previousFinish.HasValue)
					finish = Math.Max(finish.Value, previousFinish.Value);
				else
					finish = null;
			}
			targetReadyTimes[target] = finish;
			resultEtas.Add(finish.HasValue ? TimeSpan.FromSeconds(finish.Value) : null);
		}

		var displayed = resultEtas.Take(3).ToArray();
		return (displayed, Math.Max(0, resultEtas.Count - displayed.Length));
	}
}