using WhisperWriter.Views;

namespace WhisperWriter;

public interface IAppRuntime {
	string ModelPath { get; }
	void LoadSettings ();
	int HistorySize { get; }
	void SetHistorySize (int historySize);
	MainWindow? ResolveMainWindow ();
	void ShowMainWindow (MainWindow? window);
	Task InitializeWhisperAsync (string modelPath);
	void HandleTrayExit ();
	void ForceCloseMainWindow (MainWindow? window);
	void Shutdown ();
	void DisposeEta ();
	void CloseLog ();
	void RegisterAppDomainUnhandledExceptionHandler (UnhandledExceptionEventHandler handler);
	void LogError (string message, Exception exception);
}