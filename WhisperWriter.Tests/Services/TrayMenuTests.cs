using WhisperWriter.Services;
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
		platform.RegisterWindow(TestTrayMenu.WindowKind.About, aboutWindow);
		platform.RegisterWindow(TestTrayMenu.WindowKind.History, historyWindow);
		var trayMenu = new TestTrayMenu(platform);

		platform.RequestAbout();
		platform.RequestHistory();

		Assert.Equal(new[] { TestTrayMenu.WindowKind.About, TestTrayMenu.WindowKind.History }, platform.ResolvedWindows);
		Assert.Equal(1, aboutWindow.ShowCount);
		Assert.Equal(1, aboutWindow.CloseCount);
		Assert.Equal(1, historyWindow.ShowCount);
		Assert.Equal(0, historyWindow.ShowDialogCount);
	}

	[Fact]
	public void SettingsRequest_WhenAccepted_SavesUpdatesHistoryAndReloadsHotkey () {
		var platform = new TestTrayMenu.FakePlatform();
		var settingsWindow = new TestTrayMenu.FakeWindow { DialogResult = true };
		platform.RegisterWindow(TestTrayMenu.WindowKind.Settings, settingsWindow);
		var trayMenu = new TestTrayMenu(platform);

		platform.RequestSettings();

		Assert.Equal(new[] { TestTrayMenu.WindowKind.Settings }, platform.ResolvedWindows);
		Assert.Equal(1, settingsWindow.ShowDialogCount);
		Assert.Equal(new[] { "settings-save", "history-size:17", "hotkey-reload" }, platform.Effects);
	}

	[Fact]
	public void SettingsRequest_WhenCancelled_DoesNotApplySettingsEffects () {
		var platform = new TestTrayMenu.FakePlatform();
		var settingsWindow = new TestTrayMenu.FakeWindow { DialogResult = false };
		platform.RegisterWindow(TestTrayMenu.WindowKind.Settings, settingsWindow);
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

	private class TestTrayMenu: TrayMenu {
		public enum WindowKind {
			About,
			History,
			Settings,
		}

		public TestTrayMenu (FakePlatform platform) : base(platform) {
		}

		public class FakePlatform: ITrayMenuPlatform {
			private readonly Dictionary<TrayWindowKind, FakeWindow> _windows = new();
			private Action? _about;
			private Action? _history;
			private Action? _settings;
			private Action? _exit;
			private Action? _doubleClick;

			public List<WindowKind> ResolvedWindows { get; } = new();
			public List<string> Effects { get; } = new();
			public Action? OnExit { get; set; }
			public int DisposeCount { get; private set; }

			IDisposable ITrayMenuPlatform.CreateTrayIcon (Action about, Action history, Action settings, Action exit, Action doubleClick) {
				this._about = about;
				this._history = history;
				this._settings = settings;
				this._exit = exit;
				this._doubleClick = doubleClick;
				return new FakeTrayIcon(this);
			}

			ITrayMenuWindow ITrayMenuPlatform.ResolveWindow (TrayWindowKind kind) {
				this.ResolvedWindows.Add((WindowKind)(int)kind);
				return this._windows[kind];
			}

			int ITrayMenuPlatform.HistorySize => 17;

			void ITrayMenuPlatform.SaveSettings () {
				this.Effects.Add("settings-save");
			}

			void ITrayMenuPlatform.SetHistorySize (int historySize) {
				this.Effects.Add($"history-size:{historySize}");
			}

			void ITrayMenuPlatform.ReloadHotkey () {
				this.Effects.Add("hotkey-reload");
			}

			void ITrayMenuPlatform.ExitApp () {
				this.Effects.Add("exit");
				this.OnExit?.Invoke();
			}

			void ITrayMenuPlatform.ShowMainWindow () {
				this.Effects.Add("main-window");
			}

			public void RegisterWindow (WindowKind kind, FakeWindow window) {
				this._windows[(TrayWindowKind)(int)kind] = window;
			}

			public void RequestAbout () => this._about?.Invoke();
			public void RequestHistory () => this._history?.Invoke();
			public void RequestSettings () => this._settings?.Invoke();
			public void RequestExit () => this._exit?.Invoke();
			public void RequestDoubleClick () => this._doubleClick?.Invoke();

			private class FakeTrayIcon: IDisposable {
				private readonly FakePlatform _platform;

				public FakeTrayIcon (FakePlatform platform) {
					this._platform = platform;
				}

				public void Dispose () {
					this._platform.DisposeCount++;
				}
			}
		}

		public class FakeWindow: ITrayMenuWindow {
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
