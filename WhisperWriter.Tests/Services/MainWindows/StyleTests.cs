using System.Windows;
using System.Windows.Interop;
using WhisperWriter.Services.MainWindows;
using WhisperWriter.Tests.TestSupport;
using Xunit;

namespace WhisperWriter.Tests.Services.MainWindows;

public class StyleTests {
	[Fact]
	public void SetWindow_ReturnsTheSameStyleInstance () {
		WpfTestHost.Invoke(() => {
			var window = new Window();
			var style = new TestStyle(new FakeWindowStyleNativeApi());

			Assert.Same(style, style.SetWindow(window));
		});
	}

	[Fact]
	public void InitializeSystemEvents_AddsToolWindowStyleAndClearsAppWindowStyle () {
		WpfTestHost.Invoke(() => {
			var window = new Window();
			var api = new FakeWindowStyleNativeApi { InitialStyle = 0x00040004 };
			var style = new TestStyle(api).SetWindow(window);

			style.InitializeSystemEvents(window, EventArgs.Empty);

			Assert.Same(window, api.Window);
			Assert.Equal(api.Handle, api.ReadHandle);
			Assert.Equal(api.Handle, api.WrittenHandle);
			Assert.Equal(0x00000084, api.WrittenStyle);
			Assert.NotNull(api.Hook);
		});
	}

	[Fact]
	public void NativeHook_ForwardsDisplayChangeWithoutMarkingTheMessageHandled () {
		WpfTestHost.Invoke(() => {
			var api = new FakeWindowStyleNativeApi();
			var style = new TestStyle(api).SetWindow(new Window());
			int eventCount = 0;
			object? eventSender = null;
			EventArgs? eventArgs = null;
			style.WindowDisplayChange += (sender, args) => {
				eventCount++;
				eventSender = sender;
				eventArgs = args;
			};
			style.InitializeSystemEvents(style, EventArgs.Empty);
			Assert.NotNull(api.Hook);
			bool handled = false;

			IntPtr otherResult = api.Hook!(api.Handle, 0x000F, IntPtr.Zero, IntPtr.Zero, ref handled);
			Assert.Equal(IntPtr.Zero, otherResult);
			Assert.Equal(0, eventCount);
			Assert.False(handled);

			IntPtr displayResult = api.Hook(api.Handle, 0x007E, IntPtr.Zero, IntPtr.Zero, ref handled);

			Assert.Equal(IntPtr.Zero, displayResult);
			Assert.Equal(1, eventCount);
			Assert.Same(style, eventSender);
			Assert.Same(EventArgs.Empty, eventArgs);
			Assert.False(handled);
		});
	}

	private class TestStyle : WhisperWriter.Services.MainWindows.Style {
		public TestStyle (IWindowStyleNativeApi api) : base(api) {
		}
	}

	private class FakeWindowStyleNativeApi : IWindowStyleNativeApi {
		public IntPtr Handle { get; } = new(1234);
		public int InitialStyle { get; init; }
		public Window? Window { get; private set; }
		public IntPtr ReadHandle { get; private set; }
		public IntPtr WrittenHandle { get; private set; }
		public int WrittenStyle { get; private set; }
		public HwndSourceHook? Hook { get; private set; }

		public IntPtr GetWindowHandle (Window window) {
			this.Window = window;
			return this.Handle;
		}

		public int GetExtendedWindowStyle (IntPtr hwnd) {
			this.ReadHandle = hwnd;
			return this.InitialStyle;
		}

		public void SetExtendedWindowStyle (IntPtr hwnd, int style) {
			this.WrittenHandle = hwnd;
			this.WrittenStyle = style;
		}

		public void AddHook (IntPtr hwnd, HwndSourceHook hook) {
			this.Hook = hook;
		}
	}
}