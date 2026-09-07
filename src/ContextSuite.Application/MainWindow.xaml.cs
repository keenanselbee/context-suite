using System.Collections.Immutable;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() { InitializeComponent(); }

    private void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose images to convert", Multiselect = true, CheckFileExists = true,
            Filter = "Supported images (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tga|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
        e.Handled = true;
    }

    private void OnRetrySelected(object sender, RoutedEventArgs e)
    {
        var rows = ResultsGrid.SelectedItems.Cast<FileRow>().Where(row => row.Operation == "convert").ToArray();
        if (rows.Length == 0) { InputNotice.Text = "Select conversion rows to retry their original files with a new plan."; return; }
        AddFiles(rows.Select(row => row.Path));
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        try
        {
            var selection = paths.Take(OperationRequest.MaximumPaths + 1).Select(Path.GetFullPath).ToImmutableArray();
            var reply = ((MainViewModel)DataContext).Admit(new(Guid.NewGuid(), "convert", "choose-format", selection));
            InputNotice.Text = reply.Accepted ? "Selection received as a new batch. Review its conversion window when ready." : reply.Message;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException)
        { InputNotice.Text = "Could not add this selection. Use existing files only (no folders), up to 4,096 per batch."; }
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
