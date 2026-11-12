using Autofac;
using WhisperWriter;
using WhisperWriter.DI;

namespace WhisperWriter.Programs;

/// <summary>Provides the production startup boundary for the application entry point.</summary>
internal class Runtime : IProgramRuntime {
	public Container CreateContainer () => Container.GetInstance();
	public App ResolveApp (Container container) => container.Provider.Resolve<App>();
	public void InitializeComponent (App app) => app.InitializeComponent();
	public void Run (App app) => app.Run();
}