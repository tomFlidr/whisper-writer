using System.Windows;
using System.Windows.Input;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.Persistence;
using WhisperWriter.Utils.Interfaces;
using WhisperWriter.Views.Adapters;

namespace WhisperWriter.Views.Screens;

/// <summary>
/// Window that displays the transcription history list and allows copying individual entries.
/// </summary>
public partial class HistoryWindow : Window, IService, ITransient {
	[Inject]
	protected TranscriptionHistory history { get; set; } = null!;
	protected IPlatform windowPlatform { get; set; } = new WindowsPlatform();
	/// <summary>
	/// Initializes the history window and its UI components.
	/// </summary>
	public HistoryWindow () {
		this.InitializeComponent();
	}
	/// <summary>
	/// Binds the observable entries collection to the ListView when the window is activated.
	/// </summary>
	protected override void OnActivated (EventArgs e) {
		base.OnActivated(e);
		this.HistoryList.ItemsSource = this.history.Entries;
	}
	private void _handleTitleBarMouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
		if (this.windowPlatform.IsLeftButtonPressed(e))
			this.windowPlatform.DragMove(this);
	}
	private void _handleBtnCopyEntryClick (object sender, RoutedEventArgs e) {
		if (sender is System.Windows.Controls.Button btn && btn.DataContext is TranscriptionEntry entry)
			this.windowPlatform.SetClipboardText(entry.Text);
	}
	private void _handleBtnCloseClick (object sender, RoutedEventArgs e) => this.Close();
}