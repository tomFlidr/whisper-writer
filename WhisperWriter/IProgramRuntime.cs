namespace WhisperWriter;

public interface IProgramRuntime {
	DI.Container CreateContainer ();
	App ResolveApp (DI.Container container);
	void InitializeComponent (App app);
	void Run (App app);
}