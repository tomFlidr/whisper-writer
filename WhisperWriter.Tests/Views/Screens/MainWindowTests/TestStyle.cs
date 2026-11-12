using WhisperWriter.Services.MainWindows;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal class TestStyle : Style {
	public TestStyle () : base(new FakeNativeApi()) {
	}
}