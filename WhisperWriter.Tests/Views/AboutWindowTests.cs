using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views;
using Xunit;

namespace WhisperWriter.Tests.Views;

public class AboutWindowTests {
	[Fact]
	public void CloseButton_ClosesTheAboutWindowThroughItsXamlClickBinding () {
		WpfTestHost.Invoke(() => {
			var application = WpfTestHost.Application;
			var previousShutdownMode = application.ShutdownMode;
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var window = new AboutWindow { Opacity = 0, ShowInTaskbar = false };
			int closedCount = 0;
			window.Closed += (_, _) => closedCount++;
			try {
				window.Show();
				var button = AboutWindowTests.FindButtons(window)
					.Single(candidate => Equals(candidate.Content, "Close"));

				button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

				Assert.Equal("About WhisperWriter", window.Title);
				Assert.Equal(480, window.Width);
				Assert.Equal(360, window.Height);
				Assert.Equal(1, closedCount);
				Assert.False(window.IsVisible);
			} finally {
				if (window.IsVisible)
					window.Close();
				application.ShutdownMode = previousShutdownMode;
			}
		});
	}

	[Fact]
	public void CloseGlyph_ClosesTheAboutWindowThroughItsXamlClickBinding () {
		WpfTestHost.Invoke(() => {
			var application = WpfTestHost.Application;
			ShutdownMode previousShutdownMode = application.ShutdownMode;
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var window = new AboutWindow { Opacity = 0, ShowInTaskbar = false };
			int closedCount = 0;
			window.Closed += (_, _) => closedCount++;
			try {
				window.Show();
				var button = AboutWindowTests.FindButtons(window).Single(candidate => Equals(candidate.Content, "✕"));
				button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
				Assert.Equal(1, closedCount);
				Assert.False(window.IsVisible);
			} finally {
				if (window.IsVisible)
					window.Close();
				application.ShutdownMode = previousShutdownMode;
			}
		});
	}

	[Fact]
	public void TitleBarMouseDown_DelegatesDragToTheInjectedPlatform () {
		WpfTestHost.Invoke(() => {
			var platform = new FakeWindowPlatform();
			var window = new AboutWindow();
			typeof(AboutWindow).GetProperty("windowPlatform", BindingFlags.Instance | BindingFlags.NonPublic)!
				.SetValue(window, platform);
			var border = (Border)window.FindName("WidgetBorder");
			border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			});
			Assert.Equal(new[] { window }, platform.DraggedWindows);
		});
	}

	private static IEnumerable<Button> FindButtons (DependencyObject root) {
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int index = 0; index < count; index++) {
			DependencyObject child = VisualTreeHelper.GetChild(root, index);
			if (child is Button button)
				yield return button;
			foreach (Button descendant in AboutWindowTests.FindButtons(child))
				yield return descendant;
		}
	}
}