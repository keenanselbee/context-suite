using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ContextSuite.Application.Infrastructure;

namespace ContextSuite.Application.TestHost;

internal static class ViewContracts
{
    public static int Run()
    {
        // Instantiate production XAML without showing windows, starting the app,
        // creating trial data, sending input or attaching to another UI process.
        // Match the already-selected production App.xaml theme; no global theme change.
#pragma warning disable WPF0001
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System };
#pragma warning restore WPF0001
        var passed = 0;
        Window[] windows = [new MainWindow(), new ConversionWindow(), new OptimizationWindow(), new SettingsWindow()];
        try
        {
            foreach (var window in windows)
            {
                Check(!window.IsVisible && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(window)),
                    window.GetType().Name + ": instantiated without showing UI");
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(window.MinWidth - 40, window.MinHeight - 70));
                content.Arrange(new Rect(0, 0, window.MinWidth - 40, window.MinHeight - 70));
                content.UpdateLayout();
                Check(double.IsFinite(content.DesiredSize.Width) && double.IsFinite(content.DesiredSize.Height),
                    window.GetType().Name + ": minimum-size layout completes");
            }
            var main = windows[0]; var convert = windows[1]; var optimize = windows[2]; var settings = windows[3];
            foreach (var (window, id) in new[] { (main, "BatchSummary"), (main, "RecoveryNotice"), (main, "InputNotice"),
                (convert, "ConversionPlanStatus"), (convert, "ConversionPreviewStatus"),
                (optimize, "OptimizationPlanStatus"), (optimize, "OptimizationTrialStatus"), (settings, "SettingsMessage") })
            {
                var control = Find<StatusTextBlock>(window, id);
                control.Text = "A file needs attention.";
                var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                Check(peer?.GetName() == control.Text && peer.GetLiveSetting() == AutomationLiveSetting.Polite,
                    id + ": accessible live text matches the displayed message");
                control.Text = "Completed. Originals kept.";
                Check(peer!.GetName() == control.Text && !control.IsVisible,
                    id + ": updated text remains available without opening UI");
            }
            foreach (var (window, id) in new[] { (main, "FileDetails"), (main, "MoreTools"),
                (convert, "ConversionSizeQuality"), (convert, "ConversionAdvanced"),
                (optimize, "OptimizationDetails"), (optimize, "OptimizationOutput") })
                Check(!Find<Expander>(window, id).IsExpanded, id + ": secondary controls start collapsed");
            Check(Find<DataGrid>(convert, "ConversionFiles").Columns.Count == 2 &&
                Find<DataGrid>(optimize, "OptimizationFiles").Columns.Count == 2, "planners: two-column file/plan summary");
            Check(Find<Button>(convert, "CancelImageConversion").IsCancel &&
                Find<Button>(optimize, "CancelPngOptimization").IsCancel && Find<Button>(settings, "CancelSettings").IsCancel,
                "planners and settings: Escape cancellation retained");
            Check(!Find<Button>(convert, "ConfirmImageConversion").IsDefault &&
                !Find<Button>(optimize, "ConfirmPngOptimization").IsDefault,
                "media confirmation: typing Enter does not implicitly submit");
            Check(UIElementAutomationPeer.CreatePeerForElement(Find<ComboBox>(optimize, "PngOptimizationPreset"))?.GetName() ==
                "PNG optimization preset", "optimization: preset has an accessible name");
            Console.WriteLine($"Passed {passed} hidden view contracts. No desktop input, screen-reader delivery or visual acceptance tested.");
            return 0;
        }
        finally
        {
            foreach (var window in windows) window.Close();
            app.Shutdown();
        }

        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++; Console.WriteLine("PASS " + name);
        }
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        var found = Descendants(root).OfType<T>().SingleOrDefault(item => AutomationProperties.GetAutomationId(item) == id);
        return found ?? throw new InvalidOperationException("Missing control: " + id);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
