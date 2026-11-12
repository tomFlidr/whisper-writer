using System.Windows;
using WhisperWriter.Programs;

namespace WhisperWriter;

/// <summary>Program startup point.</summary>
public class Program {
	/// <summary>DI container singleton instance.</summary>
	public static DI.Container DI { get; protected set; } = null!;

	/// <summary>WPF application singleton instance.</summary>
	public static App App { get; set; } = null!;

	internal static Func<IProgramRuntime> RuntimeFactory { get; set; } = () => new Runtime();

	/// <summary>Entry point for the application.</summary>
	[System.STAThreadAttribute()]
	[System.Diagnostics.DebuggerNonUserCodeAttribute()]
	[System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "10.0.6.0")]
	public static void Main (string[] args) {
		Program.StartApplication(args, Program.RuntimeFactory());
	}

	public static void StartApplication (string[] args, IProgramRuntime runtime) {
		ArgumentNullException.ThrowIfNull(runtime);
		Program.DI = runtime.CreateContainer();
		Program.App = runtime.ResolveApp(Program.DI);
		Program.App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		runtime.InitializeComponent(Program.App);
		runtime.Run(Program.App);
	}
}