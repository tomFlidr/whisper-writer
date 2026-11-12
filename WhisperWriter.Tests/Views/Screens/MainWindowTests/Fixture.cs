using WhisperWriter.Tests.TestSupport;
using WhisperWriter.Views.Screens;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal record Fixture (
	MainWindow Window,
	TestSettings Settings,
	FakeWindowPlatform Platform,
	FakeMainWindowRuntime Runtime,
	TestPosition Position,
	FakeEnvironment PositionEnvironment
);