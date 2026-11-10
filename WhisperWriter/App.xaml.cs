using Autofac;
using System.Windows;
using System.Windows.Threading;
using WhisperWriter.DI;
using WhisperWriter.Services;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter;

public partial class App : System.Windows.Application, IService, ISingleton {

	public new MainWindow? MainWindow { get; protected set; } = null!;

	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected TranscriptionHistory transcriptionHistory { get; set; } = null!;
	[Inject]
	protected Services.Whisper whisper { get; set; } = null!;
	[Inject]
	protected EtaCalc etaCalc { get; set; } = null!;
	[Inject]
	protected Log log { get; set; } = null!;
	[Inject]
	protected TrayMenu trayMenu { get; set; } = null!;
	protected IAppRuntime? runtime { get; set; }

	/// <summary>
	/// Application startup entry. Performs environment initialization, loads settings,
	/// constructs the main window and triggers background model initialization.
	/// </summary>
	protected override async void OnStartup (StartupEventArgs e) {
		base.OnStartup(e);

		this.initSystemPath4GfxLibs();
		this.initGlobalErrorHandlers();

		var runtime = this.getRuntime();
		runtime.LoadSettings();
		runtime.SetHistorySize(runtime.HistorySize);

		// Create the main window as floating widget
		this.MainWindow = runtime.ResolveMainWindow();
		runtime.ShowMainWindow(this.MainWindow);

		// Initialize Whisper in background
		var modelPath = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			runtime.ModelPath
		);
		await Task.Run(() => runtime.InitializeWhisperAsync(modelPath));
	}

	/// <summary>
	/// Requests application exit. Disposes tray resources, closes the main window and shuts down.
	/// </summary>
	public void ExitApp () {
		var runtime = this.getRuntime();
		runtime.HandleTrayExit();
		runtime.ForceCloseMainWindow(this.MainWindow);
		runtime.Shutdown();
	}

	protected override void OnExit (ExitEventArgs e) {
		var runtime = this.getRuntime();
		runtime.DisposeEta();
		runtime.CloseLog();
		base.OnExit(e);
	}

	// Add the CUDA runtime folder (next to ggml-cuda-whisper.dll) to the process PATH
	// so the OS loader finds cudart64_13.dll / cublas64_13.dll without requiring
	// a system-wide CUDA installation or changes to the user's environment.
	/// <summary>
	/// Ensures the local CUDA runtime folder is present in the process PATH so native
	/// CUDA DLLs shipped with the app can be loaded without a system-wide CUDA install.
	/// </summary>
	protected void initSystemPath4GfxLibs () {
		var cudaRuntimeDir = System.IO.Path.Combine(
			AppContext.BaseDirectory, "runtimes", "cuda", "win-x64"
		);
		var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!currentPath.Contains(cudaRuntimeDir, StringComparison.OrdinalIgnoreCase))
			Environment.SetEnvironmentVariable("PATH", cudaRuntimeDir + ";" + currentPath);
	}

	protected void initGlobalErrorHandlers () {
		this.DispatcherUnhandledException += this.handleDispatcherUnhandledException;
		this.getRuntime().RegisterAppDomainUnhandledExceptionHandler(this.handleAppDomainUnhandledException);
	}

	protected void handleDispatcherUnhandledException (object? sender, DispatcherUnhandledExceptionEventArgs args) {
		this.getRuntime().LogError("Unhandled dispatcher exception", args.Exception);
		args.Handled = true;
	}

	protected void handleAppDomainUnhandledException (object? sender, UnhandledExceptionEventArgs args) {
		if (args.ExceptionObject is Exception ex)
			this.getRuntime().LogError("Unhandled background exception", ex);
	}

	private IAppRuntime getRuntime () {
		return this.runtime ??= new AppRuntime(this);
	}

	private class AppRuntime: IAppRuntime {
		private readonly App _owner;

		public AppRuntime (App owner) {
			this._owner = owner;
		}

		public string ModelPath => this._owner.settings.Data.ModelPath;
		public int HistorySize => this._owner.settings.Data.HistorySize;

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
}
