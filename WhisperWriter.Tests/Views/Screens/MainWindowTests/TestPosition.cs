using WhisperWriter.Services;
using WhisperWriter.Services.MainWindows;
using WhisperWriter.Services.MainWindows.Positions;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal class TestPosition : Position {
	public TestPosition (Settings settings, IEnvironment environment) : base(settings, environment) {
	}
}