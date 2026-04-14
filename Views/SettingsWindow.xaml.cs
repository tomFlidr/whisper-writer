using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WhisperWriter.Views;

public partial class SettingsWindow : Window {
	public SettingsWindow () {
		InitializeComponent();
		LoadSettings();
	}

	private void LoadSettings () {
		var s = App.SettingsService.Settings;
		foreach (ComboBoxItem item in CmbModelPath.Items) {
			var relativePath = item.Tag as string ?? string.Empty;
			var fullPath = System.IO.Path.Combine(AppContext.BaseDirectory, relativePath);
			var downloaded = System.IO.File.Exists(fullPath);
			item.IsEnabled = downloaded;
			item.Opacity = downloaded ? 1.0 : 0.4;
		}
		foreach (ComboBoxItem item in CmbModelPath.Items) {
			if (item.Tag as string == s.ModelPath) {
				CmbModelPath.SelectedItem = item;
				break;
			}
		}
		if (CmbModelPath.SelectedItem == null)
			CmbModelPath.SelectedIndex = 0;
		TxtPrompt.Text = s.Prompt;
		TxtHistorySize.Text = s.HistorySize.ToString();
		ChkCopyToClipboard.IsChecked = s.CopyToClipboard;

		// Select matching language item
		foreach (ComboBoxItem item in CmbLanguage.Items) {
			if (item.Tag as string == s.Language) {
				CmbLanguage.SelectedItem = item;
				break;
			}
		}
		if (CmbLanguage.SelectedItem == null)
			CmbLanguage.SelectedIndex = 0;
	}

	private void TxtHistorySize_PreviewTextInput (object sender, TextCompositionEventArgs e) {
		// Allow digits only
		e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
	}

	private void TitleBar_MouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
		if (e.ButtonState == MouseButtonState.Pressed)
			DragMove();
	}

	private void BtnSave_Click (object sender, RoutedEventArgs e) {
		var s = App.SettingsService.Settings;
		if (CmbModelPath.SelectedItem is ComboBoxItem selectedModel)
			s.ModelPath = selectedModel.Tag as string ?? "models/ggml-large-v2.bin";
		s.Prompt = TxtPrompt.Text;
		s.HistorySize = int.TryParse(TxtHistorySize.Text, out int historySize) && historySize >= 1
			? historySize
			: 1;
		s.CopyToClipboard = ChkCopyToClipboard.IsChecked == true;

		if (CmbLanguage.SelectedItem is ComboBoxItem selected)
			s.Language = selected.Tag as string ?? "auto";

		DialogResult = true;
		Close();
	}

	private void BtnCancel_Click (object sender, RoutedEventArgs e) {
		DialogResult = false;
		Close();
	}
}
