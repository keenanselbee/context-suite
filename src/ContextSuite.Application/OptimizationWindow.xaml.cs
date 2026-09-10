using System.Windows;

namespace ContextSuite.Application;

public partial class OptimizationWindow : Window
{
    public OptimizationWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }
    private void OnLicense(object sender, RoutedEventArgs e) { ((App)System.Windows.Application.Current).ShowLicense(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
}
