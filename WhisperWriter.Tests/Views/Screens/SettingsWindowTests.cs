using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views.Adapters;
using WhisperWriter.Views.Screens;
using Xunit;

namespace WhisperWriter.Tests.Views.Screens;

public class SettingsWindowTests {
	private static TestSettings createSettings () => new();

	private static SettingsWindow createWindow (Settings settings, IPlatform platform) {
		var window = new SettingsWindow();
		SettingsWindowTests.setProtectedProperty(window, "settings", settings);
		SettingsWindowTests.setProtectedProperty(window, "windowPlatform", platform);
		return window;
	}

	private static void activate (SettingsWindow window) =>
		typeof(SettingsWindow).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(window, [EventArgs.Empty]);

	private static void setProtectedProperty (SettingsWindow window, string name, object value) =>
		typeof(SettingsWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(window, value);

	private static ComboBox modelCombo (SettingsWindow window) => (ComboBox)window.FindName("CmbModelPath");
	private static ComboBox languageCombo (SettingsWindow window) => (ComboBox)window.FindName("CmbLanguage");
	private static TextBox prompt (SettingsWindow window) => (TextBox)window.FindName("TxtPrompt");
	private static TextBox historySize (SettingsWindow window) => (TextBox)window.FindName("TxtHistorySize");
	private static CheckBox copyToClipboard (SettingsWindow window) => (CheckBox)window.FindName("ChkCopyToClipboard");
	private static CheckBox runAtStartup (SettingsWindow window) => (CheckBox)window.FindName("ChkRunAtStartup");
	private static TextBlock hotkeyDisplay (SettingsWindow window) => (TextBlock)window.FindName("TxtHotkeyDisplay");
	private static Button captureButton (SettingsWindow window) => (Button)window.FindName("BtnCaptureHotkey");

	private static IEnumerable<Button> findButtons (DependencyObject root) {
		foreach (object child in LogicalTreeHelper.GetChildren(root)) {
			if (child is Button button)
				yield return button;
			if (child is DependencyObject dependencyObject) {
				foreach (Button nested in SettingsWindowTests.findButtons(dependencyObject))
					yield return nested;
			}
		}
	}

	private static KeyEventArgs raiseKey (SettingsWindow window, RoutedEvent routedEvent, Key key) {
		var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) {
			RoutedEvent = routedEvent,
			Source = window,
		};
		window.RaiseEvent(args);
		return args;
	}

	private static TextCompositionEventArgs createTextInput (SettingsWindow window, string text) {
		var composition = new TextComposition(InputManager.Current, window, text);
		return new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) {
			RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
			Source = window,
		};
	}

	[Fact]
	public void Activation_LoadsSettingsBuildsModelItemsAndUsesTheInjectedStartupState () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.ModelPath = "llms/ggml-medium.bin";
			settings.Data.Language = "cs";
			settings.Data.Prompt = "domain terms";
			settings.Data.HistorySize = 9;
			settings.Data.CopyToClipboard = false;
			settings.Data.HotkeyCodes = [0xA4, 0x5B];
			var platform = new FakeWindowPlatform {
				IsStartupRegistered = true,
				ExistingFile = path => path.EndsWith("ggml-medium.bin", StringComparison.Ordinal),
			};
			var window = SettingsWindowTests.createWindow(settings, platform);

			SettingsWindowTests.activate(window);
			SettingsWindowTests.activate(window);

			Assert.Equal(12, SettingsWindowTests.modelCombo(window).Items.Count);
			Assert.Equal("llms/ggml-medium.bin", ((ComboBoxItem)SettingsWindowTests.modelCombo(window).SelectedItem).Tag);
			Assert.True(((ComboBoxItem)SettingsWindowTests.modelCombo(window).SelectedItem).IsEnabled);
			Assert.Equal("domain terms", SettingsWindowTests.prompt(window).Text);
			Assert.Equal("9", SettingsWindowTests.historySize(window).Text);
			Assert.False(SettingsWindowTests.copyToClipboard(window).IsChecked);
			Assert.True(SettingsWindowTests.runAtStartup(window).IsChecked);
			Assert.Equal("cs", ((ComboBoxItem)SettingsWindowTests.languageCombo(window).SelectedItem).Tag);
			Assert.Equal(58, SettingsWindowTests.languageCombo(window).Items.Count);
			Assert.Equal("L Alt + L Win", SettingsWindowTests.hotkeyDisplay(window).Text);
			var unavailable = SettingsWindowTests.modelCombo(window).Items.Cast<ComboBoxItem>()
				.Single(item => Equals(item.Tag, "llms/ggml-large-v3.bin"));
			Assert.False(unavailable.IsEnabled);
			Assert.Equal(0.45, unavailable.Opacity);
		});
	}

	[Fact]
	public void Activation_FallsBackToTheFirstModelAndAutoLanguageWhenSavedValuesAreUnknown () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.ModelPath = "llms/not-installed.bin";
			settings.Data.Language = "unknown";
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());

			SettingsWindowTests.activate(window);

			Assert.Equal(0, SettingsWindowTests.modelCombo(window).SelectedIndex);
			Assert.Equal("auto", ((ComboBoxItem)SettingsWindowTests.languageCombo(window).SelectedItem).Tag);
		});
	}

	[Fact]
	public void QueueLimits_LoadFromAndSaveToSettingsWithExplanatoryText () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.MaxOutstandingTranscriptionJobs = 12;
			settings.Data.MaxBufferedAudioMiB = 96;
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());
			SettingsWindowTests.activate(window);
			window.Show();

			var maxJobs = window.FindName("TxtMaxOutstandingJobs") as TextBox;
			var audioBudget = window.FindName("TxtAudioBufferBudgetMiB") as TextBox;
			var description = window.FindName("TxtAudioBufferBudgetDescription") as TextBlock;
			Assert.NotNull(maxJobs);
			Assert.NotNull(audioBudget);
			Assert.NotNull(description);
			Assert.Equal("12", maxJobs.Text);
			Assert.Equal("96", audioBudget.Text);
			Assert.Contains("waiting", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("transcription completes", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("Whisper model", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("may push usage over it", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("until", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("Busy", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("try again", description.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("all outstanding jobs finish", description.Text, StringComparison.OrdinalIgnoreCase);

			maxJobs.Text = "7";
			audioBudget.Text = "64";
			var save = (Button)window.FindName("BtnSave");
			save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));

			Assert.Equal(7, settings.Data.MaxOutstandingTranscriptionJobs);
			Assert.Equal(64, settings.Data.MaxBufferedAudioMiB);
		});
	}

	[Fact]
	public void ParakeetOrtBackend_LoadsAndSavesTheSelectedProviderThroughTheXamlBinding () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.OrtBackend = "Cuda";
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());
			SettingsWindowTests.activate(window);
			var provider = window.FindName("CmbOrtBackend") as ComboBox;
			Assert.NotNull(provider);
			Assert.Equal("Cuda", ((ComboBoxItem)provider.SelectedItem).Tag);
			provider.SelectedItem = provider.Items.Cast<ComboBoxItem>()
				.Single(item => Equals(item.Tag, "DirectMl"));

			var save = (Button)window.FindName("BtnSave");
			save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));

			Assert.Equal("DirectMl", settings.Data.OrtBackend);
		});
	}

	[Fact]
	public void ParakeetProviderSettingsExplainPackInstallationAndCudaRequirements () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());
			SettingsWindowTests.activate(window);
			var providerInfo = window.FindName("TxtOrtBackendDescription") as TextBlock;
			var packInfo = window.FindName("TxtOrtProviderPackDescription") as TextBlock;
			var cudaInfo = window.FindName("TxtOrtCudaRuntimeDescription") as TextBlock;
			Assert.NotNull(providerInfo);
			Assert.NotNull(packInfo);
			Assert.NotNull(cudaInfo);
			Assert.Contains("CPU pack ships with x64", providerInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("Whisper is unchanged", providerInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("DirectML pack is optional", packInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("CUDA pack is not included in v1.4.0", packInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("restart", packInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("existing Whisper CUDA folder", cudaInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("CUDA 13 and cuDNN 9", cudaInfo.Text, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("will not enable Parakeet inference", cudaInfo.Text, StringComparison.OrdinalIgnoreCase);
		});
	}

	[Fact]
	public void ParakeetCudaRuntimePath_LoadsAndSavesThroughTheSettingsWindow () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.OrtCudaRuntimePath = @"D:\provider\cuda";
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());
			SettingsWindowTests.activate(window);
			var path = window.FindName("TxtOrtCudaRuntimePath") as TextBox;
			Assert.NotNull(path);
			Assert.Equal(@"D:\provider\cuda", path.Text);
			path.Text = @"D:\other\cudnn";

			var save = (Button)window.FindName("BtnSave");
			save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));

			Assert.Equal(@"D:\other\cudnn", settings.Data.OrtCudaRuntimePath);
		});
	}

	[Fact]
	public void SaveButton_PersistsFormValuesAndRegistryChoiceThroughTheXamlBinding () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			var platform = new FakeWindowPlatform();
			var window = SettingsWindowTests.createWindow(settings, platform);
			SettingsWindowTests.activate(window);
			window.Show();
			SettingsWindowTests.captureButton(window).RaiseEvent(new RoutedEventArgs(
				Button.ClickEvent, SettingsWindowTests.captureButton(window)));
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.LeftShift);
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyUpEvent, Key.LeftShift);
			SettingsWindowTests.modelCombo(window).SelectedItem = SettingsWindowTests.modelCombo(window).Items.Cast<ComboBoxItem>()
				.Single(item => Equals(item.Tag, "llms/ggml-tiny.bin"));
			SettingsWindowTests.languageCombo(window).SelectedItem = SettingsWindowTests.languageCombo(window).Items.Cast<ComboBoxItem>()
				.Single(item => Equals(item.Tag, "cs"));
			SettingsWindowTests.prompt(window).Text = "medical vocabulary";
			SettingsWindowTests.historySize(window).Text = "0";
			SettingsWindowTests.copyToClipboard(window).IsChecked = false;
			SettingsWindowTests.runAtStartup(window).IsChecked = true;
			var save = (Button)window.FindName("BtnSave");

			save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save));

			Assert.Equal("llms/ggml-tiny.bin", settings.Data.ModelPath);
			Assert.Equal("cs", settings.Data.Language);
			Assert.Equal("medical vocabulary", settings.Data.Prompt);
			Assert.Equal(1, settings.Data.HistorySize);
			Assert.False(settings.Data.CopyToClipboard);
			Assert.True(settings.Data.RunAtStartup);
			Assert.Equal(new[] { 0xA0, 0xA2 }, settings.Data.HotkeyCodes.Order());
			Assert.Equal(new[] { true }, platform.StartupRegistrationChanges);
			Assert.Single(platform.DialogResults);
			Assert.True(platform.DialogResults[0].Result);
		});
	}

	[Fact]
	public void CancelButton_DoesNotApplyEditedSettingsAndReturnsFalseThroughTheXamlBinding () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.Prompt = "original";
			var platform = new FakeWindowPlatform();
			var window = SettingsWindowTests.createWindow(settings, platform);
			SettingsWindowTests.activate(window);
			SettingsWindowTests.prompt(window).Text = "discard this";
			var cancel = SettingsWindowTests.findButtons(window)
				.Single(button => Equals(button.Content, "Cancel"));

			cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancel));

			Assert.Equal("original", settings.Data.Prompt);
			Assert.Empty(platform.StartupRegistrationChanges);
			Assert.Single(platform.DialogResults);
			Assert.False(platform.DialogResults[0].Result);
		});
	}

	[Fact]
	public void CloseGlyph_CancelsThroughItsXamlClickBinding () {
		WpfTestHost.Invoke(() => {
			var platform = new FakeWindowPlatform();
			var window = SettingsWindowTests.createWindow(SettingsWindowTests.createSettings(), platform);
			var close = SettingsWindowTests.findButtons(window).Single(button => Equals(button.Content, "✕"));

			close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));

			var result = Assert.Single(platform.DialogResults);
			Assert.False(result.Result);
			Assert.Same(window, result.Window);
		});
	}

	[Fact]
	public void TitleBarMouseDown_DelegatesOnlyPressedDragsToTheInjectedPlatform () {
		WpfTestHost.Invoke(() => {
			var platform = new FakeWindowPlatform { LeftButtonPressed = true };
			var window = SettingsWindowTests.createWindow(SettingsWindowTests.createSettings(), platform);
			var border = (Border)window.FindName("WidgetBorder");
			border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			});
			platform.LeftButtonPressed = false;
			border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			});

			Assert.Equal(new[] { window }, platform.DraggedWindows);
		});
	}

	[Fact]
	public void HotkeyCapture_AcceptsTheSimultaneouslyHeldKeysAndEscapeRestoresTheSavedCombo () {
		WpfTestHost.Invoke(() => {
			var settings = SettingsWindowTests.createSettings();
			settings.Data.HotkeyCodes = [0xA4, 0x5B];
			var window = SettingsWindowTests.createWindow(settings, new FakeWindowPlatform());
			window.Show();
			SettingsWindowTests.activate(window);
			var capture = (Button)window.FindName("BtnCaptureHotkey");
			capture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, capture));
			Assert.Equal("(press keys…)", SettingsWindowTests.hotkeyDisplay(window).Text);
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.LeftShift);
			var suppressed = SettingsWindowTests.raiseKey(window, Keyboard.KeyDownEvent, Key.F2);
			var released = SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyUpEvent, Key.LeftShift);

			Assert.True(suppressed.Handled);
			Assert.True(released.Handled);
			Assert.Equal("L Ctrl + L Shift", SettingsWindowTests.hotkeyDisplay(window).Text);
			SettingsWindowTests.captureButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent, SettingsWindowTests.captureButton(window)));
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.Escape);

			Assert.Equal("L Ctrl + L Shift", SettingsWindowTests.hotkeyDisplay(window).Text);
			Assert.Equal("Change…", SettingsWindowTests.captureButton(window).Content);
			SettingsWindowTests.captureButton(window).RaiseEvent(new RoutedEventArgs(
				Button.ClickEvent, SettingsWindowTests.captureButton(window)));
			SettingsWindowTests.raiseKey(window, Keyboard.PreviewKeyDownEvent, Key.LeftCtrl);
			SettingsWindowTests.captureButton(window).RaiseEvent(new RoutedEventArgs(
				Button.ClickEvent, SettingsWindowTests.captureButton(window)));
			Assert.Equal("L Ctrl + L Shift", SettingsWindowTests.hotkeyDisplay(window).Text);
			window.Close();
		});
	}

	[Fact]
	public void NumericSettingsPreviewTextInput_BlocksNonDigitsAndAllowsDigits () {
		WpfTestHost.Invoke(() => {
			var window = SettingsWindowTests.createWindow(SettingsWindowTests.createSettings(), new FakeWindowPlatform());
			SettingsWindowTests.activate(window);
			TextBox[] numericFields = [
				SettingsWindowTests.historySize(window),
				(TextBox)window.FindName("TxtMaxOutstandingJobs"),
				(TextBox)window.FindName("TxtAudioBufferBudgetMiB"),
			];
			foreach (TextBox field in numericFields) {
				TextCompositionEventArgs invalid = SettingsWindowTests.createTextInput(window, "x");
				TextCompositionEventArgs valid = SettingsWindowTests.createTextInput(window, "7");
				field.RaiseEvent(invalid);
				field.RaiseEvent(valid);
				Assert.True(invalid.Handled);
				Assert.False(valid.Handled);
			}
		});
	}

	private class TestSettings : Settings {
		private static readonly Log _log = new();

		public TestSettings () : base(Path.Combine(AppContext.BaseDirectory, $"phase0-settings-window-{Guid.NewGuid():N}.json")) {
			this.log = SettingsWindowTests.TestSettings._log;
		}
	}
}