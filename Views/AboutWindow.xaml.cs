using System.Windows;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Views;

	/// <summary>
	/// Simple informational about dialog.
	/// </summary>
public partial class AboutWindow : Window, IService, ITransient {
	/// <summary>
	/// Initializes UI components of the about window.
	/// </summary>
	public AboutWindow () {
		this.InitializeComponent();
	}

	private void _handleTitleBarMouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) => this.DragMove();

	private void _handleBtnCloseClick (object sender, RoutedEventArgs e) => this.Close();
}
