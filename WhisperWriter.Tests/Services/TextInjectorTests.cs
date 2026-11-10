using System.Reflection;
using System.Runtime.InteropServices;
using Autofac;
using Serilog.Core;
using Serilog.Events;
using WhisperWriter.Services;
using WhisperWriter.Services.TextInjectors;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class TextInjectorTests {
	[Fact]
	public void ParameterlessConstructor_RemainsResolvableWithoutPlatformRegistration() {
		var builder = new ContainerBuilder();
		builder.RegisterType<TextInjector>().AsSelf();
		using var container = builder.Build();

		Assert.IsType<TextInjector>(container.Resolve<TextInjector>());
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void InjectText_DoesNothingForEmptyInput(string? text) {
		var platform = new FakePlatform();
		var injector = new TestTextInjector(platform);

		injector.InjectText(text!, null!);

		Assert.Empty(platform.Calls);
		Assert.Empty(platform.SendInputCalls);
		Assert.Empty(platform.Delays);
	}

	[Fact]
	public void SaveAndRestoreFocus_UsesSavedWindowAndReleasesThreadAttachmentInOrder() {
		var platform = new FakePlatform {
			ForegroundWindow = new IntPtr(0x1234),
			TargetThreadId = 34,
			CurrentThreadId = 12,
		};
		var injector = new TestTextInjector(platform);

		injector.SaveFocus();
		injector.RestoreFocus();

		Assert.Equal(new[] {
			"GetForegroundWindow",
			"GetFocusedWindow:1234",
			"GetWindowThreadProcessId:1234",
			"GetCurrentThreadId",
			"AttachThreadInput:12:34:True",
			"SetForegroundWindow:1234",
			"BringWindowToTop:1234",
			"AttachThreadInput:12:34:False",
		}, platform.Calls);
	}

	[Fact]
	public void CaptureFocusSnapshots_AllowOverlappingJobsToRestoreTheirOwnTargetIndependently() {
		var platform = new FakePlatform {
			ForegroundWindow = new IntPtr(0x1234),
			FocusedWindow = new IntPtr(0x1111),
		};
		var injector = new TestTextInjector(platform);

		var firstTarget = injector.CaptureFocus();
		platform.ForegroundWindow = new IntPtr(0x2345);
		platform.FocusedWindow = new IntPtr(0x2222);
		var secondTarget = injector.CaptureFocus();

		injector.RestoreFocus(firstTarget);
		injector.RestoreFocus(secondTarget);

		Assert.Equal(new[] {
			(new IntPtr(0x1234), new IntPtr(0x1111)),
			(new IntPtr(0x2345), new IntPtr(0x2222)),
		}, platform.RestoredFocusTargets);
	}
	[Fact]
	public void InjectText_ReassertsTheJobTargetAfterWaitBeforeSendingKeys() {
		var platform = new FakePlatform {
			ForegroundWindow = new IntPtr(0x1234),
			FocusedWindow = new IntPtr(0x1111),
		};
		using var logCapture = new LogCapture();
		var injector = new TestTextInjector(platform, logCapture.Log);
		FocusTarget target = injector.CaptureFocus();
		platform.ForegroundWindow = new IntPtr(0x2345);
		platform.FocusedWindow = new IntPtr(0x2222);
		platform.Calls.Clear();

		injector.InjectText("x", Array.Empty<int>(), target);

		int restoreIndex = platform.Calls.IndexOf("SetForegroundWindow:1234");
		int sendIndex = platform.Calls.LastIndexOf("SendInput:2");
		Assert.True(restoreIndex >= 0 && restoreIndex < sendIndex);
		Assert.Contains("BringWindowToTop:1234", platform.Calls);
	}
	[Fact]
	public void InjectText_WaitsForPhysicalKeyReleaseBeforeSendingInput() {
		using var logCapture = new LogCapture();
		var platform = new FakePlatform();
		int pttKeyReads = 0;
		platform.KeyStateReader = virtualKey => {
			if (virtualKey == 0xA4 && pttKeyReads++ == 0)
				return unchecked((short)0x8000);
			return 0;
		};
		var injector = new TestTextInjector(platform, logCapture.Log);

		injector.InjectText("x", [0xA4]);

		Assert.Equal(new[] {
			"GetAsyncKeyState:A4",
			"Delay:10",
			"GetAsyncKeyState:A4",
			"GetAsyncKeyState:5B",
			"GetAsyncKeyState:5C",
			"SendInput:8",
			"Delay:80",
			"SendInput:2",
		}, platform.Calls);
		Assert.Equal(new[] { 10, 80 }, platform.Delays);
	}

	[Fact]
	public void InjectText_ContinuesAfterPhysicalKeyWaitTimesOut() {
		using var logCapture = new LogCapture();
		var platform = new FakePlatform {
			KeyStateReader = _ => unchecked((short)0x8000),
		};
		var injector = new TestTextInjector(platform, logCapture.Log);

		injector.InjectText("x", [0xA4]);

		Assert.Equal(200, platform.Delays.Count(delay => delay == 10));
		Assert.Equal(80, platform.Delays[^1]);
		Assert.Equal(201, platform.Delays.Count);
		#if DEBUG
		Assert.Contains(logCapture.Events, logEvent => logEvent.RenderMessage() == "_waitForPhysicalRelease: timed out after 2s, keys still held");
		#else
		Assert.Empty(logCapture.Events);
		#endif
		Assert.Equal(2, platform.SendInputCalls.Count);
	}

	[Fact]
	public void InjectText_ReleasesAllEightModifiersWithExpectedKeyUpFlags() {
		using var logCapture = new LogCapture();
		var platform = new FakePlatform();
		var injector = new TestTextInjector(platform, logCapture.Log);

		injector.InjectText("x", Array.Empty<int>());

		var modifierEvents = platform.SendInputCalls[0].Inputs;
		Assert.Equal(new ushort[] { 0x5B, 0x5C, 0xA2, 0xA3, 0xA4, 0xA5, 0xA0, 0xA1 }, modifierEvents.Select(input => input.VirtualKey));
		Assert.Equal(new uint[] { 3, 3, 2, 2, 2, 2, 2, 2 }, modifierEvents.Select(input => input.Flags));
		Assert.All(modifierEvents, input => {
			Assert.Equal(1u, input.Type);
			Assert.Equal(0, input.ScanCode);
			Assert.Equal(0u, input.Time);
			Assert.Equal(platform.ExtraInfo, input.ExtraInfo);
		});
	}

	[Fact]
	public void InjectText_SendsUnicodeCharactersAsOrderedKeyDownAndKeyUpEvents() {
		using var logCapture = new LogCapture();
		var platform = new FakePlatform();
		var injector = new TestTextInjector(platform, logCapture.Log);

		injector.InjectText("Aé", Array.Empty<int>());

		var unicodeEvents = platform.SendInputCalls[1].Inputs;
		Assert.Equal(new ushort[] { 'A', 'A', 'é', 'é' }, unicodeEvents.Select(input => input.ScanCode));
		Assert.Equal(new uint[] { 4, 6, 4, 6 }, unicodeEvents.Select(input => input.Flags));
		Assert.All(unicodeEvents, input => {
			Assert.Equal(1u, input.Type);
			Assert.Equal(0, input.VirtualKey);
			Assert.Equal(0u, input.Time);
			Assert.Equal(platform.ExtraInfo, input.ExtraInfo);
		});
	}

	[Theory]
	[InlineData(2u, true)]
	[InlineData(1u, false)]
	public void InjectText_ReportsFullAndPartialSendInputResults(uint reportedTextEvents, bool isComplete) {
		using var logCapture = new LogCapture();
		var platform = new FakePlatform();
		platform.SendInputResults.Enqueue(8);
		platform.SendInputResults.Enqueue(reportedTextEvents);
		var injector = new TestTextInjector(platform, logCapture.Log);

		injector.InjectText("x", Array.Empty<int>());

		#if DEBUG
		Assert.Single(logCapture.Events);
		if (isComplete) {
			Assert.Equal(LogEventLevel.Information, logCapture.Events[0].Level);
			Assert.Equal("SendInput: OK, 2 events for 1 chars", logCapture.Events[0].RenderMessage());
			Assert.Equal(0, platform.GetLastErrorCallCount);
		} else {
			Assert.Equal(LogEventLevel.Warning, logCapture.Events[0].Level);
			Assert.Equal("SendInput: sent 1/2 events. LastError=123", logCapture.Events[0].RenderMessage());
			Assert.Equal(1, platform.GetLastErrorCallCount);
		}
		#else
		Assert.Empty(logCapture.Events);
		Assert.Equal(isComplete ? 0 : 1, platform.GetLastErrorCallCount);
		#endif
	}

	[Fact]
	public void InputStructs_MatchTheWin32X64Layout() {
		if (IntPtr.Size != 8)
			return;

		Assembly assembly = typeof(TextInjector).Assembly;
		Type input = assembly.GetType("WhisperWriter.Services.TextInjectors.Input")!;
		Type keyboard = assembly.GetType("WhisperWriter.Services.TextInjectors.InputKeyboard")!;
		Type mouse = assembly.GetType("WhisperWriter.Services.TextInjectors.InputMouse")!;

		Assert.Equal(40, Marshal.SizeOf(input));
		Assert.Equal(0, Marshal.OffsetOf(input, "type").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf(input, "ki").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf(input, "mi").ToInt32());
		Assert.Equal(24, Marshal.SizeOf(keyboard));
		Assert.Equal(0, Marshal.OffsetOf(keyboard, "wVk").ToInt32());
		Assert.Equal(2, Marshal.OffsetOf(keyboard, "wScan").ToInt32());
		Assert.Equal(4, Marshal.OffsetOf(keyboard, "dwFlags").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf(keyboard, "time").ToInt32());
		Assert.Equal(16, Marshal.OffsetOf(keyboard, "dwExtraInfo").ToInt32());
		Assert.Equal(32, Marshal.SizeOf(mouse));
		Assert.Equal(0, Marshal.OffsetOf(mouse, "dx").ToInt32());
		Assert.Equal(4, Marshal.OffsetOf(mouse, "dy").ToInt32());
		Assert.Equal(8, Marshal.OffsetOf(mouse, "mouseData").ToInt32());
		Assert.Equal(12, Marshal.OffsetOf(mouse, "dwFlags").ToInt32());
		Assert.Equal(16, Marshal.OffsetOf(mouse, "time").ToInt32());
		Assert.Equal(24, Marshal.OffsetOf(mouse, "dwExtraInfo").ToInt32());
	}

	private class TestTextInjector : TextInjector {
		public TestTextInjector (ITextInjectorPlatform platform, Log? log = null) : base(platform) {
			if (log is not null)
				this.log = log;
		}
	}

	private class FakePlatform : ITextInjectorPlatform {
		public IntPtr ForegroundWindow { get; set; } = new IntPtr(0x1234);
		public IntPtr FocusedWindow { get; set; }
		public uint TargetThreadId { get; set; } = 34;
		public uint CurrentThreadId { get; set; } = 12;
		public bool AttachResult { get; set; } = true;
		public IntPtr ExtraInfo { get; set; } = new IntPtr(0x4321);
		public int InputSize { get; set; } = IntPtr.Size == 8 ? 40 : 28;
		public Func<int, short> KeyStateReader { get; set; } = _ => 0;
		public List<string> Calls { get; } = new();
		public List<int> Delays { get; } = new();
		public List<(IntPtr Foreground, IntPtr Focus)> RestoredFocusTargets { get; } = new();
		public Queue<uint> SendInputResults { get; } = new();
		public List<(uint Count, ITextInjectorPlatform.InputEvent[] Inputs, int InputSize)> SendInputCalls { get; } = new();
		public int GetLastErrorCallCount { get; private set; }

		private IntPtr _currentForegroundWindow;

		public IntPtr GetForegroundWindow () {
			this.Calls.Add("GetForegroundWindow");
			return this.ForegroundWindow;
		}

		public IntPtr GetFocusedWindow (IntPtr foregroundWindow) {
			this.Calls.Add($"GetFocusedWindow:{foregroundWindow.ToInt64():X}");
			return this.FocusedWindow;
		}

		public void SetFocus (IntPtr hWnd) {
			this.Calls.Add($"SetFocus:{hWnd.ToInt64():X}");
			this.RestoredFocusTargets.Add((this._currentForegroundWindow, hWnd));
		}

		public bool SetForegroundWindow (IntPtr hWnd) {
			this.Calls.Add($"SetForegroundWindow:{hWnd.ToInt64():X}");
			this._currentForegroundWindow = hWnd;
			return true;
		}

		public bool BringWindowToTop (IntPtr hWnd) {
			this.Calls.Add($"BringWindowToTop:{hWnd.ToInt64():X}");
			return true;
		}

		public bool AttachThreadInput (uint idAttach, uint idAttachTo, bool attach) {
			this.Calls.Add($"AttachThreadInput:{idAttach}:{idAttachTo}:{attach}");
			return this.AttachResult;
		}

		public uint GetWindowThreadProcessId (IntPtr hWnd) {
			this.Calls.Add($"GetWindowThreadProcessId:{hWnd.ToInt64():X}");
			return this.TargetThreadId;
		}

		public uint GetCurrentThreadId () {
			this.Calls.Add("GetCurrentThreadId");
			return this.CurrentThreadId;
		}

		public uint SendInput (uint inputCount, IReadOnlyList<ITextInjectorPlatform.InputEvent> inputs, int inputSize) {
			this.Calls.Add($"SendInput:{inputCount}");
			this.SendInputCalls.Add((inputCount, inputs.ToArray(), inputSize));
			return this.SendInputResults.Count > 0 ? this.SendInputResults.Dequeue() : inputCount;
		}

		public IntPtr GetMessageExtraInfo () {
			return this.ExtraInfo;
		}

		public short GetAsyncKeyState (int virtualKey) {
			this.Calls.Add($"GetAsyncKeyState:{virtualKey:X2}");
			return this.KeyStateReader(virtualKey);
		}

		public void Delay (int milliseconds) {
			this.Calls.Add($"Delay:{milliseconds}");
			this.Delays.Add(milliseconds);
		}

		public int GetLastError () {
			this.GetLastErrorCallCount++;
			return 123;
		}
	}

	private class LogCapture : IDisposable {
		private readonly Serilog.ILogger _previousLogger;
		private readonly Log _log;

		public Log Log => this._log;
		public List<LogEvent> Events { get; }

		public LogCapture () {
			this._previousLogger = Serilog.Log.Logger;
			this._log = new Log();
			var sink = new InMemorySink();
			this.Events = sink.Events;
			Serilog.Log.Logger = new Serilog.LoggerConfiguration()
				.MinimumLevel.Verbose()
				.WriteTo.Sink(sink)
				.CreateLogger();
		}

		public void Dispose () {
			this._log.CloseAndFlush();
			Serilog.Log.Logger = this._previousLogger;
		}
	}

	private class InMemorySink : ILogEventSink {
		public List<LogEvent> Events { get; } = new();

		public void Emit (LogEvent logEvent) {
			this.Events.Add(logEvent);
		}
	}
}