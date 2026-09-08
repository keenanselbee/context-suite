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

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolSettingsEditor editor }) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder", Multiselect = false };
        if (System.IO.Directory.Exists(editor.OutputDirectory)) dialog.InitialDirectory = editor.OutputDirectory;
        if (dialog.ShowDialog(this) == true) editor.OutputDirectory = dialog.FolderName;
    }
}
