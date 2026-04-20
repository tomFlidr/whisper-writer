using System.Windows;
using System.Windows.Input;
using WhisperWriter.Util;

namespace WhisperWriter.Views;

public partial class HistoryWindow : Window {
	public HistoryWindow () {
		this.InitializeComponent();
		this.HistoryList.ItemsSource = App.History.Entries;
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