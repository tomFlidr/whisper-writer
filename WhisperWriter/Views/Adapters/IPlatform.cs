using System.Windows;

namespace WhisperWriter.Views.Adapters;

public interface IPlatform {
	DateTime UtcNow { get; }
	bool FileExists (string path);
	bool IsRegisteredAtStartup ();
	void SetStartupRegistration (bool enabled);
	void SetClipboardText (string text);
	void DragMove (Window window);
	bool IsLeftButtonPressed (System.Windows.Input.MouseButtonEventArgs args);
	void SetDialogResult (Window window, bool result);
	void BeginStoryboard (Window window, string resourceKey, bool controllable = false);
	void StopStoryboard (Window window, string resourceKey);
}