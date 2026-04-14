using System.Windows;
using System.Windows.Input;
using WhisperWriter.Util;

namespace WhisperWriter.Views;

public partial class HistoryWindow : Window {
	public HistoryWindow () {
		InitializeComponent();
		HistoryList.ItemsSource = App.History.Entries;
	}

	private void TitleBar_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
		if (e.ButtonState == MouseButtonState.Pressed)
			DragMove();
	}

	private void BtnClose_Click (object sender, RoutedEventArgs e) => Close();

	private void BtnCopyEntry_Click (object sender, RoutedEventArgs e) {
		if (sender is System.Windows.Controls.Button btn && btn.DataContext is TranscriptionEntry entry)
			System.Windows.Clipboard.SetText(entry.Text);
	}
}