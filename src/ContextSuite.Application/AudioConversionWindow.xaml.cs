using System.Windows;

namespace ContextSuite.Application;

public partial class AudioConversionWindow : Window
{
    public AudioConversionWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }
    private void OnLicense(object sender, RoutedEventArgs e) { ((App)System.Windows.Application.Current).ShowLicense(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Close(); }
}
