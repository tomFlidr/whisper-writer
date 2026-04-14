using System.Windows;
using System.Windows.Input;
using WhisperWriter.Models;

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

	private void HistoryList_SelectionChanged (object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
		BtnCopy.IsEnabled = HistoryList.SelectedItem is TranscriptionEntry;
	}

	private void BtnCopy_Click (object sender, RoutedEventArgs e) {
		if (HistoryList.SelectedItem is TranscriptionEntry entry) {
			System.Windows.Clipboard.SetText(entry.Text);
			FooterHint.Text = "Copied!";
		}
	}
}
