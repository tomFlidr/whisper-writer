using WhisperWriter.Services;
using WhisperWriter.Services.TrayMenus;
using Xunit;

namespace WhisperWriter.Tests.Services;

public class TrayMenuTests {
	[Fact]
	public void DisposeAndAppExit_DisposeTrayIconOnlyOnce () {
		var platform = new TestTrayMenu.FakePlatform();
		var trayMenu = new TestTrayMenu(platform);

		trayMenu.Dispose();
		trayMenu.Dispose();
		trayMenu.HandleAppExit();
		trayMenu.HandleAppExit();

		Assert.Equal(1, platform.DisposeCount);
	}

	[Fact]
	public void AboutAndHistoryRequests_ResolveAndShowWindowsClosingThePreviousWindow () {
		var platform = new TestTrayMenu.FakePlatform();
		var aboutWindow = new TestTrayMenu.FakeWindow();
		var historyWindow = new TestTrayMenu.FakeWindow();
		platform.RegisterWindow(WindowKind.About, aboutWindow);
		platform.RegisterWindow(WindowKind.History, historyWindow);
		var trayMenu = new TestTrayMenu(platform);

		platform.RequestAbout();
		platform.RequestHistory();

		Assert.Equal(new[] { WindowKind.About, WindowKind.History }, platform.ResolvedWindows);
		Assert.Equal(1, aboutWindow.ShowCount);
		Assert.Equal(1, aboutWindow.CloseCount);
		Assert.Equal(1, historyWindow.ShowCount);
		Assert.Equal(0, historyWindow.ShowDialogCount);
	}

	[Fact]
	public void SettingsRequest_WhenAccepted_SavesUpdatesHistoryAndReloadsHotkey () {
		var platform = new TestTrayMenu.FakePlatform();
		var settingsWindow = new TestTrayMenu.FakeWindow { DialogResult = true };
		platform.RegisterWindow(WindowKind.Settings, settingsWindow);
		var trayMenu = new TestTrayMenu(platform);

		platform.RequestSettings();

		Assert.Equal(new[] { WindowKind.Settings }, platform.ResolvedWindows);
		Assert.Equal(1, settingsWindow.ShowDialogCount);
		Assert.Equal(new[] { "settings-save", "history-size:17", "hotkey-reload" }, platform.Effects);
	}

	[Fact]
	public void SettingsRequest_WhenCancelled_DoesNotApplySettingsEffects () {
		var platform = new TestTrayMenu.FakePlatform();
		var settingsWindow = new TestTrayMenu.FakeWindow { DialogResult = false };
		platform.RegisterWindow(WindowKind.Settings, settingsWindow);
		var trayMenu = new TestTrayMenu(platform);

		platform.RequestSettings();

		Assert.Equal(1, settingsWindow.ShowDialogCount);
		Assert.Empty(platform.Effects);
	}

	[Fact]
	public void ExitAndDoubleClickRequests_RouteToInjectedActions () {
		var platform = new TestTrayMenu.FakePlatform();
		var trayMenu = new TestTrayMenu(platform);
		platform.OnExit = trayMenu.HandleAppExit;

		platform.RequestExit();
		platform.RequestDoubleClick();
		platform.RequestExit();

		Assert.Equal(new[] { "exit", "main-window", "exit" }, platform.Effects);
		Assert.Equal(1, platform.DisposeCount);
	}

	private class TestTrayMenu : TrayMenu {
		public TestTrayMenu (FakePlatform platform) : base(platform) {
		}

		public class FakePlatform : IPlatform {
			public List<WindowKind> ResolvedWindows { get; } = new();
			public List<string> Effects { get; } = new();
			public Action? OnExit { get; set; }
			public int DisposeCount { get; private set; }

			int IPlatform.HistorySize => 17;

			private readonly Dictionary<WindowKind, FakeWindow> _windows = new();
			private Action? _about;
			private Action? _history;
			private Action? _settings;
			private Action? _exit;
			private Action? _doubleClick;

			public void RegisterWindow (WindowKind kind, FakeWindow window) {
				this._windows[kind] = window;
			}

			public void RequestAbout () => this._about?.Invoke();
			public void RequestHistory () => this._history?.Invoke();
			public void RequestSettings () => this._settings?.Invoke();
			public void RequestExit () => this._exit?.Invoke();
			public void RequestDoubleClick () => this._doubleClick?.Invoke();

			IDisposable IPlatform.CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick) {
				this._about = about;
				this._history = history;
				this._settings = settings;
				this._exit = exit;
				this._doubleClick = doubleClick;
				return new FakeTrayIcon(this);
			}

			IWindow IPlatform.ResolveWindow (WindowKind kind) {
				this.ResolvedWindows.Add(kind);
				return this._windows[kind];
			}

			void IPlatform.SaveSettings () {
				this.Effects.Add("settings-save");
			}

			void IPlatform.SetHistorySize (int historySize) {
				this.Effects.Add($"history-size:{historySize}");
			}

			void IPlatform.ReloadHotkey () {
				this.Effects.Add("hotkey-reload");
			}

			void IPlatform.ExitApp () {
				this.Effects.Add("exit");
				this.OnExit?.Invoke();
			}

			void IPlatform.ShowMainWindow () {
				this.Effects.Add("main-window");
			}

			private class FakeTrayIcon : IDisposable {
				private readonly FakePlatform _platform;

				public FakeTrayIcon (FakePlatform platform) {
					this._platform = platform;
				}

				public void Dispose () {
					this._platform.DisposeCount++;
				}
			}
		}

		public class FakeWindow : IWindow {
			public event EventHandler? Closed;
			public bool? DialogResult { get; set; }
			public int CloseCount { get; private set; }
			public int ShowCount { get; private set; }
			public int ShowDialogCount { get; private set; }

			public void Close () {
				this.CloseCount++;
				this.Closed?.Invoke(this, EventArgs.Empty);
			}

			public void Show () {
				this.ShowCount++;
			}

			public bool? ShowDialog () {
				this.ShowDialogCount++;
				return this.DialogResult;
			}
		}
	}
}