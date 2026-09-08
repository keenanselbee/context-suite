using System.Windows;

namespace ContextSuite.Application;

public partial class OptimizationWindow : Window
{
    public OptimizationWindow() { InitializeComponent(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
}
