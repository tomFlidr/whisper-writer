using Autofac;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace WhisperWriter {
	/// <summary>
	/// Program startup point.
	/// </summary>
	public class Program {

        /// <summary>
        /// DI container singleton instance.
        /// </summary>
        public static DI.Container DI { get; protected set; } = null!;

        /// <summary>
        /// DI container singleton instance.
        /// </summary>
        public static App App { get; set; } = null!;

        /// <summary>
        /// Entry point for the application, initializes and runs the main window.
        /// </summary>
        /// <param name="args">Command-line arguments for the application.</param>
		[System.STAThreadAttribute()]
		[System.Diagnostics.DebuggerNonUserCodeAttribute()]
		[System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "10.0.6.0")]
		public static void Main (string[] args) {
			Program.StartApplication(args, Program.RuntimeFactory());
		}

		internal static Func<IProgramRuntime> RuntimeFactory { get; set; } = () => new ProgramRuntime();

		public static void StartApplication (string[] args, IProgramRuntime runtime) {
			ArgumentNullException.ThrowIfNull(runtime);
			Program.DI = runtime.CreateContainer();
			Program.App = runtime.ResolveApp(Program.DI);
			Program.App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			runtime.InitializeComponent(Program.App);
			runtime.Run(Program.App);
		}

		private class ProgramRuntime: IProgramRuntime {
			public DI.Container CreateContainer () => WhisperWriter.DI.Container.GetInstance();
			public App ResolveApp (DI.Container container) => container.Provider.Resolve<App>();
			public void InitializeComponent (App app) => app.InitializeComponent();
			public void Run (App app) => app.Run();
		}
	}
}
