using System.Windows;

namespace WhisperWriter.Views;

public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();
	}

	private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		=> DragMove();

	private void BtnClose_Click(object sender, RoutedEventArgs e)
		=> Close();
}
