using Autofac;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using WhisperWriter;
using WhisperWriter.Services;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Views.Screens;

namespace WhisperWriter.Services.TrayMenus;

/// <summary>Connects the tray menu to Windows shell and application services.</summary>
internal class WindowsPlatform : IPlatform {
	public int HistorySize => this._settings().Data.HistorySize;

	private readonly Func<Settings> _settings;
	private readonly Func<TranscriptionHistory> _transcriptionHistory;

	public WindowsPlatform (Func<Settings> settings, Func<TranscriptionHistory> transcriptionHistory) {
		this._settings = settings;
		this._transcriptionHistory = transcriptionHistory;
	}

	public void SaveSettings () => this._settings().Save();
	public void SetHistorySize (int historySize) => this._transcriptionHistory().MaxSize = historySize;
	public void ReloadHotkey () => Program.App.MainWindow?.ReloadHotkey();
	public void ExitApp () => Program.App.ExitApp();

	public void ShowMainWindow () {
		var mainWindow = Program.App.MainWindow;
		mainWindow?.Show();
		mainWindow?.Activate();
	}

	public IDisposable CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick) {
		var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
		var trayImage = File.Exists(icoPath)
			? new System.Drawing.Icon(icoPath)
			: System.Drawing.SystemIcons.Application;
		var trayIcon = new NotifyIcon {
			Icon = trayImage,
			Text = "WhisperWriter",
			Visible = true,
		};

		var menu = new ContextMenuStrip();
		menu.Items.Add("About", null, (_, _) => about());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Transcriptions", null, (_, _) => history());
		menu.Items.Add("Settings", null, (_, _) => settings());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) => exit());
		trayIcon.ContextMenuStrip = menu;
		trayIcon.DoubleClick += (_, _) => doubleClick();
		return trayIcon;
	}

	public IWindow ResolveWindow (WindowKind kind) {
		Window window = kind switch {
			WindowKind.About => Program.DI.Provider.Resolve<AboutWindow>(),
			WindowKind.History => Program.DI.Provider.Resolve<HistoryWindow>(),
			WindowKind.Settings => Program.DI.Provider.Resolve<SettingsWindow>(),
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		};
		return new WpfWindow(window);
	}
}