using Autofac;
using System.Windows;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter.Services;

/// <summary>
/// Manages the NotifyIcon in the system tray and its context menu.
/// Responsible for showing auxiliary windows (About, History, Settings) on demand.
/// </summary>
public class TrayMenu: IDisposable, IService, ISingleton {
	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected TranscriptionHistory transcriptionHistory { get; set; } = null!;

	private readonly ITrayMenuPlatform _platform;
	private readonly IDisposable _trayIcon;
	private ITrayMenuWindow? _secondaryWindow;
	private bool _isDisposed;

	/// <summary>
	/// Creates the tray icon and constructs the static context menu.
	/// </summary>
	public TrayMenu () {
		this._platform = new WindowsTrayMenuPlatform(this);
		this._trayIcon = this.createTrayIcon();
	}

	/// <summary>
	/// Creates a tray menu using an injectable platform boundary.
	/// </summary>
	protected TrayMenu (ITrayMenuPlatform platform) {
		this._platform = platform;
		this._trayIcon = this.createTrayIcon();
	}

	/// <summary>
	/// Disposes the tray icon and releases native resources.
	/// </summary>
	public void Dispose () {
		if (this._isDisposed)
			return;

		this._isDisposed = true;
		this._trayIcon.Dispose();
	}

	/// <summary>
	/// Called by the application exit flow to ensure the tray icon is removed immediately.
	/// </summary>
	public void HandleAppExit () {
		this.Dispose();
	}

	protected void closeSecondaryWindow () {
		this._secondaryWindow?.Close();
		this._secondaryWindow = null;
	}

	protected void showAbout () {
		this.closeSecondaryWindow();
		var win = this._platform.ResolveWindow(TrayWindowKind.About);
		this._secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this._secondaryWindow == win)
				this._secondaryWindow = null;
		};
		win.Show();
	}

	protected void showHistory () {
		this.closeSecondaryWindow();
		var win = this._platform.ResolveWindow(TrayWindowKind.History);
		this._secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this._secondaryWindow == win)
				this._secondaryWindow = null;
		};
		win.Show();
	}

	protected void showSettings () {
		this.closeSecondaryWindow();
		var win = this._platform.ResolveWindow(TrayWindowKind.Settings);
		this._secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this._secondaryWindow == win)
				this._secondaryWindow = null;
		};
		if (win.ShowDialog() == true) {
			this._platform.SaveSettings();
			this._platform.SetHistorySize(this._platform.HistorySize);
			this._platform.ReloadHotkey();
		}
		this._secondaryWindow = null;
	}

	private IDisposable createTrayIcon () {
		return this._platform.CreateTrayIcon(
			this.showAbout,
			this.showHistory,
			this.showSettings,
			this._platform.ExitApp,
			this._platform.ShowMainWindow
		);
	}

	protected enum TrayWindowKind {
		About,
		History,
		Settings,
	}

	protected interface ITrayMenuWindow {
		event EventHandler? Closed;
		void Close ();
		void Show ();
		bool? ShowDialog ();
	}

	protected interface ITrayMenuPlatform {
		int HistorySize { get; }
		IDisposable CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick);
		ITrayMenuWindow ResolveWindow (TrayWindowKind kind);
		void SaveSettings ();
		void SetHistorySize (int historySize);
		void ReloadHotkey ();
		void ExitApp ();
		void ShowMainWindow ();
	}

	private class WindowsTrayMenuPlatform: ITrayMenuPlatform {
		public int HistorySize => this._owner.settings.Data.HistorySize;

		private readonly TrayMenu _owner;

		public WindowsTrayMenuPlatform (TrayMenu owner) {
			this._owner = owner;
		}

		public void SaveSettings () {
			this._owner.settings.Save();
		}

		public void SetHistorySize (int historySize) {
			this._owner.transcriptionHistory.MaxSize = historySize;
		}

		public void ReloadHotkey () {
			Program.App.MainWindow?.ReloadHotkey();
		}

		public void ExitApp () {
			Program.App.ExitApp();
		}

		public void ShowMainWindow () {
			var mainWindow = Program.App.MainWindow;
			mainWindow?.Show();
			mainWindow?.Activate();
		}

		public IDisposable CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick) {
			var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
			var trayImage = System.IO.File.Exists(icoPath)
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

		public ITrayMenuWindow ResolveWindow (TrayWindowKind kind) {
			Window window = kind switch {
				TrayWindowKind.About => Program.DI.Provider.Resolve<AboutWindow>(),
				TrayWindowKind.History => Program.DI.Provider.Resolve<HistoryWindow>(),
				TrayWindowKind.Settings => Program.DI.Provider.Resolve<SettingsWindow>(),
				_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
			};
			return new WpfTrayMenuWindow(window);
		}
	}

	private class WpfTrayMenuWindow: ITrayMenuWindow {
		private readonly Window _window;

		public WpfTrayMenuWindow (Window window) {
			this._window = window;
		}

		public event EventHandler? Closed {
			add => this._window.Closed += value;
			remove => this._window.Closed -= value;
		}

		public void Close () {
			this._window.Close();
		}

		public void Show () {
			this._window.Show();
		}

		public bool? ShowDialog () {
			return this._window.ShowDialog();
		}
	}
}
