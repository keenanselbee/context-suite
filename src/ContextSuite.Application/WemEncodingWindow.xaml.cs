using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace ContextSuite.Application;

public partial class WemEncodingWindow : Window
{
    public WemEncodingWindow()
    {
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
    }

    private async void OnLocateWwise(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate WwiseConsole.exe",
            Filter = "WwiseConsole.exe|WwiseConsole.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            await ((WemEncodingViewModel)DataContext).ChooseWwiseAsync(dialog.FileName);
    }

    private async void OnLocateOriginal(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate original WEM",
            Filter = "Wwise audio (*.wem)|*.wem",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            await ((WemEncodingViewModel)DataContext).ChooseReferenceAsync(dialog.FileName);
    }

    private void OnGetWwise(object sender, RoutedEventArgs e)
    {
        try
        {
            // Only an explicit click opens Audiokinetic's official download page.
            Process.Start(new ProcessStartInfo("https://www.audiokinetic.com/download") { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "The Wwise download page could not be opened. Visit audiokinetic.com/download in your browser.",
                "Context Suite", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
