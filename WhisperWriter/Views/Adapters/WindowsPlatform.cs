using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;

namespace WhisperWriter.Views.Adapters;

public class WindowsPlatform : IPlatform {
	public DateTime UtcNow => DateTime.UtcNow;

	private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private const string StartupValueName = "WhisperWriter";

	public bool FileExists (string path) => File.Exists(path);

	public bool IsRegisteredAtStartup () {
		using var key = Registry.CurrentUser.OpenSubKey(WindowsPlatform.StartupKeyPath, false);
		return key?.GetValue(WindowsPlatform.StartupValueName) != null;
	}

	public void SetStartupRegistration (bool enabled) {
		using var key = Registry.CurrentUser.OpenSubKey(WindowsPlatform.StartupKeyPath, true);
		if (key == null)
			return;
		if (enabled) {
			var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			key.SetValue(WindowsPlatform.StartupValueName, $"\"{exePath}\"");
		} else {
			key.DeleteValue(WindowsPlatform.StartupValueName, throwOnMissingValue: false);
		}
	}

	public void SetClipboardText (string text) => System.Windows.Clipboard.SetText(text);

	public void DragMove (Window window) => window.DragMove();

	public bool IsLeftButtonPressed (System.Windows.Input.MouseButtonEventArgs args) =>
		args.ButtonState == System.Windows.Input.MouseButtonState.Pressed;

	public void SetDialogResult (Window window, bool result) => window.DialogResult = result;

	public void BeginStoryboard (Window window, string resourceKey, bool controllable = false) =>
		((Storyboard)window.Resources[resourceKey]).Begin(window, controllable);

	public void StopStoryboard (Window window, string resourceKey) =>
		((Storyboard)window.Resources[resourceKey]).Stop(window);
}