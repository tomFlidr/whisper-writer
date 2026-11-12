using Autofac;
using WhisperWriter;
using WhisperWriter.Services;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Views.Screens;

namespace WhisperWriter.Apps;

/// <summary>Adapts the WPF application lifecycle to its runtime contract.</summary>
internal class Runtime : IAppRuntime {
	public string ModelPath => this._owner.settings.Data.ModelPath;
	public int HistorySize => this._owner.settings.Data.HistorySize;

	private readonly App _owner;

	public Runtime (App owner) {
		this._owner = owner;
	}

	public void LoadSettings () => this._owner.settings.Load();
	public void SetHistorySize (int historySize) => this._owner.transcriptionHistory.MaxSize = historySize;
	public MainWindow? ResolveMainWindow () => Program.DI.Provider.Resolve<MainWindow>();
	public void ShowMainWindow (MainWindow? window) => window?.Show();
	public Task InitializeWhisperAsync (string modelPath) => this._owner.whisper.InitializeAsync(modelPath);
	public void HandleTrayExit () => this._owner.trayMenu?.HandleAppExit();
	public void ForceCloseMainWindow (MainWindow? window) => window?.ForceClose();
	public void Shutdown () => this._owner.Shutdown();
	public void DisposeEta () => this._owner.etaCalc.Dispose();
	public void CloseLog () => this._owner.log.CloseAndFlush();
	public void RegisterAppDomainUnhandledExceptionHandler (UnhandledExceptionEventHandler handler) =>
		AppDomain.CurrentDomain.UnhandledException += handler;
	public void LogError (string message, Exception exception) => this._owner.log.Error(message, exception);
}