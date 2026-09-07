namespace ContextSuite.Application;

public partial class ConversionWindow : System.Windows.Window
{
    public ConversionWindow() { InitializeComponent(); }
    private void OnCancel(object sender, System.Windows.RoutedEventArgs e) { Close(); }
}
