namespace ContextSuite.Application;

public partial class ConversionWindow : System.Windows.Window
{
    public ConversionWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }
    private void OnLicense(object sender, System.Windows.RoutedEventArgs e) { ((App)System.Windows.Application.Current).ShowLicense(); }
    private void OnCancel(object sender, System.Windows.RoutedEventArgs e) { Close(); }
}
