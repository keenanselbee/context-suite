using System.Windows;

namespace ContextSuite.Application;

public partial class OfficeCalculationWindow : Window
{
    internal string? Calculation { get; private set; }
    public OfficeCalculationWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
        Loaded += (_, _) => SavedValues.Focus();
    }
    private void OnSaved(object sender, RoutedEventArgs e) { Calculation = "cached"; Close(); }
    private void OnRecalculate(object sender, RoutedEventArgs e) { Calculation = "recalculate"; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Close(); }
}
