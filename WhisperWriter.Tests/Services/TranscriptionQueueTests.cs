using System.Threading.Channels;
using WhisperWriter.Services;
using WhisperWriter.Services.TextInjectors;
using WhisperWriter.Utils.Enums;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class TranscriptionQueueTests {
	[Fact]
	public async Task Enqueue_StartsJobsUpToTheLimitThenStartsOldestWaitingJobWhenASlotOpens() {
		var engine = new ControlledTranscription();
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(2), new Settings());
		queue.Enqueue(TranscriptionQueueTests.createJob(1));
		queue.Enqueue(TranscriptionQueueTests.createJob(2));
		queue.Enqueue(TranscriptionQueueTests.createJob(3));

		StartedCall first = await engine.ReadStartedAsync();
		StartedCall second = await engine.ReadStartedAsync();
		Assert.Equal(new byte[] { 1, 2 }, new[] { first.Audio[0], second.Audio[0] });
		Assert.False(engine.TryReadStarted(out _));

		first.Completion.SetResult("first result");
		StartedCall third = await engine.ReadStartedAsync();
		Assert.Equal(3, third.Audio[0]);

		second.Completion.SetResult("second result");
		third.Completion.SetResult("third result");
	}

	[Fact]
	public async Task JobCompleted_EmitsSameTargetResultsInRecordingOrder() {
		var engine = new ControlledTranscription();
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(2), new Settings());
		var completed = Channel.CreateUnbounded<Guid>();
		queue.JobCompleted += result => completed.Writer.TryWrite(result.Job.Id);
		var targetWindow = new IntPtr(0xBEEF);
		var firstJob = TranscriptionQueueTests.createJob(11, targetSequence: 1, targetWindow: targetWindow);
		var secondJob = TranscriptionQueueTests.createJob(12, targetSequence: 2, targetWindow: targetWindow);
		queue.Enqueue(firstJob);
		queue.Enqueue(secondJob);

		StartedCall firstCall = await engine.ReadStartedAsync();
		StartedCall secondCall = await engine.ReadStartedAsync();
		secondCall.Completion.SetResult("second");
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 1);
		Assert.False(completed.Reader.TryRead(out _));

		firstCall.Completion.SetResult("first");
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		Guid committedFirst = await completed.Reader.ReadAsync(cancellationToken).AsTask()
			.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
		Guid committedSecond = await completed.Reader.ReadAsync(cancellationToken).AsTask()
			.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		Assert.Equal(firstJob.Id, committedFirst);
		Assert.Equal(secondJob.Id, committedSecond);
	}

	[Fact]
	public async Task AllocationFailure_RetriesTheSameJobAtLowerConcurrencyBeforeLaterQueuedJobs() {
		var engine = new ControlledTranscription();
		var capacity = new FakeCapacityManager(2);
		var queue = new TranscriptionQueue(engine, capacity, new Settings());
		var completed = Channel.CreateUnbounded<TranscriptionJobResult>();
		queue.JobCompleted += result => completed.Writer.TryWrite(result);
		queue.Enqueue(TranscriptionQueueTests.createJob(21, targetWindow: new IntPtr(0x21)));
		queue.Enqueue(TranscriptionQueueTests.createJob(22, targetWindow: new IntPtr(0x22)));
		queue.Enqueue(TranscriptionQueueTests.createJob(23, targetWindow: new IntPtr(0x23)));

		StartedCall first = await engine.ReadStartedAsync();
		StartedCall second = await engine.ReadStartedAsync();
		first.Completion.SetException(new OutOfMemoryException("simulated allocation pressure"));
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 1);
		Assert.Equal(1, capacity.CurrentLimit);
		Assert.Equal(2, queue.GetCounts().Queued);

		second.Completion.SetResult("second result");
		StartedCall retriedFirst = await engine.ReadStartedAsync();
		Assert.Equal((byte)21, retriedFirst.Audio[0]);
		Assert.Equal(1, queue.GetCounts().Active);

		retriedFirst.Completion.SetResult("first result");
		StartedCall last = await engine.ReadStartedAsync();
		Assert.Equal((byte)23, last.Audio[0]);
		last.Completion.SetResult("last result");

		var results = new List<TranscriptionJobResult>();
		for (int index = 0; index < 3; index++)
			results.Add(await completed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
				.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
		Assert.Contains(results, result => result.Job.Sequence == 21 && result.WasRetried);
		Assert.DoesNotContain(results, result => result.Job.Sequence == 21 && result.Error is not null);
	}

	[Fact]
	public async Task NonResourceFailure_DoesNotReportCapacitySuccess () {
		var engine = new ControlledTranscription();
		var capacity = new FakeCapacityManager(1);
		var queue = new TranscriptionQueue(engine, capacity, new Settings());
		var resultSource = new TaskCompletionSource<TranscriptionJobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		queue.JobCompleted += result => resultSource.TrySetResult(result);
		queue.Enqueue(TranscriptionQueueTests.createJob(24));
		StartedCall started = await engine.ReadStartedAsync();
		started.Completion.SetException(new InvalidOperationException("invalid input"));

		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TranscriptionJobResult result = await resultSource.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.Equal(0, capacity.SuccessReports);
	}

	[Fact]
	public async Task GetStatus_ReportsTheNextThreeResultReadyEtasInRecordingOrder() {
		var engine = new ControlledTranscription();
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(2), new Settings());
		var target = new IntPtr(0xBEEF);
		queue.Enqueue(TranscriptionQueueTests.createJob(31, 1, target) with { EstimatedProcessingSeconds = 10 });
		queue.Enqueue(TranscriptionQueueTests.createJob(32, 2, target) with { EstimatedProcessingSeconds = 20 });
		queue.Enqueue(TranscriptionQueueTests.createJob(33, 3, target) with { EstimatedProcessingSeconds = 30 });
		queue.Enqueue(TranscriptionQueueTests.createJob(34, 4, target) with { EstimatedProcessingSeconds = 40 });
		await engine.ReadStartedAsync();
		await engine.ReadStartedAsync();

		TranscriptionQueueStatus status = queue.GetStatus();

		Assert.Equal(2, status.ActiveCount);
		Assert.Equal(2, status.QueuedCount);
		Assert.Equal(new double?[] { 10, 20, 40 }, status.NextResultReadyEtas.Select(eta =>
			eta.HasValue ? (double?)Math.Ceiling(eta.Value.TotalSeconds) : null));
		Assert.Equal(1, status.AdditionalEtaCount);
	}

	[Fact]
	public void TryReserveRecording_RefusesToBeginCaptureWhenAvailableRamCannotReserveQueueBuffer() {
		var engine = new ControlledTranscription();
		var memory = new FakeMemoryMonitor(new InferenceMemorySnapshot(
			"Cpu", "system-ram", null, null, 0, 16L * 1024L * 1024L * 1024L));
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(2), new Settings(), memory);

		Assert.False(queue.TryReserveRecording());
		Assert.Equal(0, queue.GetCounts().Queued);
	}

	[Fact]
	public void TryReserveRecording_UsesTheConfiguredJobAndAudioMemoryLimits () {
		var settings = new Settings();
		settings.Data.MaxOutstandingTranscriptionJobs = 1;
		settings.Data.MaxBufferedAudioMiB = 16;
		var memory = new FakeMemoryMonitor(new InferenceMemorySnapshot(
			"Cpu", "system-ram", null, null, 1024L * 1024L * 1024L, 16L * 1024L * 1024L * 1024L));
		var queue = TranscriptionQueueTests.createConfiguredQueue(
			new ControlledTranscription(), new FakeCapacityManager(1), settings, memory);

		Assert.True(queue.TryReserveRecording());
		Assert.False(queue.TryReserveRecording());
		queue.CancelRecordingReservation();

		Assert.Equal(16L * 1024L * 1024L, queue.GetStatus().BufferedAudioBudgetBytes);
	}

	[Fact]
	public void TryReserveRecording_AllowsAnotherRecordingBelowTheSoftBudgetEvenIfItMayOvershoot () {
		var engine = new ControlledTranscription();
		var settings = new Settings();
		settings.Data.MaxOutstandingTranscriptionJobs = 10;
		settings.Data.MaxBufferedAudioMiB = 16;
		var queue = TranscriptionQueueTests.createConfiguredQueue(engine, new FakeCapacityManager(1), settings);
		Assert.True(queue.TryReserveRecording());
		queue.Enqueue(TranscriptionQueueTests.createJob(43) with { AudioBytes = new byte[15 * 1024 * 1024] });

		Assert.True(queue.TryReserveRecording());
		queue.CancelRecordingReservation();
	}

	[Fact]
	public async Task TryReserveRecording_AllowsOneOverBudgetBatchThenWaitsForItsCompletion () {
		var engine = new ControlledTranscription();
		var settings = new Settings();
		settings.Data.MaxOutstandingTranscriptionJobs = 10;
		settings.Data.MaxBufferedAudioMiB = 8;
		var queue = TranscriptionQueueTests.createConfiguredQueue(engine, new FakeCapacityManager(1), settings);
		Assert.True(queue.TryReserveRecording());
		var oversizedJob = TranscriptionQueueTests.createJob(41) with { AudioBytes = new byte[9 * 1024 * 1024] };
		queue.Enqueue(oversizedJob);
		StartedCall started = await engine.ReadStartedAsync();

		TranscriptionQueueStatus status = queue.GetStatus();
		Assert.True(status.BufferedAudioBytes > status.BufferedAudioBudgetBytes);
		Assert.False(queue.TryReserveRecording());

		started.Completion.SetResult("long recording result");
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 0);
		Assert.True(queue.TryReserveRecording());
		queue.CancelRecordingReservation();
	}

	[Fact]
	public async Task OverBudgetRecording_KeepsNewAdmissionsPausedUntilOutstandingJobsDrain () {
		var engine = new ControlledTranscription();
		var settings = new Settings();
		settings.Data.MaxBufferedAudioMiB = 8;
		var queue = TranscriptionQueueTests.createConfiguredQueue(engine, new FakeCapacityManager(2), settings);
		Assert.True(queue.TryReserveRecording());
		queue.Enqueue(TranscriptionQueueTests.createJob(51) with { AudioBytes = new byte[6 * 1024 * 1024] });
		StartedCall first = await engine.ReadStartedAsync();
		Assert.True(queue.TryReserveRecording());
		queue.Enqueue(TranscriptionQueueTests.createJob(52) with { AudioBytes = new byte[3 * 1024 * 1024] });
		StartedCall second = await engine.ReadStartedAsync();

		first.Completion.SetResult("first");
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 1);
		Assert.Equal(3L * 1024L * 1024L, queue.GetStatus().BufferedAudioBytes);
		Assert.True(queue.GetStatus().AdmissionPaused);
		Assert.False(queue.TryReserveRecording());

		second.Completion.SetResult("second");
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 0);
		Assert.False(queue.GetStatus().AdmissionPaused);
		Assert.True(queue.TryReserveRecording());
		queue.CancelRecordingReservation();
	}

	[Fact]
	public async Task Enqueue_TransfersTheOwnedAudioBufferWithoutCopyingIt () {
		var engine = new ControlledTranscription();
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(1), new Settings());
		var job = TranscriptionQueueTests.createJob(42);
		queue.Enqueue(job);

		StartedCall started = await engine.ReadStartedAsync();

		Assert.Same(job.AudioBytes, started.Audio);
		started.Completion.SetResult("result");
		await TranscriptionQueueTests.waitForActiveCountAsync(queue, 0);
	}

	[Fact]
	public void TryReserveRecording_ReleasesAReservationWhenCaptureIsCancelled() {
		var engine = new ControlledTranscription();
		var memory = new FakeMemoryMonitor(new InferenceMemorySnapshot(
			"Cpu", "system-ram", null, null, 8L * 1024L * 1024L * 1024L, 16L * 1024L * 1024L * 1024L));
		var queue = new TranscriptionQueue(engine, new FakeCapacityManager(2), new Settings(), memory);
		Assert.True(queue.TryReserveRecording());

		queue.CancelRecordingReservation();

		Assert.True(queue.TryReserveRecording());
	}

	private static TranscriptionQueue createConfiguredQueue (
		ITranscription transcription,
		IInferenceCapacityManager capacityManager,
		Settings settings,
		IInferenceMemoryMonitor? memoryMonitor = null
	) => new(transcription, capacityManager, settings, memoryMonitor);

	private static TranscriptionJob createJob(byte id, long targetSequence = 0, IntPtr? targetWindow = null) => new() {
		Id = Guid.NewGuid(),
		Sequence = id,
		TargetSequence = targetSequence == 0 ? id : targetSequence,
		AudioBytes = [id],
		Target = new FocusTarget(targetWindow ?? new IntPtr(id), targetWindow ?? new IntPtr(id)),
		Language = "cs",
		Prompt = string.Empty,
		ModelPath = "model.bin",
		HotkeyCodes = [0xA4, 0x5B],
		CopyToClipboard = false,
		AudioSeconds = 1,
		CapturedAt = DateTimeOffset.UtcNow,
		EstimatedProcessingSeconds = 1,
	};

	private static async Task waitForActiveCountAsync(TranscriptionQueue queue, int expected) {
		var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
		while (queue.GetCounts().Active != expected && DateTimeOffset.UtcNow < timeout)
			await Task.Delay(10);
		Assert.Equal(expected, queue.GetCounts().Active);
	}

	private sealed record StartedCall(byte[] Audio, TaskCompletionSource<string> Completion);

	private sealed class ControlledTranscription : ITranscription {
		private readonly Channel<StartedCall> _started = Channel.CreateUnbounded<StartedCall>();
		public event Action<TranscriptionState, string>? StateChanged {
			add { }
			remove { }
		}
		public string? ActiveProvider => "Cuda";

		public Task InitializeAsync(string modelPath) => Task.CompletedTask;

		public Task<string> TranscribeAsync(byte[] wavBytes, string language, string prompt) {
			var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
			this._started.Writer.TryWrite(new StartedCall(wavBytes, completion));
			return completion.Task;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;

		public Task<StartedCall> ReadStartedAsync() => this._started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

		public bool TryReadStarted(out StartedCall? started) => this._started.Reader.TryRead(out started);
	}

	private sealed class FakeCapacityManager : IInferenceCapacityManager {
		public int CurrentLimit { get; private set; }
		public int SuccessReports { get; private set; }

		public FakeCapacityManager(int limit) => this.CurrentLimit = limit;

		public int GetMaximumConcurrentJobs(TranscriptionJob job, int activeJobs) => this.CurrentLimit;
		public void ReportSuccess(TranscriptionJob job, int concurrentJobs) => this.SuccessReports++;
		public int ReportResourceFailure(TranscriptionJob job, int activeJobs) {
			this.CurrentLimit = Math.Max(1, activeJobs - 1);
			return this.CurrentLimit;
		}
	}

	private sealed class FakeMemoryMonitor(InferenceMemorySnapshot snapshot) : IInferenceMemoryMonitor {
		public InferenceMemorySnapshot GetSnapshot(string? provider) => snapshot;
	}
}
