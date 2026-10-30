using System.Windows;
using System.Diagnostics;

namespace ContextSuite.Application;

public partial class SettingsWindow : Window
{
    private readonly string _noticePath;
    private readonly Action<ProcessStartInfo> _openNotice;
    public SettingsWindow() : this(null, null) { }
    internal SettingsWindow(string? noticeDirectory, Action<ProcessStartInfo>? openNotice)
    {
        _noticePath = Path.Combine(noticeDirectory ?? AppContext.BaseDirectory, "ThirdPartyNotices.html");
        _openNotice = openNotice ?? (start => { using var process = Process.Start(start); });
        InitializeComponent();
        _ = new Infrastructure.ContentWindowSizing(this);
        Closing += (_, e) => e.Cancel = DataContext is SettingsViewModel { IsSaving: true };
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
            _openNotice(new ProcessStartInfo(_noticePath) { UseShellExecute = true });
            NoticeStatus.Text = "Third-party notices opened.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            NoticeStatus.Text = "The notices could not be opened. Open ThirdPartyNotices.html in the Context Suite application folder.";
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }


    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolSettingsEditor editor }) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder", Multiselect = false };
        if (System.IO.Directory.Exists(editor.OutputDirectory)) dialog.InitialDirectory = editor.OutputDirectory;
        if (dialog.ShowDialog(this) == true) editor.OutputDirectory = dialog.FolderName;
    }
}
