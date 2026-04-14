using System.Windows;
using System.Windows.Forms;
using WhisperWriter.Services;
using WhisperWriter.Util;
using WhisperWriter.Views;

namespace WhisperWriter;

public partial class App : System.Windows.Application {
	public static SettingsService SettingsService { get; } = new();
	public static TranscriptionHistory History { get; } = new();
	public static WhisperService WhisperService { get; } = new();

	private NotifyIcon? _trayIcon;
	private MainWindow? _mainWindow;
	private static Window? _secondaryWindow;

	protected override async void OnStartup (StartupEventArgs e) {
		base.OnStartup(e);

		// Add the CUDA runtime folder (next to ggml-cuda-whisper.dll) to the process PATH
		// so the OS loader finds cudart64_13.dll / cublas64_13.dll without requiring
		// a system-wide CUDA installation or changes to the user's environment.
		var cudaRuntimeDir = System.IO.Path.Combine(
			AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");
		var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		if (!currentPath.Contains(cudaRuntimeDir, StringComparison.OrdinalIgnoreCase))
			Environment.SetEnvironmentVariable("PATH", cudaRuntimeDir + ";" + currentPath);

		LogService.Initialize();

		// Catch any unhandled WPF dispatcher exceptions
		DispatcherUnhandledException += (_, args) => {
			LogService.Error("Unhandled dispatcher exception", args.Exception);
			args.Handled = true;
		};

		// Catch unhandled exceptions from non-UI threads
		AppDomain.CurrentDomain.UnhandledException += (_, args) => {
			if (args.ExceptionObject is Exception ex)
				LogService.Error("Unhandled background exception", ex);
		};

		SettingsService.Load();
		History.MaxSize = SettingsService.Settings.HistorySize;

		// Create the floating widget
		_mainWindow = new MainWindow();
		_mainWindow.Show();

		// Tray icon – right-click menu only
		_trayIcon = new NotifyIcon {
			Icon = SystemIcons.Application,
			Text = "WhisperWriter",
			Visible = true,
		};

		var menu = new ContextMenuStrip();
		menu.Items.Add("About WhisperWriter", null, (_, _) => ShowAbout());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Transcriptions", null, (_, _) => ShowHistory());
		menu.Items.Add("Settings", null, (_, _) => ShowSettings());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) => ExitApp());

		_trayIcon.ContextMenuStrip = menu;

		// Double-click shows / restores the widget
		_trayIcon.DoubleClick += (_, _) => {
			_mainWindow?.Show();
			_mainWindow?.Activate();
		};

		// Initialize Whisper in background
		var modelPath = System.IO.Path.Combine(AppContext.BaseDirectory,
			SettingsService.Settings.ModelPath);
		_ = WhisperService.InitializeAsync(modelPath);
	}

	private static void CloseSecondaryWindow () {
		_secondaryWindow?.Close();
		_secondaryWindow = null;
	}

	public static void ShowAbout () {
		CloseSecondaryWindow();
		var win = new AboutWindow();
		_secondaryWindow = win;
		win.Closed += (_, _) => { if (_secondaryWindow == win) _secondaryWindow = null; };
		win.Show();
	}

	public static void ShowHistory () {
		CloseSecondaryWindow();
		var win = new HistoryWindow();
		_secondaryWindow = win;
		win.Closed += (_, _) => { if (_secondaryWindow == win) _secondaryWindow = null; };
		win.Show();
	}

	public static void ShowSettings () {
		CloseSecondaryWindow();
		var win = new SettingsWindow();
		_secondaryWindow = win;
		win.Closed += (_, _) => { if (_secondaryWindow == win) _secondaryWindow = null; };
		if (win.ShowDialog() == true) {
			SettingsService.Save();
			History.MaxSize = SettingsService.Settings.HistorySize;
		}
		_secondaryWindow = null;
	}

	private void ExitApp () {
		_trayIcon?.Dispose();
		_mainWindow?.ForceClose();
		Shutdown();
	}

	protected override void OnExit (ExitEventArgs e) {
		_trayIcon?.Dispose();
		LogService.CloseAndFlush();
		base.OnExit(e);
	}
}
