using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using WhisperWriter.Services;

namespace WhisperWriter.Views;

public class WindowsWindowPlatform: IWindowPlatform {
	private const string _startupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private const string _startupValueName = "WhisperWriter";

	public DateTime UtcNow => DateTime.UtcNow;

	public int? DetectCudaVersion () => WhisperWriter.Services.Whisper.DetectCudaVersion();

	public bool FileExists (string path) => File.Exists(path);

	public bool IsRegisteredAtStartup () {
		using var key = Registry.CurrentUser.OpenSubKey(WindowsWindowPlatform._startupKeyPath, false);
		return key?.GetValue(WindowsWindowPlatform._startupValueName) != null;
	}

	public void SetStartupRegistration (bool enabled) {
		using var key = Registry.CurrentUser.OpenSubKey(WindowsWindowPlatform._startupKeyPath, true);
		if (key == null)
			return;
		if (enabled) {
			var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			key.SetValue(WindowsWindowPlatform._startupValueName, $"\"{exePath}\"");
		} else {
			key.DeleteValue(WindowsWindowPlatform._startupValueName, throwOnMissingValue: false);
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