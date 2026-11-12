using DrawingRectangle = System.Drawing.Rectangle;
using System.Windows;
using WhisperWriter.Services.MainWindows.Positions;

namespace WhisperWriter.Tests.Views.Screens.MainWindowTests;

internal class FakeEnvironment : IEnvironment {
	public int PrimaryAreaReads { get; private set; }
	public double PrimaryScreenWidth => 1920;
	public double PrimaryScreenHeight => 1080;

	private static readonly DrawingRectangle _area = new(0, 0, 1920, 1080);

	public DrawingRectangle? GetPrimaryWorkingArea () {
		this.PrimaryAreaReads++;
		return FakeEnvironment._area;
	}

	public IReadOnlyList<DrawingRectangle> GetWorkingAreas () => [FakeEnvironment._area];
	public (double scaleX, double scaleY) GetPrimaryScreenScale (Window window) => (1, 1);
	public (double scaleX, double scaleY) GetWindowScale (Window window) => (1, 1);
	public void ResetCounts () => this.PrimaryAreaReads = 0;
}