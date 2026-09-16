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
        Window[] windows = [new MainWindow(), new ConversionWindow(), new OptimizationWindow(), new SettingsWindow(), new LicenseWindow(licenseModel), new AudioConversionWindow(), new ImagePdfOrderWindow(), new OfficeCalculationWindow()];
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
            var recoveryModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-office-view-worker.exe")));
            main.DataContext = recoveryModel;
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            var recoveryReport = new OfficeRecoveryReport("Retained Office files", [new("record", OfficeRecoveryState.ReviewRequired)]);
            recoveryModel.SetOfficeRecoveryReport(recoveryReport);
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<StatusTextBlock>(main, "RecoveryNotice").Text == recoveryReport.Notice && !main.IsVisible,
                "Office restart notice updates the existing hidden results view");
            recoveryModel.SetOfficeRecoveryReport(new("Retained Office files", []));
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<StatusTextBlock>(main, "RecoveryNotice").Text.Length == 0 && !main.IsVisible,
                "Office empty restart report adds no visible status or window");
            recoveryModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var audio = windows[5];
            passed += ImagePdfOrderViewContracts.Run(windows[6]);
            var calculation = (OfficeCalculationWindow)windows[7];
            Check(calculation.Calculation is null && !Find<Button>(calculation, "OfficeSavedValues").IsDefault &&
                !Find<Button>(calculation, "OfficeRecalculate").IsDefault && Find<Button>(calculation, "OfficeCalculationCancel").IsCancel,
                "Office spreadsheet choice: no implicit policy or default confirmation; Escape is declared as cancellation");
            Find<Button>(calculation, "OfficeSavedValues").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(calculation.Calculation == "cached", "Office spreadsheet choice: saved-values button returns its explicit policy");
            var recalculate = new OfficeCalculationWindow();
            Find<Button>(recalculate, "OfficeRecalculate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(recalculate.Calculation == "recalculate", "Office spreadsheet choice: recalculate button returns its explicit policy");
            Check(!Find<Expander>(audio, "AudioConversionFiles").IsExpanded && Find<Button>(audio, "CancelAudioConversion").IsCancel &&
                !Find<Button>(audio, "ConfirmAudioConversion").IsDefault,
                "audio decision: files start collapsed; Escape cancels and Enter does not implicitly confirm");
            audio.DataContext = new { Heading = "Convert to Opus", FileSummary = "4096 audio files", QualityChanges = "Quality loss and resampling require a choice.",
                FileNames = "One.mp3\nTwo.mp3", OutputSummary = "Originals are kept.", Message = "Activate your license to convert these files.", LicenseActionLabel = "_Activate license…" };
            var audioContent = (FrameworkElement)audio.Content;
            audioContent.Measure(new Size(audio.Width - 16, double.PositiveInfinity));
            audioContent.Arrange(new Rect(0, 0, audio.Width - 16, audioContent.DesiredSize.Height));
            audioContent.UpdateLayout();
            Check(Find<TextBlock>(audio, "AudioQualityChanges").Text.Contains("resampling") &&
                (string)Find<Button>(audio, "AudioConversionLicense").Content == "_Activate license…",
                "audio decision: explicit quality text and activation action bind to the fixed plan");
            var audioFiles = Find<Expander>(audio, "AudioConversionFiles");
            var audioCollapsedHeight = audioContent.DesiredSize.Height;
            audio.SizeToContent = SizeToContent.Manual;
            audioFiles.IsExpanded = true;
            audioContent.UpdateLayout();
            audioContent.Measure(new Size(audio.Width - 16, double.PositiveInfinity));
            Check(audio.SizeToContent == SizeToContent.Height && audioContent.DesiredSize.Height > audioCollapsedHeight,
                $"audio decision: expanding files resumes height fitting after manual resizing ({audioCollapsedHeight} -> {audioContent.DesiredSize.Height})");
            audioContent.Measure(new Size(audio.MinWidth - 16, audio.MinHeight - 40));
            audioContent.Arrange(new Rect(0, 0, audio.MinWidth - 16, audio.MinHeight - 40));
            audioContent.UpdateLayout();
            var audioBody = Find<ScrollViewer>(audio, "AudioConversionBody");
            var audioStatus = Find<StatusTextBlock>(audio, "AudioConversionStatus");
            var audioConfirm = Find<Button>(audio, "ConfirmAudioConversion");
            var audioBodyBounds = audioBody.TransformToAncestor(audioContent).TransformBounds(new Rect(audioBody.RenderSize));
            var audioStatusBounds = audioStatus.TransformToAncestor(audioContent).TransformBounds(new Rect(audioStatus.RenderSize));
            var audioConfirmBounds = audioConfirm.TransformToAncestor(audioContent).TransformBounds(new Rect(audioConfirm.RenderSize));
            Check(audioBody.ScrollableHeight > 0 && audioStatusBounds.Top >= audioBodyBounds.Bottom &&
                audioStatusBounds.Bottom <= audioConfirmBounds.Top && audioConfirmBounds.Bottom <= audioContent.RenderSize.Height,
                "audio decision: status and confirmation remain reachable while expanded content scrolls at minimum size");
            audio.SizeToContent = SizeToContent.Manual;
            audioFiles.IsExpanded = false;
            audioContent.UpdateLayout();
            audioContent.Measure(new Size(audio.Width - 16, double.PositiveInfinity));
            Check(audio.SizeToContent == SizeToContent.Height && Math.Abs(audioContent.DesiredSize.Height - audioCollapsedHeight) < 1,
                "audio decision: closing files restores compact content height");
            foreach (var (window, id) in new[] { (main, "BatchSummary"), (main, "RecoveryNotice"), (main, "InputNotice"),
                (convert, "ConversionPlanStatus"), (convert, "ConversionPreviewStatus"), (convert, "ConversionTrialStatus"),
                (optimize, "OptimizationPlanStatus"), (optimize, "OptimizationTrialStatus"), (settings, "SettingsMessage"), (license, "LicenseStatus"), (audio, "AudioConversionStatus"), (windows[6], "ImagePdfOrderStatus") })
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
            var analysisRow = new FileRow(1, "analyze", @"C:\fixture.pdf", new("analyze", new()));
            analysisRow.ApplyAnalysis(Core.Analysis.HeaderAnalyzer.Analyze(analysisRow.Path, "%PDF-1.7\n"u8, 9));
            main.DataContext = new { HasResults = true, ShowDetails = true, DisplayRows = new[] { analysisRow } };
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(results.SelectedItem == analysisRow && Find<TextBlock>(main, "AnalysisSummary").Text.Contains("PDF document"),
                "analysis: first selected file shows its ordinary summary without another click");
            Check(!Find<Expander>(main, "AnalysisTechnicalDetails").IsExpanded &&
                Find<TextBlock>(main, "AnalysisFacts").Text.Contains("document objects were not parsed"),
                "analysis: technical evidence is bound but starts collapsed");
            var scriptRow = new FileRow(2, "analyze", @"C:\example.py", new("analyze", new()));
            scriptRow.ApplyAnalysis(Core.Analysis.HeaderAnalyzer.Analyze(scriptRow.Path, "Readable text"u8, 13));
            results.ItemsSource = new[] { scriptRow };
            results.SelectedItem = scriptRow;
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<TextBlock>(main, "AnalysisSummary").Text.Contains("Python source (filename hint)") &&
                !Find<Expander>(main, "AnalysisTechnicalDetails").IsExpanded,
                "analysis: ordinary report labels filename-only identity without opening technical details");
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
