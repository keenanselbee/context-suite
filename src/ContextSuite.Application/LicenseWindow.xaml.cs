using System.Windows;

namespace ContextSuite.Application;

public partial class LicenseWindow : Window
{
    internal LicenseWindow(LicenseViewModel model)
    {
        InitializeComponent(); DataContext = model;
        _ = new Infrastructure.ContentWindowSizing(this);
        Loaded += async (_, _) => await model.LoadAsync();
        Closed += (_, _) => { LicenseKey.Clear(); model.Dispose(); };
    }

    private async void OnActivate(object sender, RoutedEventArgs e)
    {
        var key = LicenseKey.Password;
        LicenseKey.Clear();
        await ((LicenseViewModel)DataContext).ActivateAsync(key);
    }
    private void OnClose(object sender, RoutedEventArgs e) { Close(); }
    private async void OnValidate(object sender, RoutedEventArgs e) => await ((LicenseViewModel)DataContext).ValidateAsync();
    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Deactivate this installation so your license can be used elsewhere? Already admitted work will finish.",
            "Deactivate license", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await ((LicenseViewModel)DataContext).DeactivateAsync();
    }
    private async void OnRecover(object sender, RoutedEventArgs e)
    {
        await ((LicenseViewModel)DataContext).RecoverAsync(PortalConfirmed.IsChecked == true);
        PortalConfirmed.IsChecked = false;
    }
}
