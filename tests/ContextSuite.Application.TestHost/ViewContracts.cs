using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

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
        Window[] windows = [new MainWindow(), new ConversionWindow(), new OptimizationWindow(), new SettingsWindow(), new LicenseWindow(licenseModel), new AudioConversionWindow(), new ImagePdfOrderWindow(), new OfficeCalculationWindow(),
            new OfficeFontReviewWindow(new(@"C:\fixtures\Document.docx", ["Missing Font", "日本語"]))];
        try
        {
            foreach (var window in windows)
            {
                Check(!window.IsVisible && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(window)),
                    window.GetType().Name + ": instantiated without showing UI");
                Check(window.SizeToContent == (window is MainWindow ? SizeToContent.Manual : SizeToContent.Height),
                    window.GetType().Name + ": results use available height; dialogs fit their content");
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
            var fontReview = (OfficeFontReviewWindow)windows[8];
            Check(!fontReview.Accepted && Find<Button>(fontReview, "OfficeFontsSkip").IsCancel &&
                !Find<Button>(fontReview, "OfficeFontsSave").IsDefault, "Office font review: no implicit consent; Escape declares skip");
            Check(Find<TextBlock>(fontReview, "OfficeMissingFonts").Text == "Missing Font" + Environment.NewLine + "日本語",
                "Office font review: exact bounded family names are readable text");
            Find<Button>(fontReview, "OfficeFontsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(fontReview.Accepted, "Office font review: Save explicitly accepts this document");
            var skippedFonts = new OfficeFontReviewWindow(new(@"C:\fixtures\Document.docx", ["Missing Font"]));
            Find<Button>(skippedFonts, "OfficeFontsSkip").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!skippedFonts.Accepted, "Office font review: Skip grants no consent");
            var closedFonts = new OfficeFontReviewWindow(new(@"C:\fixtures\Document.docx", ["Missing Font"]));
            closedFonts.Close();
            Check(!closedFonts.Accepted, "Office font review: closing grants no consent");
            var manyFonts = new OfficeFontReviewWindow(new(@"C:\fixtures\Document.docx",
                [.. Enumerable.Range(0, 64).Select(index => "Unavailable family " + index + " " + new string('x', 100))]));
            var fontContent = (FrameworkElement)manyFonts.Content;
            fontContent.Measure(new Size(manyFonts.MinWidth - 16, manyFonts.MinHeight - 40));
            fontContent.Arrange(new Rect(0, 0, manyFonts.MinWidth - 16, manyFonts.MinHeight - 40));
            fontContent.UpdateLayout();
            var fontBody = Descendants(manyFonts).OfType<ScrollViewer>().Single();
            var saveFonts = Find<Button>(manyFonts, "OfficeFontsSave");
            var saveFontBounds = saveFonts.TransformToAncestor(fontContent).TransformBounds(new Rect(saveFonts.RenderSize));
            var fontBodyBounds = fontBody.TransformToAncestor(fontContent).TransformBounds(new Rect(fontBody.RenderSize));
            Check(fontBody.ScrollableHeight > 0 && saveFontBounds.Top >= fontBodyBounds.Bottom && saveFontBounds.Bottom <= fontContent.RenderSize.Height,
                "Office font review: maximum family list scrolls while Save remains reachable at minimum size");
            manyFonts.Close();
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
            foreach (var (window, id) in new[] { (main, "BatchSummary"), (main, "RecoveryNotice"), (main, "InputNotice"), (license, "NoticeStatus"),
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
            main.DataContext = new { IsLanding = true, HasResults = false, IsOperationBusy = false, HasProblems = false, CanRetry = false };
            ((FrameworkElement)main.Content).UpdateLayout();
            Check(Find<Expander>(main, "FileDetails").Visibility == Visibility.Collapsed &&
                Find<Button>(main, "CancelPending").Visibility == Visibility.Collapsed &&
                Find<Button>(main, "RetrySelectedFiles").Visibility == Visibility.Collapsed,
                "idle: empty results and idle job controls stay hidden");
            main.DataContext = new { IsLanding = false, HasResults = true, IsOperationBusy = true, HasProblems = true, CanRetry = true };
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
            RunNoticeContracts(Check);
            RunLicenseLinkContracts(Check);
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
            // Use fresh bindings after the live-region text mutation checks above.
            main = new MainWindow();
            windows = [.. windows, main];
            _ = new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var results = Find<DataGrid>(main, "BatchResults");
            // Analyze has its own window; its facts, selection and themes are exercised by --analysis-views.
            main.DataContext = new { HasResults = true, ShowDetails = true };
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
            var conversionModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-conversion-view-worker.exe")));
            var conversionRow = new FileRow(1, "convert", @"C:\fixtures\Lecture.pdf", new ContextSuite.Core.Settings.SuiteSettings().Capture("convert"), "pdf-dark");
            conversionModel.Rows.Add(conversionRow);
            main.DataContext = conversionModel;
            Check(conversionModel.ShowDetails && Find<Expander>(main, "FileDetails").IsExpanded,
                "conversion: details open for pending work without requiring a warning");
            conversionRow.ApplyResult(new(conversionRow.Path, ContextSuite.Core.Operations.OperationState.Succeeded, "Copy saved."));
            Check(conversionModel.ShowDetails, "conversion: successful results retain expanded details");
            results.ItemsSource = Enumerable.Range(1, 30).Select(index => new
            {
                Name = $"Lecture {index} - Software Modeling.pdf", Outcome = "Completed with warning", Savings = "—",
                OperationDetails = string.Join(Environment.NewLine, Enumerable.Repeat("A long selected-file detail that remains readable in a scrollable area.", 20)),
                HasOutput = true
            }).ToArray();
            results.SelectedIndex = 0;
            _ = new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var previousHeight = 0.0;
            foreach (var height in new[] { main.MinHeight - 40, 500.0, 960.0 })
            {
                main.Width = 720; main.Height = height + 40;
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                mainContent.Measure(new Size(704, height));
                mainContent.Arrange(new Rect(0, 0, 704, height));
                mainContent.UpdateLayout();
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                mainContent.Measure(new Size(704, height));
                mainContent.Arrange(new Rect(0, 0, 704, height));
                mainContent.UpdateLayout();
                Check(results.ActualHeight >= 40 && results.ActualHeight > previousHeight, "results: table grows with window height " + height);
                previousHeight = results.ActualHeight;
                var folder = Find<Button>(main, "OpenOutputFolder");
                var close = Find<Button>(main, "CloseResults");
                Check(folder.TranslatePoint(new Point(0, folder.ActualHeight), mainContent).Y <= close.TranslatePoint(new Point(), mainContent).Y &&
                    close.TranslatePoint(new Point(0, close.ActualHeight), mainContent).Y <= mainContent.ActualHeight,
                    $"results: details and output action stay above the footer at {height} (folder bottom {folder.TranslatePoint(new Point(0, folder.ActualHeight), mainContent).Y}, footer top {close.TranslatePoint(new Point(), mainContent).Y}, table {results.ActualHeight})");
                Check(VisualDescendants(results).OfType<ScrollViewer>().Any(scroll => scroll.ScrollableHeight > 0),
                    "results: overflowing file list scrolls within its allotted space at " + height);
            }
            Check(results.ActualHeight > 400, "results: tall windows no longer cap the file table at 240 pixels");
            var disclosure = Find<Expander>(main, "FileDetails");
            disclosure.IsExpanded = false; disclosure.IsExpanded = true;
            Check(main.SizeToContent == SizeToContent.Manual, "results: opening details retains the user's window height");
            const string coordinateWarning = "Dark-mode PDF copy saved. Recovered malformed drawing coordinates using the validated zero interpretation.";
            const string dimensionWarning = "Dark-mode PDF copy saved. Recovered inconsistent image dimensions using the JPEG and mask data.";
            const string retainedWarning = "Dark-mode PDF copy saved. Some images retain their original colors.";
            var coordinateModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-coordinate-view-worker.exe")));
            var coordinateRow = AddWarning(coordinateModel, "Coordinates.pdf", "pdf-dark", coordinateWarning);
            Check(coordinateModel.Summary.Contains("recovered drawing coordinates", StringComparison.Ordinal) &&
                !coordinateModel.Summary.Contains("original colors", StringComparison.Ordinal) &&
                coordinateModel.ShowDetails && coordinateRow.Outcome == "Completed with warning" &&
                coordinateRow.OperationDetails.Contains(coordinateWarning, StringComparison.Ordinal),
                "PDF dark: coordinate recovery opens details and does not claim retained image colors");
            var dimensionModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-dimension-view-worker.exe")));
            var dimensionRow = AddWarning(dimensionModel, "Dimensions.pdf", "pdf-dark", dimensionWarning);
            Check(dimensionModel.Summary.Contains("recovered image dimensions", StringComparison.Ordinal) &&
                !dimensionModel.Summary.Contains("original colors", StringComparison.Ordinal) &&
                !dimensionModel.Summary.Contains("recovered drawing coordinates", StringComparison.Ordinal) &&
                dimensionRow.OperationDetails.Contains(dimensionWarning, StringComparison.Ordinal),
                "PDF dark: image-dimension recovery has its own summary and retains per-file detail");
            var retainedModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-retained-view-worker.exe")));
            AddWarning(retainedModel, "Retained.pdf", "pdf-dark", retainedWarning);
            Check(retainedModel.Summary.Contains("Some images retain their original colors.", StringComparison.Ordinal) &&
                !retainedModel.Summary.Contains("recovered", StringComparison.Ordinal),
                "PDF dark: retained-image warning does not imply source recovery");
            var mixedModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-mixed-view-worker.exe")));
            AddWarning(mixedModel, "Coordinates.pdf", "pdf-dark", coordinateWarning);
            AddWarning(mixedModel, "Dimensions.pdf", "pdf-dark", dimensionWarning);
            AddWarning(mixedModel, "Retained.pdf", "pdf-dark", retainedWarning);
            AddWarning(mixedModel, "Optimized.png", "auto", "Optimized copy saved. Metadata removed.", "optimize");
            Check(mixedModel.Summary.Contains("4 warnings", StringComparison.Ordinal) &&
                mixedModel.Summary.Contains("Some images retain their original colors.", StringComparison.Ordinal) &&
                mixedModel.Summary.Contains("recovered image dimensions", StringComparison.Ordinal) &&
                mixedModel.Summary.Contains("recovered drawing coordinates", StringComparison.Ordinal) &&
                mixedModel.Summary.Contains("Some metadata was removed from optimized copies.", StringComparison.Ordinal),
                "mixed batch: every warning cause remains visible and the existing Optimize warning remains separate");
            var combinedModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-combined-view-worker.exe")));
            AddWarning(combinedModel, "Combined.pdf", "pdf-dark", dimensionWarning + " " +
                "Some images retain their original colors. Recovered malformed drawing coordinates using the validated zero interpretation.");
            Check(combinedModel.Summary.Contains("original colors", StringComparison.Ordinal) &&
                combinedModel.Summary.Contains("recovered image dimensions", StringComparison.Ordinal) &&
                combinedModel.Summary.Contains("recovered drawing coordinates", StringComparison.Ordinal),
                "PDF dark: one published copy can report retained images and both recoveries together");
            var otherModel = new MainViewModel(new WorkerClient(Path.Combine(Path.GetTempPath(), "unused-other-view-worker.exe")));
            AddWarning(otherModel, "Future.pdf", "pdf-dark", "Dark-mode PDF copy saved. Additional warning.");
            Check(otherModel.Summary.Contains("see Files and details", StringComparison.Ordinal) &&
                !otherModel.Summary.Contains("original colors", StringComparison.Ordinal) &&
                !otherModel.Summary.Contains("recovered", StringComparison.Ordinal),
                "PDF dark: an unfamiliar metadata warning remains visible without an invented cause");
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

        static FileRow AddWarning(MainViewModel model, string name, string action, string message, string operation = "convert")
        {
            var path = Path.Combine(@"C:\fixtures", name);
            var row = new FileRow(1, operation, path, new SuiteSettings().Capture(operation), action);
            model.Rows.Add(row);
            model.RecordResult(row, new PublicationResult(path, PublicationOutcome.CopyCreated, message,
                path + ".copy", MetadataWarning: true).ToFileResult());
            return row;
        }
    }

    private static void RunNoticeContracts(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "notice-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var model = new LicenseViewModel(new(new UnusedLicenseService(),
            new LicenseStore(Path.Combine(root, "unused-license.bin"), LicenseEnvironment.Sandbox)));
        System.Diagnostics.ProcessStartInfo? opened = null;
        var fail = false;
        var window = new LicenseWindow(model, root, start =>
        {
            if (fail) throw new System.ComponentModel.Win32Exception(1155);
            opened = start;
        });
        try
        {
            var button = Find<Button>(window, "OpenThirdPartyNotices");
            var status = Find<StatusTextBlock>(window, "NoticeStatus");
            check(button.IsEnabled && button.Focusable &&
                UIElementAutomationPeer.CreatePeerForElement(button)?.GetName() == "Third-party notices",
                "notices: named keyboard control works without loaded license state");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(opened is null && status.Text.Contains("missing", StringComparison.Ordinal) && model.Message.Length > 0,
                "notices: missing bundle is reported without launching a process");
            var path = Path.Combine(root, "ThirdPartyNotices.html");
            File.WriteAllText(path, "<!doctype html><title>Test notices</title>");
            var licenseMessage = model.Message;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(opened?.FileName == path && opened.UseShellExecute && opened.Arguments.Length == 0 &&
                opened.ArgumentList.Count == 0 && status.Text == "Third-party notices opened." && model.Message == licenseMessage,
                "notices: opens only the bundled index and preserves license status");
            fail = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(status.Text.Contains("Open ThirdPartyNotices.html", StringComparison.Ordinal) && model.Message == licenseMessage,
                "notices: unavailable file association has an actionable local fallback");
            check(!window.IsVisible && !File.Exists(Path.Combine(root, "unused-license.bin")),
                "notices: tests did not show UI, use a browser or touch licensing storage");
        }
        finally { window.Close(); }
    }

    private static void RunLicenseLinkContracts(Action<bool, string> check)
    {
        foreach (var address in new Uri?[] { null, new("http://example.test/buy"), new("file:///C:/test"),
            new("https://user:secret@example.test/buy"), new("relative", UriKind.Relative), new("https://example.test/buy") })
        {
            using var model = new LicenseViewModel(new(new UnusedLicenseService { PurchaseUrl = address, PortalUrl = address },
                new LicenseStore(Path.Combine(Path.GetTempPath(), "unused-license-links.bin"), LicenseEnvironment.Sandbox)));
            System.Diagnostics.ProcessStartInfo? opened = null;
            var fail = false;
            var window = new LicenseWindow(model, openNotice: start =>
            {
                if (fail) throw new System.ComponentModel.Win32Exception(1155);
                opened = start;
            });
            try
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var valid = address?.OriginalString == "https://example.test/buy";
                foreach (var id in new[] { "PurchaseLicense", "ManageLicense" })
                {
                    var button = Find<Button>(window, id);
                    check(button.IsEnabled == valid && button.Focusable, "license links: availability and keyboard access " + id);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(valid ? opened?.FileName == address!.AbsoluteUri && opened.UseShellExecute &&
                        opened.Arguments.Length == 0 && opened.ArgumentList.Count == 0 : opened is null,
                        "license links: exact HTTPS destination only " + id);
                }
                if (valid)
                {
                    fail = true;
                    Find<Button>(window, "PurchaseLicense").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(Find<StatusTextBlock>(window, "LicenseLinkStatus").Text.Contains(address!.AbsoluteUri) &&
                        model.Message == "One purchase unlocks Context Suite and all future updates.",
                        "license links: browser failure provides address and preserves access status");
                }
            }
            finally { window.Close(); }
        }
    }

    private sealed class UnusedLicenseService : ILicenseService
    {
        public LicenseEnvironment Environment => LicenseEnvironment.Sandbox;
        public Uri? PurchaseUrl { get; init; }
        public Uri? PortalUrl { get; init; }
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
