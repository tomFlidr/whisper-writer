using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WhisperWriter.Views;

public partial class SettingsWindow : Window {
	private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private const string StartupValueName = "WhisperWriter";

	private static readonly (string Tag, string Name, string Details)[] _models = [
		("models/ggml-large-v3-turbo.bin", "large-v3-turbo  (best speed/accuracy tradeoff)", "~3 GB VRAM · 1.6 GB disk · CUDA GPU recommended"),
		("models/ggml-large-v3.bin",       "large-v3  (most accurate, latest)",              "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("models/ggml-large-v2.bin",       "large-v2  (most accurate, recommended)",         "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("models/ggml-large-v1.bin",       "large-v1  (accurate, older generation)",         "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("models/ggml-medium.bin",         "medium  (good balance, multilingual)",           "~5 GB VRAM · 1.5 GB disk · runs on CPU"),
		("models/ggml-medium.en.bin",      "medium.en  (good balance, English only)",        "~5 GB VRAM · 1.5 GB disk · runs on CPU"),
		("models/ggml-small.bin",          "small  (fast, multilingual)",                    "~2 GB VRAM · 488 MB disk · runs on CPU"),
		("models/ggml-small.en.bin",       "small.en  (fast, English only)",                 "~2 GB VRAM · 488 MB disk · runs on CPU"),
		("models/ggml-base.bin",           "base  (very fast, multilingual)",                "~1 GB VRAM · 148 MB disk · runs on CPU"),
		("models/ggml-base.en.bin",        "base.en  (very fast, English only)",             "~1 GB VRAM · 148 MB disk · runs on CPU"),
		("models/ggml-tiny.bin",           "tiny  (fastest, multilingual, least accurate)",  "~390 MB VRAM · 78 MB disk · runs on CPU"),
		("models/ggml-tiny.en.bin",        "tiny.en  (fastest, English only, least accurate)", "~390 MB VRAM · 78 MB disk · runs on CPU"),
	];

	public SettingsWindow () {
		InitializeComponent();
		LoadSettings();
	}

	private static bool IsRegisteredAtStartup () {
		using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, false);
		return key?.GetValue(StartupValueName) != null;
	}

	private static void SetStartupRegistry (bool enable) {
		using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
		if (key == null)
			return;
		if (enable) {
			var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			key.SetValue(StartupValueName, $"\"{exePath}\"");
		} else {
			key.DeleteValue(StartupValueName, throwOnMissingValue: false);
		}
	}

	private void BuildModelItems () {
		CmbModelPath.Items.Clear();
		foreach (var (tag, name, details) in _models) {
			var absolutePath = Path.Combine(AppContext.BaseDirectory, tag.Replace('/', Path.DirectorySeparatorChar));
			var exists = File.Exists(absolutePath);
			var detailsText = exists ? details : details + "  ·  not downloaded";

			var nameBlock = new TextBlock {
				Text = name,
				FontWeight = FontWeights.SemiBold,
			};
			var detailsBlock = new TextBlock {
				Text = detailsText,
				FontSize = 11,
				Opacity = 0.65,
			};
			var panel = new StackPanel();
			panel.Children.Add(nameBlock);
			panel.Children.Add(detailsBlock);

			var item = new ComboBoxItem {
				Tag = tag,
				Content = panel,
			};
			if (!exists) {
				item.Opacity = 0.45;
				item.IsEnabled = false;
			}
			CmbModelPath.Items.Add(item);
		}
	}

	private void LoadSettings () {
		BuildModelItems();
		var s = App.SettingsService.Settings;
		foreach (ComboBoxItem item in CmbModelPath.Items) {
			if (item.Tag as string == s.ModelPath) {
				CmbModelPath.SelectedItem = item;
				break;
			}
		}
		if (CmbModelPath.SelectedItem == null) {
			// Fall back to the first downloaded model, or the first item if none downloaded
			foreach (ComboBoxItem item in CmbModelPath.Items) {
				if (item.IsEnabled) {
					CmbModelPath.SelectedItem = item;
					break;
				}
			}
			if (CmbModelPath.SelectedItem == null)
				CmbModelPath.SelectedIndex = 0;
		}
		TxtPrompt.Text = s.Prompt;
		TxtHistorySize.Text = s.HistorySize.ToString();
		ChkCopyToClipboard.IsChecked = s.CopyToClipboard;
		ChkRunAtStartup.IsChecked = IsRegisteredAtStartup();

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
		s.RunAtStartup = ChkRunAtStartup.IsChecked == true;
		SetStartupRegistry(s.RunAtStartup);

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