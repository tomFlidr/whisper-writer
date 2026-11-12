using WhisperWriter.Views.Screens;

namespace WhisperWriter.Apps;

public interface IAppRuntime {
	string ModelPath { get; }
	int HistorySize { get; }

	void LoadSettings ();
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