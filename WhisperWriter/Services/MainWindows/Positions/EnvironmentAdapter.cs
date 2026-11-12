using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace WhisperWriter.Services.MainWindows.Positions;

public class EnvironmentAdapter : IEnvironment {
	public double PrimaryScreenWidth => SystemParameters.PrimaryScreenWidth;
	public double PrimaryScreenHeight => SystemParameters.PrimaryScreenHeight;

	public Rectangle? GetPrimaryWorkingArea () {
		return Screen.PrimaryScreen?.WorkingArea;
	}

	public IReadOnlyList<Rectangle> GetWorkingAreas () {
		return Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
	}

	public (double scaleX, double scaleY) GetPrimaryScreenScale (Window window) {
		var source = PresentationSource.FromVisual(window);
		if (source?.CompositionTarget != null)
			return (source.CompositionTarget.TransformFromDevice.M11,
				source.CompositionTarget.TransformFromDevice.M22);

		double dpiScale = this.PrimaryScreenWidth / Screen.PrimaryScreen!.Bounds.Width;
		return (dpiScale, dpiScale);
	}

	public (double scaleX, double scaleY) GetWindowScale (Window window) {
		var source = PresentationSource.FromVisual(window);
		if (source?.CompositionTarget != null)
			return (source.CompositionTarget.TransformFromDevice.M11,
				source.CompositionTarget.TransformFromDevice.M22);
		return (1.0, 1.0);
	}
}