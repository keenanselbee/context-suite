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
            Filter = "Supported images (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tga;*.dds)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tga;*.dds|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnChooseAnalysisFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose DDS files to analyze", Multiselect = true, CheckFileExists = true,
            Filter = "DDS textures (*.dds)|*.dds|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames, "analyze");
    }

    private void OnChooseOptimizationFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose PNG files to optimize", Multiselect = true, CheckFileExists = true,
            Filter = "PNG images (*.png)|*.png|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames, "optimize");
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
        e.Handled = true;
    }

    private void OnRetrySelected(object sender, RoutedEventArgs e)
    {
        InputNotice.Text = ((MainViewModel)DataContext).RetryFailed();
    }

    private void AddFiles(IEnumerable<string> paths, string operation = "convert")
    {
        try
        {
            var selection = paths.Take(OperationRequest.MaximumPaths + 1).Select(Path.GetFullPath).ToImmutableArray();
            var action = operation switch { "analyze" => "open-details", "optimize" => "choose-preset", _ => "choose-format" };
            var reply = ((MainViewModel)DataContext).Admit(new(Guid.NewGuid(), operation, action, selection));
            InputNotice.Text = reply.Accepted ? (operation == "analyze" ? "Read-only analysis started. Select a result row for its details." :
                "Selection received as a new batch. Review its planning window when ready.") : reply.Message;
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
