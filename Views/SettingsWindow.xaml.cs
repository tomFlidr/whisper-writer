using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WhisperWriter.DI;
using WhisperWriter.Services;
using WhisperWriter.Utils;
using WhisperWriter.Utils.Interfaces;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyInterop = System.Windows.Input.KeyInterop;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfTextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;

namespace WhisperWriter.Views;

public partial class SettingsWindow : Window, IService, ITransient {
	[Inject]
	protected SettingsService settingsService { get; set; } = null!;

	private const string _startupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private const string _startupValueName = "WhisperWriter";

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState (int vKey);

	// VK codes excluded from capture (they are UI-navigation keys, not hotkey material).
	private static readonly HashSet<int> _captureExcluded = [
		0x1B, // Escape – cancels capture
		0x0D, // Enter
		0x09, // Tab
	];

	private bool _captureMode;
	// The VK codes held during the last capture session.
	private List<int> _capturedVkCodes = [];
	// All VKs seen as "down" since capture mode started (held simultaneously at peak).
	private readonly HashSet<int> _captureDown = [];
	// The combo that was saved/loaded from settings (displayed when not in capture mode).
	private List<int> _currentVkCodes = [];

	private static readonly (string Tag, string Name, string Details)[] _models = [
		("llms/ggml-large-v3-turbo.bin", "large-v3-turbo  (best speed/accuracy tradeoff)", "~3 GB VRAM · 1.6 GB disk · CUDA GPU recommended"),
		("llms/ggml-large-v3.bin",       "large-v3  (most accurate, latest)",              "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("llms/ggml-large-v2.bin",       "large-v2  (most accurate, recommended)",         "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("llms/ggml-large-v1.bin",       "large-v1  (accurate, older generation)",         "~10 GB VRAM · 3 GB disk · CUDA GPU strongly recommended"),
		("llms/ggml-medium.bin",         "medium  (good balance, multilingual)",           "~5 GB VRAM · 1.5 GB disk · runs on CPU"),
		("llms/ggml-medium.en.bin",      "medium.en  (good balance, English only)",        "~5 GB VRAM · 1.5 GB disk · runs on CPU"),
		("llms/ggml-small.bin",          "small  (fast, multilingual)",                    "~2 GB VRAM · 488 MB disk · runs on CPU"),
		("llms/ggml-small.en.bin",       "small.en  (fast, English only)",                 "~2 GB VRAM · 488 MB disk · runs on CPU"),
		("llms/ggml-base.bin",           "base  (very fast, multilingual)",                "~1 GB VRAM · 148 MB disk · runs on CPU"),
		("llms/ggml-base.en.bin",        "base.en  (very fast, English only)",             "~1 GB VRAM · 148 MB disk · runs on CPU"),
		("llms/ggml-tiny.bin",           "tiny  (fastest, multilingual, least accurate)",  "~390 MB VRAM · 78 MB disk · runs on CPU"),
		("llms/ggml-tiny.en.bin",        "tiny.en  (fastest, English only, least accurate)", "~390 MB VRAM · 78 MB disk · runs on CPU"),
	];

	public SettingsWindow () {
		this.InitializeComponent();
	}
	protected override void OnActivated (EventArgs e) {
		base.OnActivated(e);
		this._loadSettings();
	}

	private static bool _isRegisteredAtStartup () {
		using var key = Registry.CurrentUser.OpenSubKey(_startupKeyPath, false);
		return key?.GetValue(_startupValueName) != null;
	}

	private static void _setStartupRegistry (bool enable) {
		using var key = Registry.CurrentUser.OpenSubKey(_startupKeyPath, true);
		if (key == null)
			return;
		if (enable) {
			var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			key.SetValue(_startupValueName, $"\"{exePath}\"");
		} else {
			key.DeleteValue(_startupValueName, throwOnMissingValue: false);
		}
	}

	private void _buildModelItems () {
		this.CmbModelPath.Items.Clear();
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
			this.CmbModelPath.Items.Add(item);
		}
	}

	private void _loadSettings () {
		this._buildModelItems();
		var s = this.settingsService.Settings;
		foreach (ComboBoxItem item in this.CmbModelPath.Items) {
			if (item.Tag as string == s.ModelPath) {
				this.CmbModelPath.SelectedItem = item;
				break;
			}
		}
		if (this.CmbModelPath.SelectedItem == null) {
			// Fall back to the first downloaded model, or the first item if none downloaded
			foreach (ComboBoxItem item in this.CmbModelPath.Items) {
				if (item.IsEnabled) {
					this.CmbModelPath.SelectedItem = item;
					break;
				}
			}
			if (this.CmbModelPath.SelectedItem == null)
				this.CmbModelPath.SelectedIndex = 0;
		}
		this.TxtPrompt.Text = s.Prompt;
		this.TxtHistorySize.Text = s.HistorySize.ToString();
		this.ChkCopyToClipboard.IsChecked = s.CopyToClipboard;
		this.ChkRunAtStartup.IsChecked = SettingsWindow._isRegisteredAtStartup();

		foreach (ComboBoxItem item in this.CmbLanguage.Items) {
			if (item.Tag as string == s.Language) {
				this.CmbLanguage.SelectedItem = item;
				break;
			}
		}
		if (this.CmbLanguage.SelectedItem == null)
			this.CmbLanguage.SelectedIndex = 0;

		// Hotkey
		this._currentVkCodes = new List<int>(s.HotkeyCodes);
		this._capturedVkCodes = new List<int>(this._currentVkCodes);
		this.TxtHotkeyDisplay.Text = VirtualKeyCodeHelper.FormatCombo(this._currentVkCodes);
	}

	private void _handleTxtHistorySizePreviewTextInput (object sender, WpfTextCompositionEventArgs e) {
		e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
	}

	private void _handleBtnCaptureHotkeyClick (object sender, RoutedEventArgs e) {
		if (this._captureMode) {
			this._exitCaptureMode(accept: false);
			return;
		}
		this._enterCaptureMode();
	}

	private void _enterCaptureMode () {
		this._captureMode = true;
		this._captureDown.Clear();
		this.BtnCaptureHotkey.Content = "Cancel";
		this.TxtCaptureHint.Visibility = Visibility.Visible;
		this.TxtHotkeyDisplay.Text = "(press keys…)";
		this.TxtHotkeyDisplay.Foreground = (System.Windows.Media.SolidColorBrush)
			System.Windows.Application.Current.Resources["AccentBrush"];

		// Hook window-level key events to capture ALL keys including modifiers.
		this.PreviewKeyDown += this._handleCapturePreviewKeyDown;
		this.PreviewKeyUp   += this._handleCapturePreviewKeyUp;

		// Suppress default key handling while capturing so e.g. Tab doesn't move focus.
		this.KeyDown += this._handleCaptureSuppressKey;
		this.KeyUp   += this._handleCaptureSuppressKey;
	}

	private void _exitCaptureMode (bool accept) {
		this._captureMode = false;
		this.PreviewKeyDown -= this._handleCapturePreviewKeyDown;
		this.PreviewKeyUp   -= this._handleCapturePreviewKeyUp;
		this.KeyDown -= this._handleCaptureSuppressKey;
		this.KeyUp   -= this._handleCaptureSuppressKey;

		this.BtnCaptureHotkey.Content = "Change…";
		this.TxtCaptureHint.Visibility = Visibility.Collapsed;
		this.TxtHotkeyDisplay.Foreground = (System.Windows.Media.SolidColorBrush)
			System.Windows.Application.Current.Resources["TextPrimaryBrush"];

		if (accept && this._capturedVkCodes.Count > 0) {
			this._currentVkCodes = new List<int>(this._capturedVkCodes);
		}
		this.TxtHotkeyDisplay.Text = VirtualKeyCodeHelper.FormatCombo(this._currentVkCodes);
		this._capturedVkCodes = new List<int>(this._currentVkCodes);
	}

	private void _handleCapturePreviewKeyDown (object sender, WpfKeyEventArgs e) {
		e.Handled = true;
		var vk = WpfKeyInterop.VirtualKeyFromKey(e.Key == WpfKey.System ? e.SystemKey : e.Key);

		// Escape cancels without accepting.
		if (vk == 0x1B) {
			this._exitCaptureMode(accept: false);
			return;
		}

		if (!_captureExcluded.Contains(vk)) {
			this._captureDown.Add(vk);
			// Show live preview of currently held keys.
			this.TxtHotkeyDisplay.Text = VirtualKeyCodeHelper.FormatCombo(this._captureDown.ToList());
		}
	}

	private void _handleCapturePreviewKeyUp (object sender, WpfKeyEventArgs e) {
		e.Handled = true;

		// On first key release: snapshot all keys that were down simultaneously.
		if (this._captureDown.Count > 0) {
			this._capturedVkCodes = [..this._captureDown];
			this._captureDown.Clear();
		}
		this._exitCaptureMode(accept: true);
	}

	private void _handleCaptureSuppressKey (object sender, WpfKeyEventArgs e) {
		e.Handled = true;
	}

	private void _titleBar_MouseLeftButtonDown (object sender, WpfMouseButtonEventArgs e) {
		if (e.ButtonState == WpfMouseButtonState.Pressed)
			this.DragMove();
	}

	private void _btnSave_Click (object sender, RoutedEventArgs e) {
		var s = this.settingsService.Settings;
		if (this.CmbModelPath.SelectedItem is ComboBoxItem selectedModel)
			s.ModelPath = selectedModel.Tag as string ?? "llms/ggml-large-v2.bin";
		s.Prompt = this.TxtPrompt.Text;
		s.HistorySize = int.TryParse(this.TxtHistorySize.Text, out int historySize) && historySize >= 1
			? historySize
			: 1;
		s.CopyToClipboard = this.ChkCopyToClipboard.IsChecked == true;
		s.RunAtStartup = this.ChkRunAtStartup.IsChecked == true;
		_setStartupRegistry(s.RunAtStartup);

		if (this.CmbLanguage.SelectedItem is ComboBoxItem selected)
			s.Language = selected.Tag as string ?? "auto";

		// Persist hotkey VK codes
		s.HotkeyCodes = new List<int>(this._currentVkCodes);

		this.DialogResult = true;
		this.Close();
	}

	private void _btnCancel_Click (object sender, RoutedEventArgs e) {
		this.DialogResult = false;
		this.Close();
	}
}