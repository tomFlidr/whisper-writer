using Autofac;
using System.Windows;
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

	protected override async void OnStartup (StartupEventArgs e) {
		base.OnStartup(e);

		this.initSystemPath4GfxLibs();
		this.initGlobalErrorHandlers();

		this.settings.Load();

		this.transcriptionHistory.MaxSize = this.settings.Data.HistorySize;
		
		// Create the main window as floating widget
		this.MainWindow = Program.DI.Provider.Resolve<MainWindow>();
		this.MainWindow.Show();

		// Initialize Whisper in background
		var modelPath = System.IO.Path.Combine(
			AppContext.BaseDirectory,
			this.settings.Data.ModelPath
		);
		await this.whisper.InitializeAsync(modelPath);
	}
	
	public void ExitApp () {
		this.trayMenu?.HandleAppExit();
		this.MainWindow?.ForceClose();
		this.Shutdown();
	}

	protected override void OnExit (ExitEventArgs e) {
		//this.trayIcon?.Dispose();
		this.etaCalc.Dispose();
		this.log.CloseAndFlush();
		base.OnExit(e);
	}
	
	// Add the CUDA runtime folder (next to ggml-cuda-whisper.dll) to the process PATH
	// so the OS loader finds cudart64_13.dll / cublas64_13.dll without requiring
	// a system-wide CUDA installation or changes to the user's environment.
	protected void initSystemPath4GfxLibs () {
		var cudaRuntimeDir = System.IO.Path.Combine(
			AppContext.BaseDirectory, "runtimes", "cuda", "win-x64"
		);
		var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!currentPath.Contains(cudaRuntimeDir, StringComparison.OrdinalIgnoreCase))
			Environment.SetEnvironmentVariable("PATH", cudaRuntimeDir + ";" + currentPath);
	}

	protected void initGlobalErrorHandlers () {
		// Catch any unhandled WPF dispatcher exceptions
		this.DispatcherUnhandledException += (_, args) => {
			this.log.Error("Unhandled dispatcher exception", args.Exception);
			args.Handled = true;
		};
		// Catch unhandled exceptions from non-UI threads
		AppDomain.CurrentDomain.UnhandledException += (_, args) => {
			if (args.ExceptionObject is Exception ex)
				this.log.Error("Unhandled background exception", ex);
		};
	}
}
