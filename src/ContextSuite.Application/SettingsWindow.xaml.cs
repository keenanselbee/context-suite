using System.Windows;

namespace ContextSuite.Application;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Closing += (_, e) => e.Cancel = DataContext is SettingsViewModel { IsSaving: true };
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
