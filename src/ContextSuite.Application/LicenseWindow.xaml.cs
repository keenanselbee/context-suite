using System.Diagnostics;
using System.Windows;

namespace ContextSuite.Application;

public partial class LicenseWindow : Window
{
    private readonly string _noticePath;
    private readonly Action<ProcessStartInfo> _openExternal;

    internal LicenseWindow(LicenseViewModel model, string? noticeDirectory = null, Action<ProcessStartInfo>? openNotice = null)
    {
        InitializeComponent(); DataContext = model;
        _noticePath = Path.Combine(noticeDirectory ?? AppContext.BaseDirectory, "ThirdPartyNotices.html");
        _openExternal = openNotice ?? (start =>
        {
            using var process = Process.Start(start);
        });
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
    private void OnPurchase(object sender, RoutedEventArgs e) => OpenLink(((LicenseViewModel)DataContext).PurchaseUrl);
    private void OnManageLicense(object sender, RoutedEventArgs e) => OpenLink(((LicenseViewModel)DataContext).PortalUrl);

    private void OpenLink(Uri? address)
    {
        if (address is not { IsAbsoluteUri: true, Scheme: "https", UserInfo.Length: 0 })
        {
            LinkStatus.Text = "This link is unavailable in this build.";
            return;
        }
        try
        {
            _openExternal(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            LinkStatus.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LinkStatus.Text = $"The browser could not be opened. Open this address in your browser: {address.AbsoluteUri}";
        }
    }
    private void OnNotices(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_noticePath))
        {
            NoticeStatus.Text = "The bundled notices are missing. Restore the Context Suite application files and try again.";
            return;
        }
        try
        {
            _openExternal(new ProcessStartInfo(_noticePath) { UseShellExecute = true });
            NoticeStatus.Text = "Third-party notices opened.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            NoticeStatus.Text = "The notices could not be opened. Open ThirdPartyNotices.html in the Context Suite application folder.";
        }
    }
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
