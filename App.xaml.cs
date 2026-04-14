using System.Windows;
using System.Windows.Forms;
using WhisperWriter.Services;
using WhisperWriter.Views;

namespace WhisperWriter;

public partial class App : System.Windows.Application {
	public static SettingsService SettingsService { get; } = new();
	public static Models.TranscriptionHistory History { get; } = new();
	public static WhisperService WhisperService { get; } = new();

	private NotifyIcon? _trayIcon;
	private MainWindow? _mainWindow;

	protected override async void OnStartup (StartupEventArgs e) {
		base.OnStartup(e);

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

	public static void ShowAbout () {
		var win = new AboutWindow();
		win.Show();
	}

	public static void ShowHistory () {
		var win = new HistoryWindow();
		win.Show();
	}

	public static void ShowSettings () {
		var win = new SettingsWindow();
		if (win.ShowDialog() == true) {
			SettingsService.Save();
			History.MaxSize = SettingsService.Settings.HistorySize;
		}
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
