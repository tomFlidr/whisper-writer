using Autofac;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using WhisperWriter.DI;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views;

namespace WhisperWriter.Services;
public class TrayMenu: IDisposable, IService, ISingleton {
	[Inject]
	protected Settings settings { get; set; } = null!;
	[Inject]
	protected TranscriptionHistory transcriptionHistory { get; set; } = null!;
	
	protected Window? secondaryWindow;
	
	protected NotifyIcon? trayIcon;

	public TrayMenu () {
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
		menu.Items.Add("About", null, (_, _) => this.showAbout());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Transcriptions", null, (_, _) => this.showHistory());
		menu.Items.Add("Settings", null, (_, _) => this.showSettings());
		menu.Items.Add(new ToolStripSeparator());
		menu.Items.Add("Exit", null, (_, _) => Program.App.ExitApp());

		this.trayIcon.ContextMenuStrip = menu;

		// Double-click shows / restores the widget
		this.trayIcon.DoubleClick += (_, _) => {
			var mainWindow = Program.App.MainWindow;
			mainWindow?.Show();
			mainWindow?.Activate();
		};
	}
	
	public void Dispose() {
		this.trayIcon?.Dispose();
	}
	
	public void HandleAppExit () {
		this.trayIcon?.Dispose();
	}

	protected void closeSecondaryWindow () {
		this.secondaryWindow?.Close();
		this.secondaryWindow = null;
	}

	protected void showAbout () {
		this.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<AboutWindow>();
		this.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this.secondaryWindow == win) 
				this.secondaryWindow = null;
		};
		win.Show();
	}

	protected void showHistory () {
		this.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<HistoryWindow>();
		this.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this.secondaryWindow == win) 
				this.secondaryWindow = null;
		};
		win.Show();
	}
	
	protected void showSettings () {
		this.closeSecondaryWindow();
		var win = Program.DI.Provider.Resolve<SettingsWindow>();
		this.secondaryWindow = win;
		win.Closed += (_, _) => {
			if (this.secondaryWindow == win) 
				this.secondaryWindow = null;
		};
		if (win.ShowDialog() == true) {
			this.settings.Save();
			this.transcriptionHistory.MaxSize = this.settings.Data.HistorySize;
			// Apply new hotkey combination immediately, without restarting.
			Program.App.MainWindow?.ReloadHotkey();
		}
		this.secondaryWindow = null;
	}

}
