using System.Windows;

namespace WhisperWriter.Tests.TestSupport;

internal class FakeWindowPlatform : WhisperWriter.Views.Adapters.IPlatform {
	public DateTime UtcNow { get; set; } = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
	public bool IsStartupRegistered { get; set; }
	public bool LeftButtonPressed { get; set; }
	public Func<string, bool> ExistingFile { get; set; } = _ => false;
	public List<string> ClipboardTexts { get; } = [];
	public List<Window> DraggedWindows { get; } = [];
	public List<bool> StartupRegistrationChanges { get; } = [];
	public List<(Window Window, bool Result)> DialogResults { get; } = [];
	public List<(Window Window, string ResourceKey, bool Controllable)> StoryboardBegins { get; } = [];
	public List<(Window Window, string ResourceKey)> StoryboardStops { get; } = [];

	public bool FileExists (string path) => this.ExistingFile(path);
	public bool IsRegisteredAtStartup () => this.IsStartupRegistered;
	public void SetStartupRegistration (bool enabled) => this.StartupRegistrationChanges.Add(enabled);
	public void SetClipboardText (string text) => this.ClipboardTexts.Add(text);
	public void DragMove (Window window) => this.DraggedWindows.Add(window);
	public bool IsLeftButtonPressed (System.Windows.Input.MouseButtonEventArgs args) => this.LeftButtonPressed;
	public void SetDialogResult (Window window, bool result) => this.DialogResults.Add((window, result));
	public void BeginStoryboard (Window window, string resourceKey, bool controllable = false) =>
		this.StoryboardBegins.Add((window, resourceKey, controllable));
	public void StopStoryboard (Window window, string resourceKey) => this.StoryboardStops.Add((window, resourceKey));
}