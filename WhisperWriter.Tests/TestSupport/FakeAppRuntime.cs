using WhisperWriter.Views;

namespace WhisperWriter.Tests.TestSupport;

internal class FakeAppRuntime : WhisperWriter.IAppRuntime {
	public string ModelPath { get; set; } = "llms/test-model.bin";
	public int HistorySize { get; set; } = 17;
	public MainWindow? Window { get; set; }
	public List<string> Effects { get; } = [];
	public List<(string Message, Exception Exception)> Errors { get; } = [];
	public ManualResetEventSlim? InitializationGate { get; set; }
	public TaskCompletionSource<int> WhisperInitializationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public TaskCompletionSource<bool> WhisperInitializationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public Action<string, Exception>? OnLogError { get; set; }
	public UnhandledExceptionEventHandler? AppDomainUnhandledExceptionHandler { get; private set; }

	public void LoadSettings () => this.Effects.Add("settings-load");
	public void SetHistorySize (int historySize) => this.Effects.Add($"history-size:{historySize}");
	public MainWindow? ResolveMainWindow () {
		this.Effects.Add("resolve-main-window");
		return this.Window;
	}
	public void ShowMainWindow (MainWindow? window) => this.Effects.Add("show-main-window");
	public Task InitializeWhisperAsync (string modelPath) {
		this.Effects.Add($"whisper-initialize:{modelPath}");
		this.WhisperInitializationStarted.TrySetResult(Environment.CurrentManagedThreadId);
		this.InitializationGate?.Wait();
		this.WhisperInitializationCompleted.TrySetResult(true);
		return Task.CompletedTask;
	}
	public void HandleTrayExit () => this.Effects.Add("tray-exit");
	public void ForceCloseMainWindow (MainWindow? window) => this.Effects.Add("main-window-close");
	public void Shutdown () => this.Effects.Add("shutdown");
	public void DisposeEta () => this.Effects.Add("eta-dispose");
	public void CloseLog () => this.Effects.Add("log-close");
	public void RegisterAppDomainUnhandledExceptionHandler (UnhandledExceptionEventHandler handler) {
		this.AppDomainUnhandledExceptionHandler = handler;
		this.Effects.Add("appdomain-handler-register");
	}
	public void LogError (string message, Exception exception) {
		this.Errors.Add((message, exception));
		this.OnLogError?.Invoke(message, exception);
	}
}