namespace WhisperWriter.Programs;

public interface IProgramRuntime {
	WhisperWriter.DI.Container CreateContainer ();
	WhisperWriter.App ResolveApp (WhisperWriter.DI.Container container);
	void InitializeComponent (WhisperWriter.App app);
	void Run (WhisperWriter.App app);
}