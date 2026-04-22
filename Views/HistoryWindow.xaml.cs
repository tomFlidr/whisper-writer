using System.Windows;
using System.Windows.Input;
using WhisperWriter.DI;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Views;

public partial class HistoryWindow : Window, IService, ITransient {
	[Inject]
	protected TranscriptionHistory history { get; set; } = null!;
	public HistoryWindow () {
		this.InitializeComponent();
	}
	protected override void OnActivated (EventArgs e) {
		base.OnActivated(e);
		this.HistoryList.ItemsSource = this.history.Entries;
	}
	private void _handleTitleBarMouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
		if (e.ButtonState == MouseButtonState.Pressed)
			this.DragMove();
	}
	private void _handleBtnCopyEntryClick (object sender, RoutedEventArgs e) {
		if (sender is System.Windows.Controls.Button btn && btn.DataContext is TranscriptionEntry entry)
			System.Windows.Clipboard.SetText(entry.Text);
	}
	private void _handleBtnCloseClick (object sender, RoutedEventArgs e) => this.Close();
}