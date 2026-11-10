using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DrawingRectangle = System.Drawing.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.MainWindows;
using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Utils.Enums;
using WhisperWriter.Views;
using Xunit;

namespace WhisperWriter.Tests.Views;

public class MainWindowTests {
	[Fact]
	public void Activation_IsIdempotentStartsHotkeyAndShowsInitialGpuDetectionState () {
		MainWindowTests.withWindow(fixture => {
			Assert.Equal(new[] { "hotkey-set", "hotkey-start" }, fixture.Runtime.Calls);
			Assert.Equal(new[] { fixture.Runtime.Settings.HotkeyCodes }, fixture.Runtime.SetHotkeyCalls);
			Assert.Equal("GPU Detection…", MainWindowTests.status(fixture.Window).Text);
			Assert.Contains((fixture.Window, "FadeIn", false), fixture.Platform.StoryboardBegins);

			MainWindowTests.invoke(fixture.Window, "OnActivated", EventArgs.Empty);

			Assert.Equal(1, fixture.Runtime.Calls.Count(call => call == "hotkey-start"));
			fixture.Platform.CudaVersion = 12;
			var secondRuntime = new FakeMainWindowRuntime();
			MainWindowTests.setProtectedProperty(fixture.Window, "runtime", secondRuntime);
			MainWindowTests.invoke(fixture.Window, "OnActivated", EventArgs.Empty);
			Assert.Empty(secondRuntime.Calls);
		});
	}

	[Fact]
	public void ReloadHotkey_UpdatesTheLiveServiceUsingCurrentSettings () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.Settings.HotkeyCodes = [0x41, 0x42];

			fixture.Window.ReloadHotkey();

			Assert.Equal(new[] { 0x41, 0x42 }, Assert.Single(fixture.Runtime.UpdateHotkeyCalls));
			Assert.Equal("hotkey-update", fixture.Runtime.Calls[^1]);
		});
	}

	[Fact]
	public void ForceClose_StopsEtaDisposesHotkeyAndAudioThenClosesTheWindow () {
		MainWindowTests.withWindow(fixture => {
			fixture.Window.Show();
			int closedCount = 0;
			fixture.Window.Closed += (_, _) => closedCount++;
			MainWindowTests.invoke(fixture.Window, "startEtaCountdown", 4.0);
			Assert.True(MainWindowTests.etaTimer(fixture.Window).IsEnabled);

			fixture.Window.ForceClose();

			Assert.False(MainWindowTests.etaTimer(fixture.Window).IsEnabled);
			Assert.Equal(new[] { "hotkey-dispose", "audio-dispose" }, fixture.Runtime.Calls.TakeLast(2));
			Assert.True(MainWindowTests.getField<bool>(fixture.Window, "_allowClose"));
			Assert.Equal(1, closedCount);
			Assert.False(fixture.Window.IsVisible);
		});
	}

	[Fact]
	public void OrdinaryClose_IsCancelledAndHidesTheWindowInsteadOfClosingIt () {
		MainWindowTests.withWindow(fixture => {
			fixture.Window.Opacity = 0;
			fixture.Window.ShowInTaskbar = false;
			int closedCount = 0;
			fixture.Window.Closed += (_, _) => closedCount++;
			fixture.Window.Show();

			fixture.Window.Close();

			Assert.False(fixture.Window.IsVisible);
			Assert.Equal(0, closedCount);
			Assert.False(MainWindowTests.getField<bool>(fixture.Window, "_allowClose"));
		});
	}

	[Fact]
	public void PushToTalkStart_SavesFocusThenStartsRecordingAndShowsRecordingState () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.RaisePushToTalkStarted();

			Assert.Equal(new[] { "focus-capture", "recording-reserve", "recording-start" }, fixture.Runtime.Calls.TakeLast(3));
			Assert.Equal("Recording…", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Visibility.Visible, MainWindowTests.amplitudeRow(fixture.Window).Visibility);
		});
	}

	[Fact]
	public void PushToTalkStart_WhenQueueCannotReserveCapacityShowsBusyWithoutCapturing () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.CanReserveRecording = false;
			fixture.Runtime.RaisePushToTalkStarted();

			Assert.Equal(new[] { "focus-capture", "recording-reserve" }, fixture.Runtime.Calls.TakeLast(2));
			Assert.Equal("Busy · transcription queue full", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Visibility.Collapsed, MainWindowTests.amplitudeRow(fixture.Window).Visibility);
			Assert.Same(WpfTestHost.Application.Resources["TextSecondaryBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);
			Assert.DoesNotContain("recording-start", fixture.Runtime.Calls);

			fixture.Runtime.RaisePushToTalkStopped();

			Assert.DoesNotContain("recording-stop", fixture.Runtime.Calls);
			Assert.DoesNotContain("job-enqueue", fixture.Runtime.Calls);
			Assert.Same(WpfTestHost.Application.Resources["AccentBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);
		});
	}

	[Fact]
	public void PushToTalkStop_EnqueuesAnImmutableRequestWithTheFocusAndSettingsCapturedAtStart() {
		MainWindowTests.withWindow(fixture => {
			var target = new WhisperWriter.Services.TextInjectors.FocusTarget(
				new IntPtr(0x1234), new IntPtr(0x5678));
			fixture.Runtime.FocusTargets.Enqueue(target);
			fixture.Runtime.Recording = new byte[32000];
			fixture.Runtime.Settings.ModelPath = "llms/ggml-medium.bin";
			fixture.Runtime.Settings.Language = "cs-CZ";
			fixture.Runtime.Settings.Prompt = "first prompt";
			fixture.Runtime.Settings.HotkeyCodes = [0xA4, 0x5B];
			fixture.Runtime.Settings.CopyToClipboard = false;
			fixture.Runtime.RaisePushToTalkStarted();

			fixture.Runtime.Settings.Language = "en";
			fixture.Runtime.Settings.Prompt = "changed prompt";
			fixture.Runtime.Settings.HotkeyCodes = [0x41];
			fixture.Runtime.Settings.CopyToClipboard = true;
			fixture.Runtime.RaisePushToTalkStopped();

			var job = Assert.Single(fixture.Runtime.EnqueuedJobs);
			Assert.Equal(target, job.Target);
			Assert.Equal("cs-CZ", job.Language);
			Assert.Equal("first prompt", job.Prompt);
			Assert.Equal(new[] { 0xA4, 0x5B }, job.HotkeyCodes);
			Assert.False(job.CopyToClipboard);
			Assert.Equal(32000, job.AudioBytes.Length);
			Assert.DoesNotContain("transcribe", fixture.Runtime.Calls);
		});
	}

	[Fact]
	public void OverlappingRecordings_KeepTheirOwnAudioSettingsAndTargetsWhenCompletedInReverseOrder () {
		MainWindowTests.withWindow(fixture => {
			var firstTarget = new WhisperWriter.Services.TextInjectors.FocusTarget(
				new IntPtr(0x1111), new IntPtr(0x1112));
			var secondTarget = new WhisperWriter.Services.TextInjectors.FocusTarget(
				new IntPtr(0x2221), new IntPtr(0x2222));
			fixture.Runtime.FocusTargets.Enqueue(firstTarget);
			fixture.Runtime.FocusTargets.Enqueue(secondTarget);
			fixture.Runtime.Recording = new byte[32000];
			fixture.Runtime.Settings.ModelPath = "llms/first-model.bin";
			fixture.Runtime.Settings.Language = "cs";
			fixture.Runtime.Settings.Prompt = "first prompt";
			fixture.Runtime.Settings.HotkeyCodes = [0xA4, 0x5B];
			fixture.Runtime.Settings.CopyToClipboard = false;
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();
			TranscriptionJob first = Assert.Single(fixture.Runtime.EnqueuedJobs);

			fixture.Runtime.Recording = new byte[64000];
			fixture.Runtime.Settings.ModelPath = "llms/second-model.bin";
			fixture.Runtime.Settings.Language = "en";
			fixture.Runtime.Settings.Prompt = "second prompt";
			fixture.Runtime.Settings.HotkeyCodes = [0x41];
			fixture.Runtime.Settings.CopyToClipboard = true;
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();
			TranscriptionJob second = fixture.Runtime.EnqueuedJobs[^1];

			Assert.Equal(32000, first.AudioBytes.Length);
			Assert.Equal(64000, second.AudioBytes.Length);
			Assert.NotSame(first.AudioBytes, second.AudioBytes);
			Assert.Equal(firstTarget, first.Target);
			Assert.Equal(secondTarget, second.Target);
			Assert.Equal("first prompt", first.Prompt);
			Assert.Equal("second prompt", second.Prompt);

			fixture.Runtime.RaiseJobCompleted(new TranscriptionJobResult(
				second, "second result", null, TimeSpan.FromMilliseconds(500), false));
			fixture.Runtime.RaiseJobCompleted(new TranscriptionJobResult(
				first, "first result", null, TimeSpan.FromMilliseconds(600), false));

			Assert.Equal(new[] { secondTarget, firstTarget }, fixture.Runtime.RestoredFocusTargets);
			Assert.Equal(new[] { secondTarget, firstTarget }, fixture.Runtime.InjectionTargets);
			Assert.Equal(new[] { "second result", "first result" }, fixture.Runtime.Injections.Select(item => item.Text));
			Assert.Equal(new[] { "second-model", "first-model" }, fixture.Runtime.RecordedEstimates.Select(item => item.ModelKey));
			Assert.Equal(new[] { "second result" }, fixture.Platform.ClipboardTexts);
		});
	}

	[Fact]
	public void PushToTalkStop_IgnoresNullOrTooShortAudioAndReturnsToReadyState () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.Recording = new byte[999];
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();

			Assert.Equal(new[] { "recording-stop", "recording-reservation-cancel" }, fixture.Runtime.Calls.TakeLast(2));
			Assert.DoesNotContain("transcribe", fixture.Runtime.Calls);
			Assert.DoesNotContain("job-enqueue", fixture.Runtime.Calls);
			Assert.DoesNotContain("eta-estimate", fixture.Runtime.Calls);
			Assert.Equal("Ready (Cpu)", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Visibility.Collapsed, MainWindowTests.amplitudeRow(fixture.Window).Visibility);

			fixture.Runtime.Recording = null;
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();
			Assert.DoesNotContain("transcribe", fixture.Runtime.Calls);
		});
	}

	[Fact]
	public void PushToTalkStop_QueuesTheBatchAndInjectsItsCompletedResult () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.Recording = new byte[32000];
			fixture.Runtime.EstimatedProcessingSeconds = 2.2;
			fixture.Runtime.Settings.ModelPath = "llms/ggml-small.bin";
			fixture.Runtime.Settings.Language = "fr";
			fixture.Runtime.Settings.Prompt = "medical context";
			fixture.Runtime.Settings.CopyToClipboard = true;
			fixture.Runtime.Settings.HotkeyCodes = [0xA4, 0x5B];

			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();

			Assert.Contains("eta:ggml-small:1", fixture.Runtime.TranscriptionArguments);
			var job = Assert.Single(fixture.Runtime.EnqueuedJobs);
			Assert.Equal(32000, job.AudioBytes.Length);
			Assert.Equal("fr", job.Language);
			Assert.Equal("medical context", job.Prompt);
			Assert.Empty(fixture.Runtime.HistoryEntries);

			var target = job.Target;
			fixture.Runtime.RaiseJobCompleted(new TranscriptionJobResult(
				job, "recognized text", null, TimeSpan.FromSeconds(1.2), false));

			var entry = Assert.Single(fixture.Runtime.HistoryEntries);
			Assert.Equal("recognized text", entry.Text);
			Assert.Equal(job.CapturedAt.LocalDateTime, entry.Timestamp);
			Assert.Single(fixture.Runtime.TranscriptionLogs);
			Assert.Equal(new[] { "recognized text" }, fixture.Platform.ClipboardTexts);
			Assert.Equal(new[] {
				"history-add", "transcription-log", "focus-restore-target", "background-run", "text-inject-target", "eta-record",
			}, fixture.Runtime.Calls.TakeLast(6));
			Assert.Equal(new[] { target }, fixture.Runtime.RestoredFocusTargets);
			var injection = Assert.Single(fixture.Runtime.Injections);
			Assert.Equal("recognized text", injection.Text);
			Assert.Equal(new[] { 0xA4, 0x5B }, injection.HotkeyCodes);
			Assert.Equal(new[] { target }, fixture.Runtime.InjectionTargets);
			var estimate = Assert.Single(fixture.Runtime.RecordedEstimates);
			Assert.Equal("ggml-small", estimate.ModelKey);
			Assert.Equal(1, estimate.AudioSeconds);
			Assert.Equal(1.2, estimate.ProcessingSeconds, 3);
			Assert.Equal(Visibility.Collapsed, MainWindowTests.etaLabel(fixture.Window).Visibility);
			Assert.Equal("Ready (Cpu)", MainWindowTests.status(fixture.Window).Text);
		});
	}

	[Fact]
	public void PushToTalkStop_SuppressesBlankTextAndReportsTranscriptionErrors () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.Recording = new byte[32000];
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();
			var blankJob = Assert.Single(fixture.Runtime.EnqueuedJobs);
			fixture.Runtime.RaiseJobCompleted(new TranscriptionJobResult(blankJob, " \r\n ", null, TimeSpan.FromSeconds(1), false));
			Assert.Empty(fixture.Runtime.HistoryEntries);
			Assert.DoesNotContain("focus-restore-target", fixture.Runtime.Calls);
			Assert.Empty(fixture.Platform.ClipboardTexts);

			fixture.Runtime.Recording = new byte[32000];
			fixture.Runtime.RaisePushToTalkStarted();
			fixture.Runtime.RaisePushToTalkStopped();
			var failedJob = fixture.Runtime.EnqueuedJobs[^1];
			fixture.Runtime.RaiseJobCompleted(new TranscriptionJobResult(
				failedJob, null, new InvalidOperationException("decoder failed"), TimeSpan.FromSeconds(1), false));

			var error = Assert.Single(fixture.Runtime.Errors);
			Assert.Equal("Transcription failed", error.Message);
			Assert.Equal("decoder failed", error.Exception?.Message);
			Assert.Equal("Error: decoder failed", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Visibility.Collapsed, MainWindowTests.etaLabel(fixture.Window).Visibility);
		});
	}

	[Fact]
	public async Task PushToTalkStop_LogsBackgroundInjectionFailures () {
		var logged = new TaskCompletionSource<(string Message, Exception? Exception)>(TaskCreationOptions.RunContinuationsAsynchronously);
		var fixture = WpfTestHost.Invoke(() => {
			var created = MainWindowTests.createFixture();
			created.Runtime.Recording = new byte[32000];
			created.Runtime.BackgroundException = new InvalidOperationException("injection failed");
			created.Runtime.OnLogError = (message, exception) => logged.TrySetResult((message, exception));
			created.Runtime.RaisePushToTalkStarted();
			created.Runtime.RaisePushToTalkStopped();
			var job = Assert.Single(created.Runtime.EnqueuedJobs);
			created.Runtime.RaiseJobCompleted(new TranscriptionJobResult(
				job, "recognized text", null, TimeSpan.FromSeconds(1), false));
			return created;
		});

		try {
			(string message, Exception? exception) = await logged.Task.WaitAsync(
				TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			Assert.Equal("InjectText failed", message);
			Assert.Equal("injection failed", exception?.Message);
		} finally {
			WpfTestHost.Invoke(() => {
				if (!MainWindowTests.getField<bool>(fixture.Window, "_allowClose"))
					fixture.Window.ForceClose();
				fixture.Settings.Dispose();
			});
		}
	}

	[Fact]
	public void QueueStatus_ShowsActiveAndQueuedCountsWithThreeOrderedEtas () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.ActiveProvider = "Cuda";
			fixture.Runtime.RaiseStateChanged(TranscriptionState.Loading, "Loading model…");
			Assert.Equal("Loading model…", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Color.FromRgb(0xFF, 0xCC, 0x00), ((SolidColorBrush)MainWindowTests.recordingDot(fixture.Window).Fill).Color);

			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				2, 1, 3, 1024, 128L * 1024 * 1024, 2,
				[TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(17), TimeSpan.FromSeconds(25)], 2));
			Assert.Equal("Transcribing 2 · Queued 1", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal("~8s; ~17s; ~25s; +2", MainWindowTests.etaLabel(fixture.Window).Text);
			Assert.Equal(Visibility.Visible, MainWindowTests.etaLabel(fixture.Window).Visibility);

			// Legacy engine-wide transcribe states must not overwrite the queue's per-job state.
			fixture.Runtime.RaiseStateChanged(TranscriptionState.Transcribing, "ignored");
			fixture.Runtime.RaiseStateChanged(TranscriptionState.Done, "ignored");
			Assert.Equal("Transcribing 2 · Queued 1", MainWindowTests.status(fixture.Window).Text);

			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				0, 0, 0, 0, 128L * 1024 * 1024, 2, Array.Empty<TimeSpan?>(), 0));
			Assert.Equal("Ready (Cuda)", MainWindowTests.status(fixture.Window).Text);
			Assert.Equal(Visibility.Collapsed, MainWindowTests.etaLabel(fixture.Window).Visibility);

			fixture.Runtime.RaiseStateChanged(TranscriptionState.Error, "model failure");
			Assert.Equal("model failure", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["AccentRecordingBrush"], MainWindowTests.status(fixture.Window).Foreground);
			fixture.Runtime.RaiseStateChanged((TranscriptionState)999, "ignored");
			Assert.Equal("Ready (Cuda)", MainWindowTests.status(fixture.Window).Text);
		});
	}

	[Fact]
	public void QueueStatus_DimsAdmissionWhenFullAndRestoresBlueWhenCapacityReturns () {
		MainWindowTests.withWindow(fixture => {
			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				2, 1, 3, 96L * 1024L * 1024L, 128L * 1024L * 1024L, 2,
				[TimeSpan.FromSeconds(8)], 0));
			Assert.DoesNotContain("Paused", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["AccentBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);

			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				2, 2, 4, 146L * 1024L * 1024L, 128L * 1024L * 1024L, 2,
				[TimeSpan.FromSeconds(8)], 1) { AdmissionPaused = true });
			Assert.Contains("Paused", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["TextSecondaryBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);

			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				1, 1, 2, 96L * 1024L * 1024L, 128L * 1024L * 1024L, 2,
				[TimeSpan.FromSeconds(8)], 0) { AdmissionPaused = true });
			Assert.Contains("Paused", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["TextSecondaryBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);

			fixture.Runtime.RaiseQueueStatusChanged(new TranscriptionQueueStatus(
				0, 0, 0, 0, 128L * 1024L * 1024L, 2, Array.Empty<TimeSpan?>(), 0));
			Assert.Equal("Ready (Cpu)", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["AccentBrush"], MainWindowTests.recordingDot(fixture.Window).Fill);
		});
	}

	[Fact]
	public void EtaCountdown_UsesInjectedClockCeilsRemainingSecondsAndStopsWhenHidden () {
		MainWindowTests.withWindow(fixture => {
			DateTime start = fixture.Platform.UtcNow;
			MainWindowTests.invoke(fixture.Window, "startEtaCountdown", 0.25);
			Assert.Equal(Visibility.Visible, MainWindowTests.etaLabel(fixture.Window).Visibility);
			Assert.Equal("~1s", MainWindowTests.etaLabel(fixture.Window).Text);
			Assert.True(MainWindowTests.etaTimer(fixture.Window).IsEnabled);

			fixture.Platform.UtcNow = start.AddMilliseconds(500);
			MainWindowTests.invoke(fixture.Window, "handleEtaTick", null, EventArgs.Empty);
			Assert.Equal("~1s", MainWindowTests.etaLabel(fixture.Window).Text);
			fixture.Platform.UtcNow = start.AddSeconds(2);
			MainWindowTests.invoke(fixture.Window, "updateEtaLabel");
			Assert.Equal("~0s", MainWindowTests.etaLabel(fixture.Window).Text);

			MainWindowTests.invoke(fixture.Window, "stopEtaCountdown");
			Assert.Equal(Visibility.Collapsed, MainWindowTests.etaLabel(fixture.Window).Visibility);
			Assert.False(MainWindowTests.etaTimer(fixture.Window).IsEnabled);
		});
	}

	[Fact]
	public void RecordingAmplitudeEvent_ScalesTheMeterAndRecordingStateRestoresIdleUi () {
		MainWindowTests.withWindow(fixture => {
			MainWindowTests.invoke(fixture.Window, "setRecordingState", true);
			var row = MainWindowTests.amplitudeRow(fixture.Window);
			row.Width = 100;
			row.Measure(new Size(100, 3));
			row.Arrange(new Rect(0, 0, 100, 3));

			fixture.Runtime.RaiseAmplitudeChanged(0.1f);

			Assert.Equal(40, MainWindowTests.amplitudeBar(fixture.Window).Width, 4);
			MainWindowTests.invoke(fixture.Window, "setRecordingState", false);
			Assert.Equal(Visibility.Collapsed, row.Visibility);
			Assert.Equal(0, MainWindowTests.amplitudeBar(fixture.Window).Width);
			Assert.Equal("Processing…", MainWindowTests.status(fixture.Window).Text);
			Assert.Contains((fixture.Window, "PulseAnim", true), fixture.Platform.StoryboardBegins);
			Assert.Equal((fixture.Window, "PulseAnim"), Assert.Single(fixture.Platform.StoryboardStops));
		});
	}

	[Fact]
	public void DisplayAndSizeChangesChooseStoredOrDefaultPositionPaths () {
		MainWindowTests.withWindow(fixture => {
			fixture.Settings.Data.WindowLeft = -1;
			fixture.Settings.Data.WindowBottom = -1;
			MainWindowTests.invoke(fixture.Window, "handleWindowDisplayChange");
			Assert.Equal(2, fixture.PositionEnvironment.PrimaryAreaReads);

			fixture.PositionEnvironment.ResetCounts();
			fixture.Settings.Data.WindowLeft = 300;
			fixture.Settings.Data.WindowBottom = 120;
			MainWindowTests.invoke(fixture.Window, "handleWindowDisplayChange");
			Assert.Equal(1, fixture.PositionEnvironment.PrimaryAreaReads);

			fixture.PositionEnvironment.ResetCounts();
			MainWindowTests.invoke(fixture.Window, "handleWindowSizeChanged", fixture.Window, null!);
			Assert.Equal(0, fixture.PositionEnvironment.PrimaryAreaReads);

			fixture.Window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, fixture.Window));
			fixture.Settings.Data.WindowLeft = 300;
			fixture.Settings.Data.WindowBottom = 120;
			fixture.PositionEnvironment.ResetCounts();
			MainWindowTests.invoke(fixture.Window, "handleWindowSizeChanged", fixture.Window, null!);
			Assert.Equal(1, fixture.PositionEnvironment.PrimaryAreaReads);
		});
	}

	[Fact]
	public void XamlMouseBindingsDelegateDragAndHoverEventsWithoutPhysicalInput () {
		MainWindowTests.withWindow(fixture => {
			var border = (Border)fixture.Window.FindName("WidgetBorder");
			var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			};

			border.RaiseEvent(mouseDown);
			border.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) {
				RoutedEvent = UIElement.MouseEnterEvent,
				Source = border,
			});
			border.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) {
				RoutedEvent = UIElement.MouseLeaveEvent,
				Source = border,
			});
			Assert.Equal(new[] { fixture.Window }, fixture.Platform.DraggedWindows);
			Assert.Contains((fixture.Window, "FadeToHover", false), fixture.Platform.StoryboardBegins);
			Assert.Contains((fixture.Window, "FadeToIdle", false), fixture.Platform.StoryboardBegins);
			Assert.NotEqual(-1, fixture.Settings.Data.WindowLeft);
			Assert.NotEqual(-1, fixture.Settings.Data.WindowBottom);
		});
	}

	[Fact]
	public void StatusSetter_UsesTheErrorBrushOnlyForErrorState () {
		MainWindowTests.withWindow(fixture => {
			MainWindowTests.invoke(fixture.Window, "setStatus", "Ready", false);
			Assert.Equal("Ready", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["TextSecondaryBrush"], MainWindowTests.status(fixture.Window).Foreground);
			MainWindowTests.invoke(fixture.Window, "setStatus", "failed", true);
			Assert.Equal("failed", MainWindowTests.status(fixture.Window).Text);
			Assert.Same(WpfTestHost.Application.Resources["AccentRecordingBrush"], MainWindowTests.status(fixture.Window).Foreground);
		});
	}

	private static void withWindow (Action<Fixture> action) {
		WpfTestHost.Invoke(() => {
			var fixture = MainWindowTests.createFixture();
			try {
				action(fixture);
			} finally {
				if (!MainWindowTests.getField<bool>(fixture.Window, "_allowClose"))
					fixture.Window.ForceClose();
				fixture.Settings.Dispose();
			}
		});
	}

	private static Fixture createFixture () {
		var settings = MainWindowTests.createSettings();
		var platform = new FakeWindowPlatform();
		var runtime = new FakeMainWindowRuntime();
		var environment = new FakePositionEnvironment();
		var position = new TestPosition(settings, environment);
		var window = new MainWindow(position, new TestStyle());
		MainWindowTests.setProtectedProperty(window, "settings", settings);
		MainWindowTests.setProtectedProperty(window, "runtime", runtime);
		MainWindowTests.setProtectedProperty(window, "windowPlatform", platform);
		MainWindowTests.invoke(window, "OnActivated", EventArgs.Empty);
		return new Fixture(window, settings, platform, runtime, position, environment);
	}

	private static TestSettings createSettings () {
		string path = Path.Combine(AppContext.BaseDirectory, "phase0-main-window", $"{Guid.NewGuid():N}.json");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		return new TestSettings(path);
	}

	private static void invoke (MainWindow window, string name, params object?[] args) =>
		typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

	private static void setProtectedProperty (MainWindow window, string name, object value) =>
		typeof(MainWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

	private static T getField<T> (MainWindow window, string name) =>
		(T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

	private static DispatcherTimer etaTimer (MainWindow window) => getField<DispatcherTimer>(window, "etaTimer");
	private static TextBlock status (MainWindow window) => (TextBlock)window.FindName("StatusLabel");
	private static TextBlock etaLabel (MainWindow window) => (TextBlock)window.FindName("EtaLabel");
	private static Ellipse recordingDot (MainWindow window) => (Ellipse)window.FindName("RecDot");
	private static Grid amplitudeRow (MainWindow window) => (Grid)window.FindName("AmplitudeRow");
	private static WpfRectangle amplitudeBar (MainWindow window) => (WpfRectangle)window.FindName("AmplitudeBar");

	private sealed record Fixture (MainWindow Window, TestSettings Settings, FakeWindowPlatform Platform,
		FakeMainWindowRuntime Runtime, TestPosition Position, FakePositionEnvironment PositionEnvironment);

	private sealed class TestStyle : WhisperWriter.Services.MainWindows.Style {
		public TestStyle () : base(new FakeWindowStyleNativeApi()) {
		}
	}

	private sealed class FakeWindowStyleNativeApi : IWindowStyleNativeApi {
		public IntPtr GetWindowHandle (Window window) => IntPtr.Zero;
		public int GetExtendedWindowStyle (IntPtr hwnd) => 0;
		public void SetExtendedWindowStyle (IntPtr hwnd, int style) { }
		public void AddHook (IntPtr hwnd, System.Windows.Interop.HwndSourceHook hook) { }
	}

	private sealed class TestPosition : Position {
		public TestPosition (Settings settings, IPositionEnvironment environment) : base(settings, environment) {
		}
	}

	private sealed class FakePositionEnvironment : IPositionEnvironment {
		private static readonly DrawingRectangle _area = new(0, 0, 1920, 1080);
		public int PrimaryAreaReads { get; private set; }

		public DrawingRectangle? GetPrimaryWorkingArea () {
			this.PrimaryAreaReads++;
			return FakePositionEnvironment._area;
		}

		public IReadOnlyList<DrawingRectangle> GetWorkingAreas () => [FakePositionEnvironment._area];
		public (double scaleX, double scaleY) GetPrimaryScreenScale (Window window) => (1, 1);
		public (double scaleX, double scaleY) GetWindowScale (Window window) => (1, 1);
		public double PrimaryScreenWidth => 1920;
		public double PrimaryScreenHeight => 1080;
		public void ResetCounts () => this.PrimaryAreaReads = 0;
	}

	private sealed class TestSettings : Settings, IDisposable {
		private static readonly Log _log = new();
		private readonly string _path;

		public TestSettings (string path) : base(path) {
			this._path = path;
			this.log = MainWindowTests.TestSettings._log;
		}

		public string Path => this._path;

		public void Dispose () {
			if (File.Exists(this._path))
				File.Delete(this._path);
			string? directory = System.IO.Path.GetDirectoryName(this._path);
			if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
				Directory.Delete(directory);
		}
	}
}