using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WhisperWriter.Tests.TestSupport;
using Xunit;

namespace WhisperWriter.Tests;

public class AppLifecycleTests {
	private static void invoke (App app, string methodName, params object?[] arguments) =>
		typeof(App).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(app, arguments);

	[Fact]
	public async Task Startup_LoadsSettingsShowsTheWindowAndInitializesWhisperInOrder () {
		var app = WpfTestHost.Application;
		var runtime = new FakeAppRuntime { ModelPath = "llms/phase0-model.bin", HistorySize = 23 };
		var originalPath = Environment.GetEnvironmentVariable("PATH");
		string isolatedPath = "C:\\phase0-path";
		WpfTestHost.Invoke(() => {
			try {
				Environment.SetEnvironmentVariable("PATH", isolatedPath);
				WpfTestHost.SetRuntime(runtime);
				AppLifecycleTests.invoke(app, "OnStartup", (object?)null);
				string cudaRuntime = Path.Combine(AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");
				Assert.Equal(cudaRuntime + ";" + isolatedPath, Environment.GetEnvironmentVariable("PATH"));
			} finally {
				Environment.SetEnvironmentVariable("PATH", originalPath);
			}
		});
		await runtime.WhisperInitializationCompleted.Task.WaitAsync(
			TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Equal(new[] {
			"appdomain-handler-register",
			"settings-load",
			"history-size:23",
			"resolve-main-window",
			"show-main-window",
			$"whisper-initialize:{Path.Combine(AppContext.BaseDirectory, "llms/phase0-model.bin")}",
		}, runtime.Effects);
	}

	[Fact]
	public async Task Startup_RunsWhisperInitializationOffDispatcherAndKeepsTheUiResponsive () {
		var runtime = new FakeAppRuntime();
		using var gate = new ManualResetEventSlim();
		runtime.InitializationGate = gate;
		int uiThreadId = WpfTestHost.ThreadId;
		string? originalPath = Environment.GetEnvironmentVariable("PATH");
		Task startup = Task.Run(() => WpfTestHost.Invoke(() => {
			Environment.SetEnvironmentVariable("PATH", "C:\\phase2-startup-path");
			WpfTestHost.SetRuntime(runtime);
			AppLifecycleTests.invoke(WpfTestHost.Application, "OnStartup", (object?)null);
		}), TestContext.Current.CancellationToken);

		try {
			CancellationToken cancellationToken = TestContext.Current.CancellationToken;
			int initializationThreadId = await runtime.WhisperInitializationStarted.Task
				.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
			Assert.NotEqual(uiThreadId, initializationThreadId);

			Task<bool> dispatcherProbe = Task.Run(() => WpfTestHost.Invoke(() => true));
			Task completed = await Task.WhenAny(dispatcherProbe, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
			Assert.Same(dispatcherProbe, completed);
			Assert.True(await dispatcherProbe.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
		} finally {
			gate.Set();
			await startup.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
			await runtime.WhisperInitializationCompleted.Task.WaitAsync(
				TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
			WpfTestHost.Invoke(() => Environment.SetEnvironmentVariable("PATH", originalPath));
		}
	}

	[Fact]
	public void ExitApp_DisposesTrayBeforeClosingTheWindowAndShuttingDown () {
		var runtime = new FakeAppRuntime();
		WpfTestHost.Invoke(() => {
			WpfTestHost.SetRuntime(runtime);
			WpfTestHost.Application.ExitApp();
		});

		Assert.Equal(new[] { "tray-exit", "main-window-close", "shutdown" }, runtime.Effects);
	}

	[Fact]
	public void OnExit_DisposesEtaBeforeClosingTheLog () {
		var runtime = new FakeAppRuntime();
		WpfTestHost.Invoke(() => {
			WpfTestHost.SetRuntime(runtime);
			AppLifecycleTests.invoke(WpfTestHost.Application, "OnExit", (object?)null);
		});

		Assert.Equal(new[] { "eta-dispose", "log-close" }, runtime.Effects);
	}

	[Fact]
	public void DispatcherUnhandledException_IsLoggedAndMarkedHandled () {
		var runtime = new FakeAppRuntime();
		using var logged = new ManualResetEventSlim();
		using var handledEvent = new ManualResetEventSlim();
		var exception = new InvalidOperationException("dispatcher failure");
		bool handled = false;
		runtime.OnLogError = (_, _) => logged.Set();
		DispatcherUnhandledExceptionEventHandler captureHandled = (_, args) => {
			handled = args.Handled;
			handledEvent.Set();
		};
		WpfTestHost.Invoke(() => {
			WpfTestHost.SetRuntime(runtime);
			App app = WpfTestHost.Application;
			AppLifecycleTests.invoke(app, "initGlobalErrorHandlers");
			app.DispatcherUnhandledException += captureHandled;
			app.Dispatcher.BeginInvoke(new Action(() => throw exception));
		});

		try {
			CancellationToken cancellationToken = TestContext.Current.CancellationToken;
			Assert.True(logged.Wait(TimeSpan.FromSeconds(5), cancellationToken), "Dispatcher exception handler did not log the exception.");
			Assert.True(handledEvent.Wait(TimeSpan.FromSeconds(5), cancellationToken), "Dispatcher exception event was not raised.");
			Assert.True(handled);
			Assert.Contains(runtime.Errors, item => item.Message == "Unhandled dispatcher exception" && ReferenceEquals(item.Exception, exception));
		} finally {
			WpfTestHost.Invoke(() => WpfTestHost.Application.DispatcherUnhandledException -= captureHandled);
		}
	}

	[Fact]
	public void GlobalErrorHandlerInitialization_RegistersAppDomainHandlerThroughRuntime () {
		var runtime = new FakeAppRuntime();
		WpfTestHost.Invoke(() => {
			WpfTestHost.SetRuntime(runtime);
			AppLifecycleTests.invoke(WpfTestHost.Application, "initGlobalErrorHandlers");
		});

		UnhandledExceptionEventHandler handler = Assert.IsType<UnhandledExceptionEventHandler>(
			runtime.AppDomainUnhandledExceptionHandler
		);
		Assert.Equal("handleAppDomainUnhandledException", handler.Method.Name);
		Assert.Same(WpfTestHost.Application, handler.Target);
		Assert.Equal(new[] { "appdomain-handler-register" }, runtime.Effects);
	}

	[Fact]
	public void AppDomainUnhandledException_LogsOnlyExceptionObjects () {
		var runtime = new FakeAppRuntime();
		WpfTestHost.Invoke(() => {
			WpfTestHost.SetRuntime(runtime);
			AppLifecycleTests.invoke(WpfTestHost.Application, "handleAppDomainUnhandledException", null,
				new UnhandledExceptionEventArgs("non-exception", false));
			AppLifecycleTests.invoke(WpfTestHost.Application, "handleAppDomainUnhandledException", null,
				new UnhandledExceptionEventArgs(new InvalidOperationException("background failure"), true));
		});

		var error = Assert.Single(runtime.Errors);
		Assert.Equal("Unhandled background exception", error.Message);
		Assert.Equal("background failure", error.Exception.Message);
	}

	[Fact]
	public void GraphicsPathInitialization_PrependsTheRuntimeFolderOnlyOnce () {
		var originalPath = Environment.GetEnvironmentVariable("PATH");
		string isolatedPath = "C:\\phase0-path";
		WpfTestHost.Invoke(() => {
			try {
				Environment.SetEnvironmentVariable("PATH", isolatedPath);
				AppLifecycleTests.invoke(WpfTestHost.Application, "initSystemPath4GfxLibs");
				string cudaRuntime = Path.Combine(AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");
				string expected = cudaRuntime + ";" + isolatedPath;
				Assert.Equal(expected, Environment.GetEnvironmentVariable("PATH"));
				AppLifecycleTests.invoke(WpfTestHost.Application, "initSystemPath4GfxLibs");
				Assert.Equal(expected, Environment.GetEnvironmentVariable("PATH"));
			} finally {
				Environment.SetEnvironmentVariable("PATH", originalPath);
			}
		});
	}
}