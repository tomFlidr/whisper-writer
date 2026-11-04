using System.Drawing;
using System.Windows;

namespace WhisperWriter.Services.MainWindows;

public interface IPositionEnvironment {
	Rectangle? GetPrimaryWorkingArea ();
	IReadOnlyList<Rectangle> GetWorkingAreas ();
	(double scaleX, double scaleY) GetPrimaryScreenScale (Window window);
	(double scaleX, double scaleY) GetWindowScale (Window window);
	double PrimaryScreenWidth { get; }
	double PrimaryScreenHeight { get; }
}