using System.Runtime.InteropServices;
using System.Windows;

namespace ContextSuite.Application;

public partial class AnalyzeWindow : Window
{
    private bool _initialLayoutApplied;
    private double _expandedHeight = double.NaN;

    public static readonly DependencyProperty DetailsExpandedProperty = DependencyProperty.Register(
        nameof(DetailsExpanded), typeof(bool), typeof(AnalyzeWindow), new PropertyMetadata(true, OnDetailsExpandedChanged));
    public bool DetailsExpanded
    {
        get => (bool)GetValue(DetailsExpandedProperty);
        set => SetValue(DetailsExpandedProperty, value);
    }
    internal event Action<bool>? DetailsExpandedChanged;

    private static void OnDetailsExpandedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var window = (AnalyzeWindow)sender;
        if (window._initialLayoutApplied && window.WindowState == WindowState.Normal)
        {
            var expanded = (bool)e.NewValue;
            if (!expanded) window._expandedHeight = window.Height;
            Infrastructure.InitialWindowPlacement.ResizeForDetails(window, expanded, window._expandedHeight);
        }
        if (window.IsLoaded) window.DetailsExpandedChanged?.Invoke((bool)e.NewValue);
    }

    public AnalyzeWindow()
    {
        InitializeComponent();
        Loaded += OnInitialLayout;
    }

    private void OnInitialLayout(object sender, RoutedEventArgs e)
    {
        Loaded -= OnInitialLayout;
        Infrastructure.InitialWindowPlacement.Apply(this, DetailsExpanded);
        _initialLayoutApplied = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) { Close(); }

    private void OnCopyAnalysis(object sender, RoutedEventArgs e)
    {
        var report = ((MainViewModel)DataContext).AnalysisReport;
        if (string.IsNullOrEmpty(report)) return;
        try { Clipboard.SetText(report); InputNotice.Text = "Analysis details copied."; }
        catch (ExternalException) { InputNotice.Text = "The clipboard is busy. Try copying again."; }
    }

    private void OnShowAnalysisFile(object sender, RoutedEventArgs e)
    {
        if (((MainViewModel)DataContext).SelectedAnalysisRow is not { } row) return;
        nint item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(row.Path, 0, out item, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0));
            InputNotice.Text = "";
        }
        catch (Exception error) when (error is COMException or ArgumentException)
        { InputNotice.Text = "The file location could not be opened. Its path remains in the analysis details."; }
        finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint item, uint attributes, out uint foundAttributes);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(nint item, uint count, nint children, uint flags);

}
