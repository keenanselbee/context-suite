using System.Diagnostics;
using System.Windows;

namespace ContextSuite.Application;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) { Close(); }

    private void OnLicense(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowLicense();

    private void OnRetrySelected(object sender, RoutedEventArgs e)
    {
        InputNotice.Text = ((MainViewModel)DataContext).RetryFailed();
    }

    private void OnOpenOutputFolder(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not FileRow row || string.IsNullOrEmpty(row.OutputPath))
        { InputNotice.Text = "Select a completed row with an output path first."; return; }
        var directory = Path.GetDirectoryName(row.OutputPath);
        if (directory is null || !Directory.Exists(directory)) { InputNotice.Text = "The output folder is no longer available."; return; }
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        { InputNotice.Text = "The output folder could not be opened. Its path remains in the result row."; }
    }
}
