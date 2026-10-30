namespace ContextSuite.Application;

public partial class ConversionWindow : System.Windows.Window
{
    public ConversionWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }
    private void OnCancel(object sender, System.Windows.RoutedEventArgs e) { Close(); }
}
