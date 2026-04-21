using System.Windows;
using WhisperWriter.Utils.Interfaces;

namespace WhisperWriter.Views;

public partial class AboutWindow : Window, IService, ITransient {
	public AboutWindow () {
		this.InitializeComponent();
	}

	private void _handleTitleBarMouseLeftButtonDown (object sender, System.Windows.Input.MouseButtonEventArgs e) => this.DragMove();

	private void _handleBtnCloseClick (object sender, RoutedEventArgs e) => this.Close();
}
