using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using WhisperWriter.Models;
using WhisperWriter.Services;
using WhisperWriter.Services.MainWindows;
using WhisperWriter.Services.MainWindows.Positions;
using WhisperWriter.Tests.TestSupport;
using Xunit;

namespace WhisperWriter.Tests.Services.MainWindows;

public class PositionTests {
	private static void withWindow (Action<Window> action) {
		WpfTestHost.Invoke(() => {
			var application = Application.Current;
			var previousShutdownMode = application.ShutdownMode;
			application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			var window = new Window { Width = 200, Height = 100, Opacity = 0, ShowInTaskbar = false };
			try {
				window.Show();
				window.Hide();
				action(window);
			} finally {
				window.Close();
				application.ShutdownMode = previousShutdownMode;
			}
		});
	}

	private static void raiseLoaded (Window window) {
		window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, window));
	}

	private static TestSettings createSettings () {
		string path = Path.Combine(
			AppContext.BaseDirectory,
			"phase0-position-tests",
			$"{Guid.NewGuid():N}.json"
		);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		return new TestSettings(path);
	}

	private static void AssertPersistedPosition (TestSettings settings, double expectedLeft, double expectedBottom) {
		Assert.True(File.Exists(settings.Path));
		var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settings.Path));
		Assert.NotNull(saved);
		Assert.Equal(expectedLeft, saved.WindowLeft);
		Assert.Equal(expectedBottom, saved.WindowBottom);
	}

	[Fact]
	public void SetWindow_ReturnsTheSamePositionInstance () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			var position = new TestPosition(settings, new FakeEnvironment());

			Assert.Same(position, position.SetWindow(window));
		});
	}

	[Fact]
	public void ApplyStoredPosition_ConvertsWorkingAreaAnchorsUsingIndependentDpiScales () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			settings.Data.WindowLeft = 400;
			settings.Data.WindowBottom = 99.5;
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = new Rectangle(100, 50, 1000, 800),
				WorkingAreas = [new Rectangle(100, 50, 1000, 800)],
				PrimaryScale = (0.5, 0.75),
				WindowScale = (0.5, 0.75),
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.ApplyStoredPosition();

			Assert.Equal(350, window.Left);
			Assert.Equal(488, window.Top);
			Assert.Equal(400, settings.Data.WindowLeft);
			Assert.Equal(99.5, settings.Data.WindowBottom);
		});
	}

	[Fact]
	public void PlaceAtDefaultPosition_UsesPrimaryWorkingAreaAndPersistsAnchor () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = new Rectangle(100, 50, 1000, 800),
				WorkingAreas = [new Rectangle(100, 50, 1000, 800)],
				PrimaryScale = (0.5, 0.5),
				WindowScale = (0.5, 0.5),
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.PlaceAtDefaultPosition();

			Assert.Equal(200, window.Left);
			Assert.Equal(355, window.Top);
			AssertPersistedPosition(settings, 250, 20);
		});
	}

	[Fact]
	public void PlaceAtDefaultPosition_UsesSystemParametersFallbackWithoutPrimaryMonitor () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = null,
				WorkingAreas = [],
				PrimaryScreenWidth = 1920,
				PrimaryScreenHeight = 1080,
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.PlaceAtDefaultPosition();

			Assert.Equal(860, window.Left);
			Assert.Equal(960, window.Top);
			Assert.Equal(-1, settings.Data.WindowLeft);
			Assert.False(File.Exists(settings.Path));
		});
	}

	[Fact]
	public void ApplyStoredPosition_WithoutPrimaryMonitorFallsBackToSystemParametersPlacement () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			settings.Data.WindowLeft = 400;
			settings.Data.WindowBottom = 100;
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = null,
				WorkingAreas = [],
				PrimaryScreenWidth = 1920,
				PrimaryScreenHeight = 1080,
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.ApplyStoredPosition();

			Assert.Equal(860, window.Left);
			Assert.Equal(960, window.Top);
			Assert.Equal(400, settings.Data.WindowLeft);
			Assert.Equal(100, settings.Data.WindowBottom);
			Assert.False(File.Exists(settings.Path));
		});
	}

	[Fact]
	public void ClampWindowToScreen_UsesMonitorWithGreatestOverlapAndPersistsClampedPosition () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			window.Left = 450;
			window.Top = 390;
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = new Rectangle(0, 0, 1000, 800),
				WorkingAreas = [
					new Rectangle(0, 0, 1000, 800),
					new Rectangle(1000, 0, 1000, 800),
				],
				PrimaryScale = (0.5, 0.5),
				WindowScale = (0.5, 0.5),
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.ClampWindowToScreen();

			Assert.Equal(500, window.Left);
			Assert.Equal(300, window.Top);
			AssertPersistedPosition(settings, 600, 50);
		});
	}

	[Fact]
	public void SaveWindowPosition_WithoutPrimaryMonitorPersistsSystemParametersRelativeAnchor () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			window.Left = 50;
			window.Top = 100;
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = null,
				WorkingAreas = [],
				PrimaryScreenWidth = 1920,
				PrimaryScreenHeight = 1080,
			};
			var position = new TestPosition(settings, environment).SetWindow(window);

			position.SaveWindowPosition();

			AssertPersistedPosition(settings, 150, 930);
		});
	}

	[Fact]
	public void InitialPosition_StoredValuesApplyBeforeInvokingCallbackAndReturnTrue () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			settings.Data.WindowLeft = 400;
			settings.Data.WindowBottom = 99.5;
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = new Rectangle(100, 50, 1000, 800),
				WorkingAreas = [new Rectangle(100, 50, 1000, 800)],
				PrimaryScale = (0.5, 0.75),
				WindowScale = (0.5, 0.75),
			};
			var position = new TestPosition(settings, environment).SetWindow(window);
			int callbackCount = 0;
			double callbackLeft = double.NaN;
			double callbackTop = double.NaN;

			bool hasStoredPosition = position.InitialPosition(() => {
				callbackCount++;
				callbackLeft = window.Left;
				callbackTop = window.Top;
			});
			Assert.True(hasStoredPosition);
			Assert.Equal(0, callbackCount);
			PositionTests.raiseLoaded(window);

			Assert.Equal(1, callbackCount);
			Assert.Equal(350, callbackLeft);
			Assert.Equal(488, callbackTop);
		});
	}

	[Fact]
	public void InitialPosition_DefaultValuesApplyBeforeInvokingCallbackAndReturnFalse () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			var environment = new FakeEnvironment {
				PrimaryWorkingArea = new Rectangle(100, 50, 1000, 800),
				WorkingAreas = [new Rectangle(100, 50, 1000, 800)],
				PrimaryScale = (0.5, 0.5),
				WindowScale = (0.5, 0.5),
			};
			var position = new TestPosition(settings, environment).SetWindow(window);
			int callbackCount = 0;
			double callbackLeft = double.NaN;
			double callbackTop = double.NaN;

			bool hasStoredPosition = position.InitialPosition(() => {
				callbackCount++;
				callbackLeft = window.Left;
				callbackTop = window.Top;
			});
			Assert.False(hasStoredPosition);
			Assert.Equal(0, callbackCount);
			PositionTests.raiseLoaded(window);

			Assert.Equal(1, callbackCount);
			Assert.Equal(200, callbackLeft);
			Assert.Equal(355, callbackTop);
		});
	}

	[Fact]
	public void PrimaryScale_UsesTheInjectedDpiScale () {
		PositionTests.withWindow(window => {
			using var settings = PositionTests.createSettings();
			var environment = new FakeEnvironment { PrimaryScale = (0.5, 0.75) };
			var position = new TestPosition(settings, environment);
			position.SetWindow(window);

			Assert.Equal((0.5, 0.75), position.GetPrimaryScreenScale());
		});
	}

	private class TestPosition : Position {
		public TestPosition (Settings settings, IEnvironment environment) : base(settings, environment) {
		}

		public (double scaleX, double scaleY) GetPrimaryScreenScale () {
			return this.getPrimaryScreenScale();
		}
	}

	private class FakeEnvironment : IEnvironment {
		public Rectangle? PrimaryWorkingArea { get; init; }
		public IReadOnlyList<Rectangle> WorkingAreas { get; init; } = [];
		public (double scaleX, double scaleY) PrimaryScale { get; init; } = (1, 1);
		public (double scaleX, double scaleY) WindowScale { get; init; } = (1, 1);
		public double PrimaryScreenWidth { get; init; } = 1920;
		public double PrimaryScreenHeight { get; init; } = 1080;

		public Rectangle? GetPrimaryWorkingArea () => this.PrimaryWorkingArea;
		public IReadOnlyList<Rectangle> GetWorkingAreas () => this.WorkingAreas;
		public (double scaleX, double scaleY) GetPrimaryScreenScale (Window window) => this.PrimaryScale;
		public (double scaleX, double scaleY) GetWindowScale (Window window) => this.WindowScale;
	}

	private class TestSettings : Settings, IDisposable {
		public string Path => this._path;

		private static readonly Log _log = new();
		private readonly string _path;

		public TestSettings (string path) : base(path) {
			this._path = path;
			this.log = PositionTests.TestSettings._log;
		}

		public void Dispose () {
			if (File.Exists(this._path))
				File.Delete(this._path);
			string? directory = System.IO.Path.GetDirectoryName(this._path);
			if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
				Directory.Delete(directory);
		}
	}
}