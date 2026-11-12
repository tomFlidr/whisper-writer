namespace WhisperWriter.Services.TrayMenus;

public interface IWindow {
	event EventHandler? Closed;
	void Close ();
	void Show ();
	bool? ShowDialog ();
}