namespace WhisperWriter.Services.TrayMenus;

public interface IPlatform {
	int HistorySize { get; }
	IDisposable CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick);
	IWindow ResolveWindow (WindowKind kind);
	void SaveSettings ();
	void SetHistorySize (int historySize);
	void ReloadHotkey ();
	void ExitApp ();
	void ShowMainWindow ();
}