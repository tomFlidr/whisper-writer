using Autofac;
using System;
using System.Threading.Tasks;
using System.Windows;
using WhisperWriter.Services;

namespace WhisperWriter {
	/// <summary>
	/// Program startup point.
	/// </summary>
	public class Program {

		/// <summary>
		/// DI container singleton instance.
		/// </summary>
		public static DI DI { get; protected set; } = null!;

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
			Program.DI = WhisperWriter.Services.DI.GetInstance();
			Program.App = Program.DI.Container.Resolve<App>();
			Program.App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			Program.App.Exit += (_, __) => {
				// TODO
			};
			Program.App.InitializeComponent();
			Program.App.Run();
		}
	}
}
