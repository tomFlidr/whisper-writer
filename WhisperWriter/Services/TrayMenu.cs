using WhisperWriter.DI;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Services.TrayMenus;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Services;

/// <summary>
/// Manages the NotifyIcon in the system tray and its context menu.
/// Responsible for showing auxiliary windows (About, History, Settings) on demand.
/// </summary>
public class TrayMenu : IDisposable, IService, ISingleton {
	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected TranscriptionHistory transcriptionHistory { get; set; } = null!;

	private readonly IPlatform _platform;
	private readonly IDisposable _trayIcon;
	private IWindow? _secondaryWindow;
	private bool _isDisposed;

	/// <summary>
	/// Creates the tray icon and constructs the static context menu.
	/// </summary>
	public TrayMenu () {
		this._platform = new WindowsPlatform(() => this.settings, () => this.transcriptionHistory);
		this._trayIcon = this.createTrayIcon();
	}

	/// <summary>
	/// Creates a tray menu using an injectable platform boundary.
	/// </summary>
	protected TrayMenu (IPlatform platform) {
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
		var win = this._platform.ResolveWindow(WindowKind.About);
		this._secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this._secondaryWindow == win)
				this._secondaryWindow = null;
		};
		win.Show();
	}

	protected void showHistory () {
		this.closeSecondaryWindow();
		var win = this._platform.ResolveWindow(WindowKind.History);
		this._secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this._secondaryWindow == win)
				this._secondaryWindow = null;
		};
		win.Show();
	}

	protected void showSettings () {
		this.closeSecondaryWindow();
		var win = this._platform.ResolveWindow(WindowKind.Settings);
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
}