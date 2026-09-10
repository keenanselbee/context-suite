using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;

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
        // The window is never loaded or shown, so this store and provider must
        // not be called. No production licensing state is touched.
        var licenseModel = new LicenseViewModel(new(new UnusedLicenseService(),
            new LicenseStore(Path.Combine(Path.GetTempPath(), "unused-license-view-contract.bin"), LicenseEnvironment.Sandbox)));
        Window[] windows = [new MainWindow(), new ConversionWindow(), new OptimizationWindow(), new SettingsWindow(), new LicenseWindow(licenseModel)];
        try
        {
            foreach (var window in windows)
            {
                Check(!window.IsVisible && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(window)),
                    window.GetType().Name + ": instantiated without showing UI");
                Check(window.SizeToContent == SizeToContent.Height,
                    window.GetType().Name + ": content controls height while width stays independent");
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(window.MinWidth - 40, window.MinHeight - 70));
                content.Arrange(new Rect(0, 0, window.MinWidth - 40, window.MinHeight - 70));
                content.UpdateLayout();
                Check(double.IsFinite(content.DesiredSize.Width) && double.IsFinite(content.DesiredSize.Height),
                    window.GetType().Name + ": minimum-size layout completes");
            }
            var main = windows[0]; var convert = windows[1]; var optimize = windows[2]; var settings = windows[3]; var license = windows[4];
            foreach (var (window, id) in new[] { (main, "BatchSummary"), (main, "RecoveryNotice"), (main, "InputNotice"),
                (convert, "ConversionPlanStatus"), (convert, "ConversionPreviewStatus"), (convert, "ConversionTrialStatus"),
                (optimize, "OptimizationPlanStatus"), (optimize, "OptimizationTrialStatus"), (settings, "SettingsMessage"), (license, "LicenseStatus") })
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
            foreach (var (window, id) in new[] { (main, "FileDetails"), (convert, "ConversionFileDetails"), (convert, "ConversionPreview"),
                (optimize, "OptimizationDetails"), (optimize, "OptimizationOutput") })
                Check(!Find<Expander>(window, id).IsExpanded, id + ": secondary controls start collapsed");
            Check(Find<DataGrid>(convert, "ConversionFiles").Columns.Count == 2 &&
                Find<DataGrid>(optimize, "OptimizationFiles").Columns.Count == 2, "planners: two-column file/plan summary");
            main.DataContext = new { IsLanding = true, HasResults = false, IsBusy = false, HasProblems = false, CanRetry = false };
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<Expander>(main, "FileDetails").Visibility == Visibility.Collapsed &&
                Find<Button>(main, "CancelPending").Visibility == Visibility.Collapsed &&
                Find<Button>(main, "RetrySelectedFiles").Visibility == Visibility.Collapsed,
                "idle: empty results and idle job controls stay hidden");
            main.DataContext = new { IsLanding = false, HasResults = true, IsBusy = true, HasProblems = true, CanRetry = true };
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<Expander>(main, "FileDetails").Visibility == Visibility.Visible &&
                Find<Button>(main, "CancelPending").Visibility == Visibility.Visible &&
                Find<Button>(main, "RetrySelectedFiles").Visibility == Visibility.Visible,
                "jobs: progress and problem controls become available");
            Check(!main.AllowDrop && !Descendants(main).Any(item => AutomationProperties.GetAutomationId(item) is "MoreTools" or "ChooseFiles" or "ExplorerHelp"),
                "utility: no manual picker, drop workspace or landing hub");
            Check(!Descendants(convert).Any(item => AutomationProperties.GetAutomationId(item) is "ConversionSizeQuality" or "ConversionAdvanced" or "ImageMetadataPolicy" or "ConfirmImageReplacement"),
                "conversion: fixed defaults and saved output choice need no routine controls");
            Check(Find<Button>(main, "CloseResults").IsCancel && Find<Button>(convert, "CancelImageConversion").IsCancel &&
                Find<Button>(optimize, "CancelPngOptimization").IsCancel && Find<Button>(settings, "CancelSettings").IsCancel,
                "results, decisions and settings: Escape close/cancellation retained");
            Check(!Find<Button>(convert, "ConfirmImageConversion").IsDefault &&
                !Find<Button>(optimize, "ConfirmPngOptimization").IsDefault,
                "media confirmation: typing Enter does not implicitly submit");
            Check(UIElementAutomationPeer.CreatePeerForElement(Find<ComboBox>(optimize, "PngOptimizationPreset"))?.GetName() ==
                "PNG optimization preset", "optimization: preset has an accessible name");
            optimize.DataContext = new { TrialMessage = "Your trial has expired. Activate a license to convert or optimize. Analyze remains available.",
                LicenseActionLabel = "_Activate license..." };
            var optimizationContent = (FrameworkElement)optimize.Content;
            optimizationContent.Measure(new Size(optimize.MinWidth - 40, optimize.MinHeight - 70));
            optimizationContent.Arrange(new Rect(0, 0, optimize.MinWidth - 40, optimize.MinHeight - 70));
            optimizationContent.UpdateLayout();
            var accessStatus = Find<StatusTextBlock>(optimize, "OptimizationTrialStatus");
            var optimizationBody = Find<ScrollViewer>(optimize, "OptimizationBody");
            var statusBounds = accessStatus.TransformToAncestor(optimizationContent).TransformBounds(new Rect(accessStatus.RenderSize));
            var bodyBounds = optimizationBody.TransformToAncestor(optimizationContent).TransformBounds(new Rect(optimizationBody.RenderSize));
            var confirmOptimize = Find<Button>(optimize, "ConfirmPngOptimization");
            var confirmBounds = confirmOptimize.TransformToAncestor(optimizationContent).TransformBounds(new Rect(confirmOptimize.RenderSize));
            Check(statusBounds.Height > 0 && statusBounds.Top >= bodyBounds.Bottom && statusBounds.Bottom <= confirmBounds.Top &&
                confirmBounds.Bottom <= optimizationContent.RenderSize.Height,
                "optimization: access explanation and actions stay outside scrolling content at minimum size");
            settings.DataContext = new SettingsViewModel(new SettingsStore(Path.Combine(Path.GetTempPath(), "unused-layout-settings.json")),
                new(new(), null, true, null), "convert", replacementAvailable: true);
            foreach (var section in new[] { 0, 1 })
            {
                settings.SizeToContent = SizeToContent.Manual;
                Find<TabControl>(settings, "SettingsSections").SelectedIndex = section;
                if (section == 1)
                    Check(settings.SizeToContent == SizeToContent.Height, "settings: changing tool tabs resumes height fitting");
                var content = (FrameworkElement)settings.Content;
                content.Measure(new Size(settings.Width - 16, double.PositiveInfinity));
                content.Arrange(new Rect(0, 0, settings.Width - 16, content.DesiredSize.Height));
                content.UpdateLayout();
                var body = Find<ScrollViewer>(settings, "SettingsBody");
                var overwrite = VisualDescendants(content).OfType<RadioButton>()
                    .Single(item => AutomationProperties.GetAutomationId(item) == "AllowReplacement");
                var bounds = overwrite.TransformToAncestor(body).TransformBounds(new Rect(overwrite.RenderSize));
                Check(overwrite.RenderSize.Height > 0 && bounds.Top >= 0 && bounds.Bottom <= body.ViewportHeight && body.ScrollableHeight == 0,
                    "settings: both output choices fit without scrolling in section " + section);
                Check(overwrite.Content.ToString() == "_Overwrite originals", "settings: clear overwrite label in section " + section);
                var heading = VisualDescendants(content).OfType<TextBlock>()
                    .Single(item => AutomationProperties.GetAutomationId(item) == "SettingsSectionHeading");
                Check(heading.Text == (section == 0 ? "Convert settings" : "Optimize settings"),
                    "settings: the active tool is identified inside its settings panel in section " + section);
                var copyOptions = VisualDescendants(content).OfType<Expander>()
                    .Single(item => AutomationProperties.GetAutomationId(item) == "CopyOptions");
                content.Measure(new Size(settings.Width - 16, double.PositiveInfinity));
                var collapsedHeight = content.DesiredSize.Height;
                copyOptions.IsExpanded = true;
                content.UpdateLayout();
                content.Measure(new Size(settings.Width - 16, double.PositiveInfinity));
                var expandedHeight = content.DesiredSize.Height;
                Check(settings.SizeToContent == SizeToContent.Height && expandedHeight > collapsedHeight + 100,
                    $"settings: opening copy options resumes height fitting after a manual resize in section {section} ({settings.SizeToContent}, {collapsedHeight} -> {expandedHeight})");
                settings.SizeToContent = SizeToContent.Manual;
                copyOptions.IsExpanded = false;
                content.UpdateLayout();
                content.Measure(new Size(settings.Width - 16, double.PositiveInfinity));
                Check(settings.SizeToContent == SizeToContent.Height && Math.Abs(content.DesiredSize.Height - collapsedHeight) < 1,
                    "settings: closing copy options restores compact content height in section " + section);
                copyOptions.IsExpanded = true;
                content.Measure(new Size(settings.MinWidth - 16, settings.MinHeight - 40));
                content.Arrange(new Rect(0, 0, settings.MinWidth - 16, settings.MinHeight - 40));
                content.UpdateLayout();
                var save = Find<Button>(settings, "SaveSettings");
                var saveBounds = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
                var bodyBoundsAtMinimum = body.TransformToAncestor(content).TransformBounds(new Rect(body.RenderSize));
                Check(body.ScrollableHeight > 0 && saveBounds.Top >= bodyBoundsAtMinimum.Bottom && saveBounds.Bottom <= content.RenderSize.Height,
                    "settings: expanded content scrolls while Save stays reachable at minimum size in section " + section);
                copyOptions.IsExpanded = false;
            }
            var licenseContent = (FrameworkElement)license.Content;
            licenseContent.Measure(new Size(license.Width - 16, double.PositiveInfinity));
            Check(license.SizeToContent == SizeToContent.Height && licenseContent.DesiredSize.Height < 440,
                "license: normal content sizes naturally without a large blank lower area");
            Check(Find<PasswordBox>(license, "LicenseKey").MaxLength == 512 &&
                UIElementAutomationPeer.CreatePeerForElement(Find<PasswordBox>(license, "LicenseKey"))?.GetName() == "License key",
                "license: bounded masked entry has an accessible name");
            Check(!Find<Button>(license, "ActivateLicense").IsDefault && Find<Button>(license, "CloseLicense").IsCancel,
                "license: Enter does not activate implicitly and Escape closes");
            var recovery = Find<StackPanel>(license, "LicenseRecovery");
            var confirmRecovery = Find<Button>(license, "ConfirmLicenseRecovery");
            Check(recovery.Visibility == Visibility.Collapsed && !confirmRecovery.IsEnabled,
                "license: recovery is absent until a recovery state is loaded");
            license.DataContext = new { ShowRecovery = true, CanRecover = true };
            recovery.UpdateLayout();
            Check(recovery.Visibility == Visibility.Visible && !confirmRecovery.IsEnabled,
                "license: recovery instructions appear directly but require confirmation");
            ((CheckBox)license.FindName("PortalConfirmed")).IsChecked = true;
            Check(confirmRecovery.IsEnabled, "license: portal confirmation enables recovery");
            license.DataContext = new { ShowRecovery = true, CanRecover = false };
            Check(recovery.Visibility == Visibility.Visible && !confirmRecovery.IsEnabled,
                "license: busy recovery keeps instructions visible and disables submission");
            license.DataContext = licenseModel;
            var results = Find<DataGrid>(main, "BatchResults");
            const string longName = "A long image filename with several descriptive words that should stay readable when the results window is narrow.png";
            results.ItemsSource = new[] { new { Name = longName, Outcome = "Needs attention", Savings = "—" } };
            Find<Expander>(main, "FileDetails").IsExpanded = true;
            var mainContent = (FrameworkElement)main.Content;
            foreach (var width in new[] { main.MinWidth - 16, main.Width - 16 })
            {
                mainContent.Measure(new Size(width, main.Height - 40));
                mainContent.Arrange(new Rect(0, 0, width, main.Height - 40));
                mainContent.UpdateLayout();
                Check(results.Columns.Max(column => column.ActualWidth) - results.Columns.Min(column => column.ActualWidth) < 1,
                    "results: columns share the available width equally at " + width);
                var filename = VisualDescendants(mainContent).OfType<TextBlock>().Single(item => item.Text == longName);
                Check(filename.ActualHeight > filename.FontSize * 2 && filename.ActualWidth <= results.Columns[0].ActualWidth,
                    "results: long filenames wrap inside their column at " + width);
            }
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

    private sealed class UnusedLicenseService : ILicenseService
    {
        public LicenseEnvironment Environment => LicenseEnvironment.Sandbox;
        public Task<LicenseReply> ActivateAsync(string key, Guid installationId, CancellationToken cancellationToken) => throw new InvalidOperationException("Hidden view must not activate.");
        public Task<LicenseReply> ValidateAsync(string key, Guid installationId, Guid activationId, CancellationToken cancellationToken) => throw new InvalidOperationException("Hidden view must not validate.");
        public Task<LicenseReply> DeactivateAsync(string key, Guid activationId, CancellationToken cancellationToken) => throw new InvalidOperationException("Hidden view must not deactivate.");
    }

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        var found = Descendants(root).OfType<T>().SingleOrDefault(item => AutomationProperties.GetAutomationId(item) == id);
        return found ?? throw new InvalidOperationException("Missing control: " + id);
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
