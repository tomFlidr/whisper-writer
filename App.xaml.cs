using Serilog;
using System.Windows;
using System.Windows.Forms;
using WhisperWriter.Services;
using WhisperWriter.Utils;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter;

public partial class App : System.Windows.Application, IService, ISingleton {
	
	public static SettingsService SettingsService { get; } = new();
	//public static TranscriptionHistory History { get; } = new();
	public required TranscriptionHistory History { get; set; }
	public static ITranscriptionService WhisperService { get; } = new WhisperService();
	public static EtaService Eta { get; } = new();

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

		LogService.Initialize();

		// Catch any unhandled WPF dispatcher exceptions
		this.DispatcherUnhandledException += (_, args) => {
			LogService.Error("Unhandled dispatcher exception", args.Exception);
			args.Handled = true;
		};

		// Catch unhandled exceptions from non-UI threads
		AppDomain.CurrentDomain.UnhandledException += (_, args) => {
			if (args.ExceptionObject is Exception ex)
				LogService.Error("Unhandled background exception", ex);
		};

		App.SettingsService.Load();
		this.History.MaxSize = App.SettingsService.Settings.HistorySize;
		App.Eta.Initialize();

		// Create the floating widget
		this.mainWindow = new MainWindow();
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
			App.SettingsService.Settings.ModelPath);
		_ = App.WhisperService.InitializeAsync(modelPath);

		await Task.Run(() => Thread.Sleep(0));
	}

	protected static void closeSecondaryWindow () {
		App.secondaryWindow?.Close();
		App.secondaryWindow = null;
	}

	protected static void showAbout () {
		App.closeSecondaryWindow();
		var win = new AboutWindow();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		win.Show();
	}

	protected static void showHistory () {
		App.closeSecondaryWindow();
		var win = new HistoryWindow();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		win.Show();
	}

	protected static void showSettings () {
		App.closeSecondaryWindow();
		var win = new SettingsWindow();
		App.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (App.secondaryWindow == win) 
				App.secondaryWindow = null;
		};
		if (win.ShowDialog() == true) {
			App.SettingsService.Save();
			Program.App.History.MaxSize = App.SettingsService.Settings.HistorySize;
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
		App.Eta.Dispose();
		LogService.CloseAndFlush();
		base.OnExit(e);
	}
}
