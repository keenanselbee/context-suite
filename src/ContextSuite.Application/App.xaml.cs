using System.Windows;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

public partial class App : System.Windows.Application
{
    private ActivationRouter? _router;
    private MainViewModel? _viewModel;
    private bool _closing;
    private readonly SettingsStore _settingsStore;
    private readonly ApplicationPaths _paths;
    private readonly Func<string, CancellationToken, Task<OperationRequest>> _readActivation;
    private LoadedSettings? _settings;
    private SettingsWindow? _settingsWindow;
    private AnalyzeWindow? _analyzeWindow;
    private bool _openingSettings;
    private string _requestedSettingsSection = "convert";
    private ConversionWindow? _conversionWindow;
    private AudioConversionWindow? _audioConversionWindow;
    private WemEncodingWindow? _wemEncodingWindow;
    private ImagePdfOrderWindow? _imagePdfOrderWindow;
    private OfficeCalculationWindow? _officeCalculationWindow;
    private OfficeFontReviewWindow? _officeFontReviewWindow;
    private readonly QuietWorkflow _quiet = new();
    private readonly System.Windows.Threading.DispatcherTimer _quietTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _userOpened;
    private Task _sounds = Task.CompletedTask;
    private readonly ILicenseService? _licenseService;
    private readonly CancellationTokenSource _licenseLifetime = new();
    private PaidLicenseManager? _paidLicense;
    private LicenseWindow? _licenseWindow;
    private Task? _licenseRefresh;
    private bool _licenseValidating;
    private Task _officeRecovery = Task.CompletedTask;
    private readonly CancellationTokenSource _officeRecoveryLifetime = new();

    private static partial ILicenseService? CreateProductionLicensing();
    public App() : this(ApplicationPaths.Production, CreateProductionLicensing()) { }
    internal App(ApplicationPaths paths, ILicenseService? licenseService = null,
        Func<string, CancellationToken, Task<OperationRequest>>? readActivation = null)
    {
        _paths = paths; _settingsStore = new(paths.Settings); _licenseService = licenseService;
        _readActivation = readActivation ?? ActivationStore.ReadAsync;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? activationPath = null;
            if (e.Args.Length != 0)
            {
                if (e.Args.Length != 2 || e.Args[0] != "--activation-file")
                    throw new InvalidDataException("Use --activation-file with a Context Suite selection request.");
                activationPath = e.Args[1];
            }
            OperationRequest? request = activationPath is null ? null :
                await _readActivation(activationPath, CancellationToken.None);
            using var handoff = new ActivationGate();
            await handoff.EnterAsync(CancellationToken.None);
            var forwardDeadline = DateTime.UtcNow.AddSeconds(12);
            while ((_router = ActivationRouter.TryCreate()) is null)
            {
                try { await ActivationRouter.ForwardAsync(request, CancellationToken.None); }
                catch (RouterClosingException) when (DateTime.UtcNow < forwardDeadline)
                { await Task.Delay(100); continue; }
                if (activationPath is not null) ActivationStore.Acknowledge(activationPath);
                Shutdown();
                return;
            }
            _settings = await _settingsStore.LoadAsync();
            _analysisDetailsExpanded = _settings.Settings.AnalysisDetailsExpanded;
            var publisher = new OutputPublisher(_paths.Publications, new WindowsFileRecycler(), PublicationSupport.ReplacementAvailable);
            IOperationAccess access = new LocalTrialStore(_paths.Trial);
            if (_licenseService is not null)
            {
                _paidLicense = new(_licenseService, new LicenseStore(Path.Combine(Path.GetDirectoryName(_paths.Trial)!,
                    "license-" + _licenseService.Environment.ToString().ToLowerInvariant() + ".bin"), _licenseService.Environment));
                access = new OperationAccess(new LocalTrialStore(_paths.Trial), _paidLicense);
            }
            _viewModel = new MainViewModel(new WorkerClient(_paths.Worker, _paths.WorkerScratch), _settings.Settings, publisher, access, _paths.OfficeContexts);
            _viewModel.SettingsRequested += ShowSettings;
            _viewModel.ConversionRequested += ShowConversionAsync;
            _viewModel.AudioConversionRequested += ShowAudioConversionAsync;
            _viewModel.VideoAudioTrackRequested += ShowVideoAudioTrackAsync;
            _viewModel.WemEncodingRequested += ShowWemEncodingAsync;
            _viewModel.ImagePdfOrderRequested += ShowImagePdfOrderAsync;
            _viewModel.TiffMatteRequested += ShowTiffMatteAsync;
            _viewModel.SvgSizeRequested += ShowSvgSizeAsync;
            _viewModel.OfficeCalculationRequested += ShowOfficeCalculationAsync;
            _viewModel.OfficeFontsRequested += ShowOfficeFontReviewAsync;
            var window = new MainWindow { DataContext = _viewModel };
            window.InputNotice.Text = _settings.Warning ?? "";
            MainWindow = window;
            window.Closing += OnClosing;
            window.Activated += (_, _) => { if (!window.ShowActivated) _userOpened = true; };
            _viewModel.QuickBatchStarted += (incoming, rows) => _quiet.Begin(incoming.RequestId, rows[0].Settings.PlayCompletionSound, DateTimeOffset.UtcNow);
            _viewModel.QuickBatchCompleted += (incoming, rows) =>
            {
                if (_closing) return;
                if (_quiet.Complete(incoming.RequestId, rows.Select(row => row.Result).ToArray()))
                    _sounds = PlayAfterAsync(_sounds);
                else if (_quiet.WarningSoundRequested)
                    _sounds = PlayAfterAsync(_sounds, warning: true);
                if (_quiet.NeedsAttention)
                {
                    if (!_userOpened || window.ResultsGrid.SelectedItem is null)
                        window.ResultsGrid.SelectedItem = rows.FirstOrDefault(row => row.Result.State is OperationState.Failed or OperationState.Unsupported || row.Result.Publication?.HasWarning == true)
                            ?? rows.LastOrDefault();
                    window.Show();
                }
            };
            _quietTimer.Tick += CheckQuietWindow;
            _quietTimer.Start();
            if (_paidLicense is not null) _licenseRefresh = RefreshLicenseAsync();
            _router.Start(incoming => Dispatcher.InvokeAsync(() =>
            {
                if (_closing) return new ActivationReply(1, incoming?.RequestId ?? Guid.Empty, false, "Application is closing.");
                var reply = _viewModel.Admit(incoming);
                if (reply.Accepted && incoming is null) ShowSettings("convert");
                else if (reply.Accepted && incoming?.Operation == "analyze") ShowAnalyze();
                else if (reply.Accepted && incoming?.IsQuickAction != true && incoming?.IsSettingsRequest != true)
                {
                    _userOpened = true;
                    window.ShowActivated = true;
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    if (incoming?.IsSettingsRequest != true) (_officeCalculationWindow as Window ?? _wemEncodingWindow as Window ?? _conversionWindow as Window ?? window).Activate();
                }
                return reply;
            }).Task);
            _officeRecovery = RecoverOfficeAsync(window);
            var accepted = _viewModel.Admit(request);
            if (!accepted.Accepted) throw new InvalidDataException(accepted.Message);
            _userOpened = (request is not null && request.Operation != "analyze" && request.IsQuickAction != true && request.IsSettingsRequest != true) ||
                !string.IsNullOrEmpty(_viewModel.RecoveryNotice) || _settings.Warning is not null;
            window.ShowActivated = _userOpened;
            if (_userOpened) window.Show();
            if (request?.Operation == "analyze") ShowAnalyze();
            if (request is null) ShowSettings("convert");
            if (activationPath is not null) ActivationStore.Acknowledge(activationPath);
            ActivationStore.CleanupStale(_paths.ActivationCleanupDirectory);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Activation failed: {error.GetType().Name}, HRESULT 0x{error.HResult:X8}, method {error.TargetSite?.Name}.");
            _officeRecoveryLifetime.Cancel();
            await _officeRecovery;
            MessageBox.Show("Context Suite could not receive this selection. Check that the files still exist and try again.",
                "Context Suite", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (_router is not null) await _router.DisposeAsync();
            if (_viewModel is not null) await _viewModel.DisposeAsync();
            Shutdown(1);
        }
    }

    private async Task RecoverOfficeAsync(MainWindow window)
    {
        var report = await _viewModel!.BeginOfficeRecovery(_officeRecoveryLifetime.Token);
        if (_closing || report.Cancelled) return;
        _viewModel!.SetOfficeRecoveryReport(report);
        if (!report.NeedsAttention) return;
        _userOpened = true;
        window.ShowActivated = true;
        window.Show();
    }

    private void ShowAnalyze()
    {
        if (_analyzeWindow is null)
        {
            _analyzeWindow = new AnalyzeWindow { DataContext = _viewModel, DetailsExpanded = _analysisDetailsExpanded };
            _analyzeWindow.DetailsExpandedChanged += expanded =>
            {
                _analysisDetailsExpanded = expanded;
                _analysisPreferenceSave = SaveAnalysisPreferenceAsync(_analysisPreferenceSave, expanded);
            };
            _analyzeWindow.Closed += (_, _) => _analyzeWindow = null;
        }
        _analyzeWindow.Show();
        if (_analyzeWindow.WindowState == WindowState.Minimized) _analyzeWindow.WindowState = WindowState.Normal;
        _analyzeWindow.Activate();
    }

    private bool _analysisDetailsExpanded = true;
    private Task _analysisPreferenceSave = Task.CompletedTask;

    private async Task SaveAnalysisPreferenceAsync(Task previous, bool expanded)
    {
        await previous;
        try
        {
            // Merge only the disclosure preference into the latest settings.
            // Serial saves preserve the final choice even after rapid toggles.
            var latest = await _settingsStore.LoadAsync();
            if (!latest.CanSave || latest.Warning is not null)
                throw new IOException("Settings are unavailable.");
            _settings = latest.Settings.AnalysisDetailsExpanded == expanded ? latest :
                await _settingsStore.SaveAsync(latest.Settings with { AnalysisDetailsExpanded = expanded }, latest);
            _viewModel!.Settings = _settings.Settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_analyzeWindow is not null)
                _analyzeWindow.InputNotice.Text = "This view choice could not be saved. It will be remembered until Context Suite closes.";
        }
    }

    private async Task<string?> ShowOfficeCalculationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new OfficeCalculationWindow();
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _officeCalculationWindow = window;
        window.Closed += (_, _) => completion.TrySetResult(window.Calculation);
        window.Show();
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try { return await completion.Task; }
        finally { _officeCalculationWindow = null; }
    }

    private async Task<bool> ShowOfficeFontReviewAsync(Core.Office.OfficeFontReview review, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new OfficeFontReviewWindow(review);
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _officeFontReviewWindow = window;
        window.Closed += (_, _) => completion.TrySetResult(window.Accepted);
        window.Show();
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try { return await completion.Task; }
        finally { _officeFontReviewWindow = null; }
    }

    private static async Task PlayAfterAsync(Task previous, bool warning = false)
    {
        try { await previous; await CompletionSound.PlayAsync(warning); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        { /* A missing sound is not a media failure. */ }
    }

    internal void ShowLicense()
    {
        if (_paidLicense is null || _closing) return;
        if (_licenseWindow is not null) { _licenseWindow.Activate(); return; }
        _licenseWindow = new(new LicenseViewModel(_paidLicense));
        var owner = _officeFontReviewWindow as Window ?? _officeCalculationWindow as Window ?? _imagePdfOrderWindow as Window ?? _audioConversionWindow as Window ?? _wemEncodingWindow as Window ?? _conversionWindow as Window ?? _settingsWindow ?? MainWindow;
        if (owner.IsVisible) _licenseWindow.Owner = owner;
        _licenseWindow.Closed += async (_, _) =>
        {
            _licenseWindow = null;
            if (_closing) return;
            if (_conversionWindow?.DataContext is ConversionViewModel converter) await converter.RefreshAccessAsync();
            if (_audioConversionWindow?.DataContext is AudioConversionViewModel audio) await audio.RefreshAccessAsync();
            if (_imagePdfOrderWindow?.DataContext is ImagePdfOrderViewModel order) await order.RefreshAccessAsync();
        };
        _licenseWindow.Show();
    }

    private async Task RefreshLicenseAsync()
    {
        try
        {
            while (!_licenseLifetime.IsCancellationRequested)
            {
                _licenseValidating = true;
                try { await _paidLicense!.RefreshAsync(cancellationToken: _licenseLifetime.Token); }
                finally { _licenseValidating = false; }
                await Task.Delay(TimeSpan.FromMinutes(1), _licenseLifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _licenseLifetime.Cancel();
        _officeRecoveryLifetime.Cancel();
        if (_licenseService is IDisposable disposable) disposable.Dispose();
        base.OnExit(e);
    }

    private async void CheckQuietWindow(object? sender, EventArgs e)
    {
        if (_closing || _viewModel is null) return;
        if (!_userOpened && _conversionWindow is null && _audioConversionWindow is null && _wemEncodingWindow is null && _imagePdfOrderWindow is null && _officeCalculationWindow is null && _officeFontReviewWindow is null && _quiet.ShowProgress(DateTimeOffset.UtcNow)) MainWindow.Show();
        if (_viewModel.IsBusy || _userOpened || _quiet.NeedsAttention || _settingsWindow is not null ||
            _wemEncodingWindow is not null || _openingSettings || _licenseWindow is not null) return;
        MainWindow.Hide();
        if (_analyzeWindow is not null) return;
        // A bounded refresh may finish quietly before exit so short image jobs do
        // not repeatedly cancel the daily validation and exhaust offline grace.
        if (!_sounds.IsCompleted || !_analysisPreferenceSave.IsCompleted || _licenseValidating || !_officeRecovery.IsCompleted || _router!.HasConnectedClient) return;
        using var handoff = new ActivationGate();
        if (!handoff.TryEnter()) return;
        _closing = true;
        _quietTimer.Stop();
        _licenseLifetime.Cancel();
        _officeRecoveryLifetime.Cancel();
        await _officeRecovery;
        await _analysisPreferenceSave;
        if (_licenseRefresh is not null) await _licenseRefresh;
        _router.StopAccepting();
        await _viewModel.DisposeAsync();
        await _router.DisposeAsync();
        Shutdown();
    }

    private async Task<ConfirmedImageBatch?> ShowConversionAsync(ConversionViewModel planner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ConfirmedImageBatch?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new ConversionWindow { DataContext = planner };
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _conversionWindow = window;
        void Confirmed(ConfirmedImageBatch confirmed) { completion.TrySetResult(confirmed); window.Close(); }
        planner.Confirmed += Confirmed;
        window.Closed += (_, _) => { planner.CancelPreparation(); completion.TrySetResult(null); };
        window.Show();
        using var registration = cancellationToken.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try
        {
            await planner.InitializeAsync();
            return await completion.Task;
        }
        finally { planner.Confirmed -= Confirmed; _conversionWindow = null; }
    }

    private Task<VideoAudioSelection?> ShowVideoAudioTrackAsync(VideoAudioTrackViewModel decision, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = new VideoAudioTrackWindow(decision);
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        return Task.FromResult(window.ShowDialog() == true ? window.Selection : null);
    }

    private async Task<ConfirmedAudioConversion?> ShowAudioConversionAsync(AudioConversionViewModel decision, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ConfirmedAudioConversion?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new AudioConversionWindow { DataContext = decision };
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _audioConversionWindow = window;
        void Confirmed(ConfirmedAudioConversion confirmed) { completion.TrySetResult(confirmed); window.Close(); }
        decision.Confirmed += Confirmed;
        window.Closed += (_, _) => { decision.Dispose(); completion.TrySetResult(null); };
        window.Show();
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try { await decision.RefreshAccessAsync(); return await completion.Task; }
        finally { decision.Confirmed -= Confirmed; _audioConversionWindow = null; }
    }

    private async Task<WemEncodingOptions?> ShowWemEncodingAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<WemEncodingOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decision = new WemEncodingViewModel(_viewModel!.Settings.WwiseConsolePath, RememberWwiseConsoleAsync, token);
        var window = new WemEncodingWindow { DataContext = decision };
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _wemEncodingWindow = window;
        void Confirmed(WemEncodingOptions options) { completion.TrySetResult(options); window.Close(); }
        decision.Confirmed += Confirmed;
        window.Closed += (_, _) => { decision.Dispose(); completion.TrySetResult(null); };
        window.Show();
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try { await decision.InitializeAsync(); return await completion.Task; }
        finally { decision.Confirmed -= Confirmed; _wemEncodingWindow = null; }
    }

    private async Task RememberWwiseConsoleAsync(string path, CancellationToken token)
    {
        var latest = await _settingsStore.LoadAsync(token);
        if (!latest.CanSave)
            throw new IOException(latest.Warning ?? "Settings are unavailable; the Wwise location could not be saved.");
        var saved = string.Equals(latest.Settings.WwiseConsolePath, path, StringComparison.OrdinalIgnoreCase)
            ? latest : await _settingsStore.SaveAsync(latest.Settings with { WwiseConsolePath = path }, latest, token);
        _settings = saved;
        _viewModel!.Settings = saved.Settings;
    }

    private async Task<ConfirmedImagePdf?> ShowImagePdfOrderAsync(ImagePdfOrderViewModel decision, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ConfirmedImagePdf?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new ImagePdfOrderWindow { DataContext = decision };
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        _imagePdfOrderWindow = window;
        void Confirmed(ConfirmedImagePdf confirmed) { completion.TrySetResult(confirmed); window.Close(); }
        decision.Confirmed += Confirmed;
        window.Closed += (_, _) => { decision.Dispose(); completion.TrySetResult(null); };
        window.Show();
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try { await decision.RefreshAccessAsync(); return await completion.Task; }
        finally { decision.Confirmed -= Confirmed; _imagePdfOrderWindow = null; }
    }

    private Task<uint?> ShowTiffMatteAsync(ContextSuite.Core.Tiff.TiffSourceFacts source,
        ContextSuite.Core.Images.ImagePreview preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = new TiffMatteWindow(Path.GetFileName(source.Path), preview);
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        return Task.FromResult(window.ShowDialog() == true ? window.MatteRgb : null);
    }

    private Task<ContextSuite.Core.Svg.SvgConversionOptions?> ShowSvgSizeAsync(
        IReadOnlyList<ContextSuite.Core.Svg.SvgSourceFacts> sources, ContextSuite.Core.Images.ImageFormat target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = new SvgSizeWindow(sources, target);
        if (MainWindow.IsVisible) window.Owner = MainWindow;
        using var registration = token.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        return Task.FromResult(window.ShowDialog() == true ? window.Options : null);
    }

    private async void ShowSettings(string section)
    {
        _requestedSettingsSection = section;
        if (_settingsWindow is not null)
        {
            ((SettingsViewModel)_settingsWindow.DataContext).SelectedSection = section == "optimize" ? 1 : 0;
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        if (_openingSettings) return;
        _openingSettings = true;
        try
        {
            _settings = await _settingsStore.LoadAsync();
            if (_closing) return;
            _viewModel!.Settings = _settings.Settings;
            var settingsViewModel = new SettingsViewModel(_settingsStore, _settings, _requestedSettingsSection, PublicationSupport.ReplacementAvailable);
            var window = new SettingsWindow { DataContext = settingsViewModel };
            if (MainWindow.IsVisible) window.Owner = MainWindow;
            _settingsWindow = window;
            settingsViewModel.Saved += loaded =>
            {
                _settings = loaded;
                _viewModel.Settings = loaded.Settings;
                window.Close();
            };
            window.Closed += (_, _) => _settingsWindow = null;
            window.Show();
        }
        finally { _openingSettings = false; }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_closing || _viewModel is null) return;
        if (_analyzeWindow is not null)
        {
            if (_viewModel.IsOperationBusy)
            {
                if (MessageBox.Show(MainWindow, "Cancel pending conversions and optimizations and close these results? Completed results will not be undone. Analyze will stay open.",
                    "Context Suite", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _viewModel.CancelCommand.Execute(null);
            }
            // Closing results leaves the independent inspector and its analysis alive.
            _userOpened = false;
            _quiet.DismissAttention();
            MainWindow.Hide();
            return;
        }
        if (_viewModel.IsBusy && MessageBox.Show("Cancel pending work and exit? Completed results will not be undone.",
            "Context Suite", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _closing = true;
        _quietTimer.Stop();
        _licenseLifetime.Cancel();
        _officeRecoveryLifetime.Cancel();
        await _officeRecovery;
        await _analysisPreferenceSave;
        if (_licenseRefresh is not null) await _licenseRefresh;
        _router!.StopAccepting();
        await _viewModel.DisposeAsync();
        await _router.DisposeAsync();
        Shutdown();
    }
}
