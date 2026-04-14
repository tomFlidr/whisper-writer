using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WhisperWriter.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow()
	{
		InitializeComponent();
		LoadSettings();
	}

	private void LoadSettings()
	{
		var s = App.SettingsService.Settings;
		TxtModelPath.Text = s.ModelPath;
		TxtPrompt.Text = s.Prompt;
		SliderHistory.Value = s.HistorySize;

		// Select matching language item
		foreach (ComboBoxItem item in CmbLanguage.Items)
		{
			if (item.Tag as string == s.Language)
			{
				CmbLanguage.SelectedItem = item;
				break;
			}
		}
		if (CmbLanguage.SelectedItem == null)
			CmbLanguage.SelectedIndex = 0;
	}

	private void SliderHistory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (HistorySizeLabel != null)
			HistorySizeLabel.Text = ((int)e.NewValue).ToString();
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
			DragMove();
	}

	private void BtnSave_Click(object sender, RoutedEventArgs e)
	{
		var s = App.SettingsService.Settings;
		s.ModelPath = TxtModelPath.Text.Trim();
		s.Prompt = TxtPrompt.Text;
		s.HistorySize = (int)SliderHistory.Value;

		if (CmbLanguage.SelectedItem is ComboBoxItem selected)
			s.Language = selected.Tag as string ?? "auto";

		DialogResult = true;
		Close();
	}

	private void BtnCancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
