using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application.TestHost;

internal static class AnalysisWindowContracts
{
    public static int Run(string output, string theme)
    {
        var root = Path.GetFullPath(output);
        if (!root.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        Directory.CreateDirectory(root);
#pragma warning disable WPF0001
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown,
            ThemeMode = theme == "light" ? ThemeMode.Light : ThemeMode.Dark };
#pragma warning restore WPF0001
        var checks = 0;
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidDataException(message);
            checks++; File.AppendAllText(Path.Combine(root, theme + "-checks.txt"), "PASS: " + message + Environment.NewLine);
        }
        var view = new MainViewModel(new WorkerClient(Path.Combine(root, "must-not-start.exe")));
        var row = new FileRow(1, "analyze", Path.Combine(root, "index.html"), new SuiteSettings().Capture("analyze"), "open-details");
        var sample = System.Text.Encoding.UTF8.GetBytes("<!doctype html>\n<html>\n<body>\n" +
            string.Concat(Enumerable.Range(1, 124).Select(index => $"  <p>Authored preview line {index}.</p>\n")) + "</body></html>\n");
        row.ApplyAnalysis(HeaderAnalyzer.Analyze(row.Path, sample, sample.Length));
        view.AnalysisRows.Add(row); view.SelectedAnalysisRow = row;
        var inspector = new AnalyzeWindow { DataContext = view, Left = 100, Top = 100 };
        var results = new MainWindow { DataContext = view };
#pragma warning disable WPF0001
        inspector.ThemeMode = app.ThemeMode;
        results.ThemeMode = app.ThemeMode;
#pragma warning restore WPF0001
        _ = new System.Windows.Interop.WindowInteropHelper(inspector).EnsureHandle();
        _ = new System.Windows.Interop.WindowInteropHelper(results).EnsureHandle();
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(inspector).Handle;
            var monitor = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            Check(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), "opening monitor work area is available");
            var scale = System.Windows.Interop.HwndSource.FromHwnd(handle)!.CompositionTarget.TransformFromDevice.M22;
            var workHeight = (monitor.Work.Bottom - monitor.Work.Top) * scale;
            inspector.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            GetWindowRect(handle, out var openingBounds);
            Check(Math.Abs(inspector.Height - workHeight * 0.82) < 1 &&
                Math.Abs((openingBounds.Top - monitor.Work.Top) - (monitor.Work.Bottom - openingBounds.Bottom)) < 3,
                "expanded opening uses 82 percent of usable height with balanced vertical spacing");
            var expandedHeight = inspector.Height;
            var openingTop = inspector.Top;
            var openingLeft = inspector.Left;
            var openingWidth = inspector.Width;
            inspector.DetailsExpanded = false;
            Check(inspector.Height == 570 && inspector.Top == openingTop && inspector.Left == openingLeft && inspector.Width == openingWidth,
                "collapsing details shrinks the window without changing its position or width");
            inspector.DetailsExpanded = true;
            Check(inspector.Height == expandedHeight && inspector.Top == openingTop,
                "reopening details restores the previous expanded height");
            inspector.Height = 700;
            inspector.DetailsExpanded = false;
            inspector.DetailsExpanded = true;
            Check(inspector.Height == 700, "disclosure round trip remembers a manually chosen expanded height");
            inspector.WindowState = WindowState.Maximized;
            var maximizedHeight = inspector.Height;
            inspector.DetailsExpanded = false;
            inspector.DetailsExpanded = true;
            Check(inspector.WindowState == WindowState.Maximized && inspector.Height == maximizedHeight,
                "disclosure changes preserve maximized state and restore geometry");
            inspector.WindowState = WindowState.Normal;
            var collapsed = new AnalyzeWindow { DataContext = view, Left = 100, Top = 100, DetailsExpanded = false };
            try
            {
                _ = new System.Windows.Interop.WindowInteropHelper(collapsed).EnsureHandle();
                collapsed.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Check(collapsed.Height == Math.Min(570, expandedHeight),
                    "remembered collapsed disclosure retains a compact opening height");
                Check(Math.Abs(collapsed.Top - openingTop) < 1,
                    "new collapsed and expanded inspectors open at the same top position");
                var collapsedTop = collapsed.Top;
                collapsed.DetailsExpanded = true;
                Check(collapsed.Top == collapsedTop && Math.Abs(collapsed.Height - expandedHeight) < 1,
                    "an inspector opened collapsed expands downward to the full preferred height without recentering");
            }
            finally { collapsed.Close(); }
            Layout(inspector, 720, 520);
            Check(!inspector.IsVisible && !results.IsVisible, "actual XAML is exercised without opening desktop windows");
            Check(Find<DataGrid>(inspector, "AnalysisFiles").Visibility == Visibility.Collapsed &&
                Find<TextBlock>(inspector, "AnalysisFilename").Text == "index.html",
                "single file shows its name directly without a results table");
            Check(row.AnalysisPresentation!.Highlights.Select(item => item.Label).SequenceEqual(new[] { "File size", "Lines", "Text encoding" }) &&
                row.AnalysisPresentation.Highlights[1].Value == "128" &&
                Find<DataGrid>(inspector, "AnalysisFiles").Columns.Count == 3 &&
                !Descendants(inspector).OfType<TextBlock>().Any(block => AutomationProperties.GetAutomationId(block) == "AnalysisEvidence"),
                "text summary prioritizes measured lines and encoding; identification has no prominent card, paragraph or column");
            Check(Find<Expander>(inspector, "AnalysisTechnicalDetails").IsExpanded &&
                !Descendants(inspector).OfType<Button>().Any(button => button.Content?.ToString()?.Contains("Settings") == true),
                "technical details start expanded and inspector has no settings clutter");
            Capture(inspector, Path.Combine(root, theme + "-single.png"));
            var details = Find<Expander>(inspector, "AnalysisTechnicalDetails");
            var header = (System.Windows.Controls.Primitives.ToggleButton)details.Template.FindName("DetailsHeader", details);
            Check(Math.Abs(header.ActualWidth - details.ActualWidth) < 2,
                "technical details retains its full-width themed disclosure header");
            header.IsChecked = false;
            Layout(inspector, 720, 520);
            Check(((Border)details.Template.FindName("DetailsBody", details)).Visibility == Visibility.Collapsed &&
                Descendants(inspector).OfType<ScrollViewer>().All(scroll => scroll.ScrollableHeight == 0),
                "closing details removes the body and obsolete scroll range in the first layout without animation delay");
            Check(!Find<Expander>(inspector, "AnalysisTechnicalDetails").IsExpanded,
                "header toggle closes details through the remembered disclosure binding");
            Find<Expander>(inspector, "AnalysisTechnicalDetails").IsExpanded = true;
            Layout(inspector, 720, 520);
            Check(((Border)details.Template.FindName("DetailsBody", details)).Visibility == Visibility.Visible &&
                !((Border)details.Template.FindName("DetailsBody", details)).HasAnimatedProperties,
                "opening details shows the body immediately without animated layout properties");
            Check(inspector.DetailsExpanded, "opening the disclosure updates the remembered window preference");
            var unknown = new FileRow(2, "analyze", Path.Combine(root, "mystery.bin"), row.Settings, "open-details");
            unknown.ApplyAnalysis(HeaderAnalyzer.Analyze(unknown.Path, [0, 255, 1], 3));
            var missing = new FileRow(2, "analyze", Path.Combine(root, "missing.bin"), row.Settings, "open-details");
            missing.ApplyResult(new(missing.Path, OperationState.Failed, "This file could not be found. Check whether it was moved or deleted, then select it again."));
            // A fresh binding reads the complete retained session, matching a reopened inspector.
            view.AnalysisRows.Add(unknown); view.AnalysisRows.Add(missing);
            inspector.DataContext = null; inspector.DataContext = view;
            Layout(inspector, 720, 720);
            Check(Find<DataGrid>(inspector, "AnalysisFiles").Visibility == Visibility.Visible &&
                Find<DataGrid>(inspector, "AnalysisFiles").Items.Count == 3,
                "multiple files show a comparison table including unavailable inputs");
            Capture(inspector, Path.Combine(root, theme + "-batch.png"));
            view.SelectedAnalysisRow = missing;
            Layout(inspector, 512, 590);
            Check(Find<StatusTextBlock>(inspector, "AnalysisWarnings").Text.Contains("moved or deleted"),
                "selecting an unreadable file immediately displays its actionable error");
            var close = Find<Button>(inspector, "CloseAnalysis");
            var content = (FrameworkElement)inspector.Content;
            var bounds = close.TransformToAncestor(content).TransformBounds(new Rect(close.RenderSize));
            Check(bounds.Bottom <= content.ActualHeight && bounds.Right <= content.ActualWidth,
                "minimum width preserves access to Close");
            Capture(inspector, Path.Combine(root, theme + "-unreadable.png"));
            view.SelectedAnalysisRow = row;
            Find<Expander>(inspector, "AnalysisTechnicalDetails").IsExpanded = true;
            Layout(inspector, 512, 590);
            Check(Descendants(inspector).OfType<ScrollViewer>().Any(scroll => scroll.ScrollableHeight > 0),
                "expanded technical details scroll at minimum width");
            Capture(inspector, Path.Combine(root, theme + "-details.png"));
            var movedTop = SystemParameters.WorkArea.Bottom + 40;
            inspector.Top = movedTop;
            inspector.Height = 600;
            inspector.DetailsExpanded = false;
            inspector.DetailsExpanded = true;
            inspector.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            inspector.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Check(inspector.Top == movedTop && inspector.Height == 600 && double.IsPositiveInfinity(inspector.MaxHeight) &&
                inspector.SizeToContent == SizeToContent.Manual,
                "movement and repeated Loaded do not recenter or impose a maximum height; disclosure restores the chosen height");
            inspector.Close();
            Check(results.DataContext == view && view.AnalysisRows.Count == 3,
                "closing inspector leaves shared results and analysis history intact");
            File.AppendAllText(Path.Combine(root, theme + "-checks.txt"), $"Passed {checks} checks." + Environment.NewLine);
            return 0;
        }
        finally
        {
            inspector.Close(); results.Close();
            view.DisposeAsync().AsTask().GetAwaiter().GetResult(); app.Shutdown();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    private static void Layout(Window window, double width, double height)
    {
        window.SizeToContent = SizeToContent.Manual;
        window.Width = width + 16;
        window.Height = height + 40;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
    }

    private static void Capture(Window window, string path)
    {
        // Let theme disclosure animations settle before capturing hidden XAML.
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        var content = (FrameworkElement)window.Content;
        const double padding = 24;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth + padding * 2), (int)Math.Ceiling(content.ActualHeight + padding * 2), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(padding, padding, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(background);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(item => AutomationProperties.GetAutomationId(item) == id);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
}
