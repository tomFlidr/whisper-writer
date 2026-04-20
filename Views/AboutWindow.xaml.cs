using System.Windows;

namespace WhisperWriter.Views;

public partial class AboutWindow : Window {
	public AboutWindow () {
		this.InitializeComponent();
	}

	private void _handleTitleBarMouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) => this.DragMove();

	private void _handleBtnCloseClick (object sender, RoutedEventArgs e) => this.Close();
}
