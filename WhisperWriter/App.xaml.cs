using Autofac;
using System.Windows;
using System.Windows.Threading;
using WhisperWriter.Apps;
using WhisperWriter.DI;
using WhisperWriter.Services;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views.Screens;

namespace WhisperWriter;

public partial class App : System.Windows.Application, IService, ISingleton {

	public new MainWindow? MainWindow { get; protected set; } = null!;

	[Inject]
	protected internal Settings settings { get; set; } = null!;
	[Inject]
	protected internal TranscriptionHistory transcriptionHistory { get; set; } = null!;
	[Inject]
	protected internal Services.Whisper whisper { get; set; } = null!;
	[Inject]
	protected internal EtaCalc etaCalc { get; set; } = null!;
	[Inject]
	protected internal Log log { get; set; } = null!;
	[Inject]
	protected internal TrayMenu trayMenu { get; set; } = null!;
	protected IAppRuntime? runtime { get; set; }

	/// <summary>
	/// Requests application exit. Disposes tray resources, closes the main window and shuts down.
	/// </summary>
	public void ExitApp () {
		var runtime = this.getRuntime();
		runtime.HandleTrayExit();
		runtime.ForceCloseMainWindow(this.MainWindow);
		runtime.Shutdown();
	}

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
		return this.runtime ??= new Runtime(this);
	}


}