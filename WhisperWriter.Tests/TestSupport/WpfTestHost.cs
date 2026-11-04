using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WhisperWriter;

namespace WhisperWriter.Tests.TestSupport;

internal static class WpfTestHost {
	private static readonly Lazy<Host> _host = new(WpfTestHost.Start);

	public static int ThreadId => WpfTestHost._host.Value.ThreadId;
	public static App Application => WpfTestHost._host.Value.Application;

	public static void SetRuntime (IAppRuntime runtime) {
		WpfTestHost.setRuntime(WpfTestHost.Application, runtime);
	}

	public static void Invoke(Action action) {
		WpfTestHost._host.Value.Dispatcher.Invoke(action);
	}

	public static T Invoke<T>(Func<T> action) {
		return WpfTestHost._host.Value.Dispatcher.Invoke(action);
	}

	private static Host Start() {
		using var ready = new ManualResetEventSlim();
		Host? host = null;
		var thread = new Thread(() => {
			var application = new App();
			WpfTestHost.setRuntime(application, new FakeAppRuntime());
			WpfTestHost.addTestResources(application);
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			host = new Host(application, Dispatcher.CurrentDispatcher, Environment.CurrentManagedThreadId);
			ready.Set();
			Dispatcher.Run();
		});
		thread.IsBackground = true;
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		ready.Wait();
		return host!;
	}

	private static void setRuntime (App application, IAppRuntime runtime) {
		typeof(App).GetProperty("runtime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
			.SetValue(application, runtime);
	}

	private static void addTestResources (App application) {
		application.Resources["BgBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
		application.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x8B, 0xFF));
		application.Resources["AccentRecordingBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
		application.Resources["TextPrimaryBrush"] = new SolidColorBrush(Colors.White);
		application.Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
		application.Resources["IconButton"] = new Style(typeof(Button));
	}

	private class Host {
		public Host(App application, Dispatcher dispatcher, int threadId) {
			this.Application = application;
			this.Dispatcher = dispatcher;
			this.ThreadId = threadId;
		}

		public App Application { get; }
		public Dispatcher Dispatcher { get; }
		public int ThreadId { get; }
	}
}
