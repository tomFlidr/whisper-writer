using System.Windows;

namespace WhisperWriter.Services.TrayMenus;

/// <summary>Adapts a WPF window to the tray menu window contract.</summary>
internal class WpfWindow : IWindow {
	public event EventHandler? Closed {
		add => this._window.Closed += value;
		remove => this._window.Closed -= value;
	}

	private readonly Window _window;

	public WpfWindow (Window window) {
		this._window = window;
	}

	public void Close () => this._window.Close();
	public void Show () => this._window.Show();
	public bool? ShowDialog () => this._window.ShowDialog();
}