using System.Drawing;
using System.Windows;

namespace WhisperWriter.Services.MainWindows.Positions;

public interface IEnvironment {
	double PrimaryScreenWidth { get; }
	double PrimaryScreenHeight { get; }

	Rectangle? GetPrimaryWorkingArea ();
	IReadOnlyList<Rectangle> GetWorkingAreas ();
	(double scaleX, double scaleY) GetPrimaryScreenScale (Window window);
	(double scaleX, double scaleY) GetWindowScale (Window window);
}