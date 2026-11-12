using System.Reflection;
using System.Windows;
using WhisperWriter.DI;
using WhisperWriter.Programs;
using WhisperWriter.Tests.TestSupport;
using Xunit;

namespace WhisperWriter.Tests;

public class ProgramStartupTests {
	[Fact]
	public void Main_UsesTheStartupRuntimeAndRunsTheContainerAppInTheRequiredOrder () {
		var app = WpfTestHost.Application;
		var runtime = new RecordingProgramRuntime(app);
		WhisperWriter.DI.Container? previousContainer = Program.DI;
		App? previousApp = Program.App;
		ShutdownMode previousShutdownMode = WpfTestHost.Invoke(() => app.ShutdownMode);
		var factory = typeof(Program).GetProperty("RuntimeFactory", BindingFlags.Static | BindingFlags.NonPublic)!;
		object previousFactory = factory.GetValue(null)!;
		try {
			factory.SetValue(null, (Func<IProgramRuntime>)(() => runtime));
			WpfTestHost.Invoke(() => Program.Main(["phase0", "test"]));

			Assert.Equal(new[] { "container-create", "app-resolve", "component-initialize", "app-run" }, runtime.Effects);
			Assert.NotNull(runtime.Container);
			Assert.Same(runtime.Container, Program.DI);
			Assert.Same(app, Program.App);
			Assert.Same(runtime.Container, runtime.ResolvedContainer);
			Assert.Equal(ShutdownMode.OnExplicitShutdown, WpfTestHost.Invoke(() => app.ShutdownMode));
			Assert.NotNull(typeof(Program).GetMethod(nameof(Program.Main))!.GetCustomAttribute<STAThreadAttribute>());
		} finally {
			factory.SetValue(null, previousFactory);
			Program.App = previousApp!;
			typeof(Program).GetProperty(nameof(Program.DI), BindingFlags.Static | BindingFlags.Public)!
				.SetValue(null, previousContainer);
			WpfTestHost.Invoke(() => app.ShutdownMode = previousShutdownMode);
			runtime.Container?.Provider.Dispose();
		}
	}

	[Fact]
	public void StartApplication_RejectsANullRuntimeBeforeChangingGlobalApplicationState () {
		Assert.Throws<ArgumentNullException>(() => Program.StartApplication([], null!));
	}

	private class RecordingProgramRuntime : IProgramRuntime {
		public List<string> Effects { get; } = [];
		public WhisperWriter.DI.Container? Container { get; private set; }
		public WhisperWriter.DI.Container? ResolvedContainer { get; private set; }

		private readonly App _app;

		public RecordingProgramRuntime (App app) => this._app = app;

		public WhisperWriter.DI.Container CreateContainer () {
			this.Effects.Add("container-create");
			return this.Container = new WhisperWriter.DI.Container();
		}

		public App ResolveApp (WhisperWriter.DI.Container container) {
			this.Effects.Add("app-resolve");
			this.ResolvedContainer = container;
			return this._app;
		}

		public void InitializeComponent (App app) {
			Assert.Same(this._app, app);
			this.Effects.Add("component-initialize");
		}

		public void Run (App app) {
			Assert.Same(this._app, app);
			this.Effects.Add("app-run");
		}
	}
}