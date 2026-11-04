using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views;
using Xunit;

namespace WhisperWriter.Tests.Views;

public class HistoryWindowTests {
	[Fact]
	public void Activation_BindsTheObservableHistoryCollectionToTheList () {
		WpfTestHost.Invoke(() => {
			var history = new TranscriptionHistory();
			var newest = new TranscriptionEntry { Text = "newest" };
			var oldest = new TranscriptionEntry { Text = "oldest" };
			history.Add(oldest);
			history.Add(newest);
			var window = HistoryWindowTests.createWindow(history, new FakeWindowPlatform());

			HistoryWindowTests.activate(window);

			var list = (ListView)window.FindName("HistoryList");
			Assert.Same(history.Entries, list.ItemsSource);
			Assert.Equal(new[] { newest, oldest }, list.Items.Cast<TranscriptionEntry>());
		});
	}

	[Fact]
	public void CopyButton_UsesItsBoundEntryAndWritesTheTextThroughTheXamlBinding () {
		WpfTestHost.Invoke(() => {
			var application = WpfTestHost.Application;
			ShutdownMode previousShutdownMode = application.ShutdownMode;
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var entry = new TranscriptionEntry { Text = "copied from history", Duration = TimeSpan.FromSeconds(1.25) };
			var platform = new FakeWindowPlatform();
			var window = HistoryWindowTests.createWindow(new TranscriptionHistory(), platform);
			window.Opacity = 0;
			window.ShowInTaskbar = false;
			try {
				HistoryWindowTests.activate(window);
				((TranscriptionHistory)HistoryWindowTests.getProtectedProperty(window, "history")).Add(entry);
				window.Show();
				window.UpdateLayout();
				var list = (ListView)window.FindName("HistoryList");
				list.UpdateLayout();
				var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
				container.ApplyTemplate();
				window.UpdateLayout();
				var text = Assert.Single(HistoryWindowTests.findVisualDescendants<TextBox>(container));
				Assert.Equal(entry.Text, text.Text);
				var metadata = Assert.Single(
					HistoryWindowTests.findVisualDescendants<TextBlock>(container),
					block => block.Text.Contains(" • ", StringComparison.Ordinal)
				);
				var culture = metadata.Language.GetEquivalentCulture();
				Assert.Equal(
					string.Format(culture, "{0:HH:mm:ss}  •  {1:0.0}s", entry.Timestamp, entry.Duration.TotalSeconds),
					metadata.Text
				);
				var copy = HistoryWindowTests.findVisualDescendants<Button>(container)
					.Single(button => Equals(button.Content, "Copy"));

				copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, copy));

				Assert.Equal(new[] { "copied from history" }, platform.ClipboardTexts);
				Assert.Same(entry, copy.DataContext);
			} finally {
				if (window.IsVisible)
					window.Close();
				application.ShutdownMode = previousShutdownMode;
			}
		});
	}

	[Fact]
	public void TitleBarMouseDown_UsesTheInjectedPressedDecisionAndDragBoundary () {
		WpfTestHost.Invoke(() => {
			var platform = new FakeWindowPlatform { LeftButtonPressed = true };
			var window = HistoryWindowTests.createWindow(new TranscriptionHistory(), platform);
			var border = (Border)window.FindName("WidgetBorder");
			var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			};

			border.RaiseEvent(args);

			Assert.Equal(new[] { window }, platform.DraggedWindows);
			platform.LeftButtonPressed = false;
			border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) {
				RoutedEvent = UIElement.MouseLeftButtonDownEvent,
				Source = border,
			});
			Assert.Single(platform.DraggedWindows);
		});
	}

	[Fact]
	public void CloseButton_ClosesHistoryThroughItsXamlClickBinding () {
		WpfTestHost.Invoke(() => {
			var application = WpfTestHost.Application;
			ShutdownMode previousShutdownMode = application.ShutdownMode;
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var window = HistoryWindowTests.createWindow(new TranscriptionHistory(), new FakeWindowPlatform());
			window.Opacity = 0;
			window.ShowInTaskbar = false;
			int closedCount = 0;
			window.Closed += (_, _) => closedCount++;
			try {
				window.Show();
				var close = HistoryWindowTests.findVisualDescendants<Button>(window)
					.Single(button => Equals(button.Content, "✕"));

				close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));

				Assert.Equal(1, closedCount);
				Assert.False(window.IsVisible);
			} finally {
				if (window.IsVisible)
					window.Close();
				application.ShutdownMode = previousShutdownMode;
			}
		});
	}

	private static HistoryWindow createWindow (TranscriptionHistory history, IWindowPlatform platform) {
		var window = new HistoryWindow();
		HistoryWindowTests.setProtectedProperty(window, "history", history);
		HistoryWindowTests.setProtectedProperty(window, "windowPlatform", platform);
		return window;
	}

	private static void activate (HistoryWindow window) =>
		typeof(HistoryWindow).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(window, [EventArgs.Empty]);

	private static object getProtectedProperty (HistoryWindow window, string name) =>
		typeof(HistoryWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

	private static void setProtectedProperty (HistoryWindow window, string name, object value) =>
		typeof(HistoryWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

	private static IEnumerable<T> findVisualDescendants<T> (DependencyObject root) where T : DependencyObject {
		for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) {
			DependencyObject child = VisualTreeHelper.GetChild(root, index);
			if (child is T match)
				yield return match;
			foreach (T descendant in HistoryWindowTests.findVisualDescendants<T>(child))
				yield return descendant;
		}
	}
}