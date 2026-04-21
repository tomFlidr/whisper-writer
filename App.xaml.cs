using Autofac;
using Serilog;
using System.Windows;
using System.Windows.Forms;
using WhisperWriter.DI;
using WhisperWriter.Services;
using WhisperWriter.Utils;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter;

public partial class App : System.Windows.Application, IService, ISingleton {
	[Inject]
	protected SettingsService settingsService { get; set; } = null!;
	[Inject]
	protected TranscriptionHistory historyService { get; set; } = null!;
	[Inject]
	protected WhisperService whisperService { get; set; } = null!;
	[Inject]
	protected EtaService etaService { get; set; } = null!;
	[Inject]
	protected LogService logService { get; set; } = null!;

	protected NotifyIcon? trayIcon;
	protected MainWindow? mainWindow;
	protected static Window? secondaryWindow;

	protected override async void OnStartup (StartupEventArgs e) {
		base.OnStartup(e);

		// Add the CUDA runtime folder (next to ggml-cuda-whisper.dll) to the process PATH
		// so the OS loader finds cudart64_13.dll / cublas64_13.dll without requiring
		// a system-wide CUDA installation or changes to the user's environment.
		var cudaRuntimeDir = System.IO.Path.Combine(
			AppContext.BaseDirectory, "runtimes", "cuda", "win-x64"
		);
		var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!currentPath.Contains(cudaRuntimeDir, StringComparison.OrdinalIgnoreCase))
			Environment.SetEnvironmentVariable("PATH", cudaRuntimeDir + ";" + currentPath);

		// Catch any unhandled WPF dispatcher exceptions
		this.DispatcherUnhandledException += (_, args) => {
			this.logService.Error("Unhandled dispatcher exception", args.Exception);
			args.Handled = true;
		};

		// Catch unhandled exceptions from non-UI threads
		AppDomain.CurrentDomain.UnhandledException += (_, args) => {
			if (args.ExceptionObject is Exception ex)
				this.logService.Error("Unhandled background exception", ex);
		};

		this.settingsService.Load();
		this.historyService.MaxSize = this.settingsService.Settings.HistorySize;
		
		// Create the floating widget
		this.mainWindow = Program.DI.Provider.Resolve<MainWindow>();
		this.mainWindow.Show();

		// Tray icon – right-click menu only
		var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
		var trayIcon = System.IO.File.Exists(icoPath)
			? new System.Drawing.Icon(icoPath)
			: SystemIcons.Application;
		this.trayIcon = new NotifyIcon {
			Icon = trayIcon,
			Text = "WhisperWriter",
			Visible = true,
		};

		var menu = new ContextMenuStrip();
		menu.Items.Add("About WhisperWriter", null, (_, _) => App.showAbout());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Transcriptions", null, (_, _) => App.showHistory());
		menu.Items.Add("Settings", null, (_, _) => App.showSettings());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) => this.exitApp());

		this.trayIcon.ContextMenuStrip = menu;

		// Double-click shows / restores the widget
		this.trayIcon.DoubleClick += (_, _) => {
			this.mainWindow?.Show();
			this.mainWindow?.Activate();
		};

		// Initialize Whisper in background
		var modelPath = System.IO.Path.Combine(AppContext.BaseDirectory,
			this.settingsService.Settings.ModelPath);
		_ = this.whisperService.InitializeAsync(modelPath);

		await Task.Run(() => Thread.Sleep(0));
	}

	protected static void closeSecondaryWindow () {
		App.secondaryWindow?.Close();
		App.secondaryWindow = null;
	}

	protected static void showAbout () {
		App.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<AboutWindow>();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		win.Show();
	}

	protected static void showHistory () {
		App.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<HistoryWindow>();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		win.Show();
	}

	protected static void showSettings () {
		App.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<SettingsWindow>();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		if (win.ShowDialog() == true) {
			Program.App.settingsService.Save();
			Program.App.historyService.MaxSize = Program.App.settingsService.Settings.HistorySize;
			// Apply new hotkey combination immediately, without restarting.
			(Current as App)?.mainWindow?.ReloadHotkey();
		}
		App.secondaryWindow = null;
	}

	protected void exitApp () {
		this.trayIcon?.Dispose();
		this.mainWindow?.ForceClose();
		this.Shutdown();
	}

	protected override void OnExit (ExitEventArgs e) {
		this.trayIcon?.Dispose();
		this.etaService.Dispose();
		this.logService.CloseAndFlush();
		base.OnExit(e);
	}
}
