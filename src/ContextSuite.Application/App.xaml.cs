using System.Windows;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;

namespace ContextSuite.Application;

public partial class App : System.Windows.Application
{
    private ActivationRouter? _router;
    private MainViewModel? _viewModel;
    private bool _closing;
    private readonly SettingsStore _settingsStore;
    private readonly ApplicationPaths _paths;
    private LoadedSettings? _settings;
    private SettingsWindow? _settingsWindow;
    private bool _openingSettings;
    private string _requestedSettingsSection = "convert";
    private ConversionWindow? _conversionWindow;
    private OptimizationWindow? _optimizationWindow;
    private readonly QuietWorkflow _quiet = new();
    private readonly System.Windows.Threading.DispatcherTimer _quietTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _userOpened;
    private Task _sounds = Task.CompletedTask;

    public App() : this(ApplicationPaths.Production) { }
    internal App(ApplicationPaths paths) { _paths = paths; _settingsStore = new(paths.Settings); }

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
                await ActivationStore.ReadAsync(activationPath, CancellationToken.None);
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
            var publisher = new OutputPublisher(_paths.Publications, new WindowsFileRecycler(), PublicationSupport.ReplacementAvailable);
            _viewModel = new MainViewModel(new WorkerClient(_paths.Worker, _paths.WorkerScratch), _settings.Settings, publisher, new LocalTrialStore(_paths.Trial));
            _viewModel.SettingsRequested += ShowSettings;
            _viewModel.ConversionRequested += ShowConversionAsync;
            _viewModel.OptimizationRequested += ShowOptimizationAsync;
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
                    if (!_userOpened) window.ResultsGrid.SelectedItem = rows.FirstOrDefault(row => row.Result.State is OperationState.Failed or OperationState.Unsupported || row.Result.Publication?.HasWarning == true);
                    window.Show();
                }
            };
            _quietTimer.Tick += CheckQuietWindow;
            _quietTimer.Start();
            _router.Start(incoming => Dispatcher.InvokeAsync(() =>
            {
                if (_closing) return new ActivationReply(1, incoming?.RequestId ?? Guid.Empty, false, "Application is closing.");
                var reply = _viewModel.Admit(incoming);
                if (reply.Accepted && incoming?.IsQuickAction != true && incoming?.IsSettingsRequest != true)
                {
                    _userOpened = true;
                    window.ShowActivated = true;
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    if (incoming?.IsSettingsRequest != true) (_optimizationWindow as Window ?? _conversionWindow as Window ?? window).Activate();
                }
                return reply;
            }).Task);
            var accepted = _viewModel.Admit(request);
            if (!accepted.Accepted) throw new InvalidDataException(accepted.Message);
            _userOpened = (request?.IsQuickAction != true && request?.IsSettingsRequest != true) ||
                !string.IsNullOrEmpty(_viewModel.RecoveryNotice) || _settings.Warning is not null;
            window.ShowActivated = _userOpened;
            if (_userOpened) window.Show();
            if (activationPath is not null) ActivationStore.Acknowledge(activationPath);
            ActivationStore.CleanupStale();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Activation failed: {error.GetType().Name}, HRESULT 0x{error.HResult:X8}, method {error.TargetSite?.Name}.");
            MessageBox.Show("Context Suite could not receive this selection. Check that the files still exist and try again.",
                "Context Suite", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (_router is not null) await _router.DisposeAsync();
            if (_viewModel is not null) await _viewModel.DisposeAsync();
            Shutdown(1);
        }
    }

    private static async Task PlayAfterAsync(Task previous, bool warning = false)
    {
        try { await previous; await CompletionSound.PlayAsync(warning); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        { /* A missing sound is not a media failure. */ }
    }

    private async void CheckQuietWindow(object? sender, EventArgs e)
    {
        if (_closing || _viewModel is null) return;
        if (!_userOpened && _quiet.ShowProgress(DateTimeOffset.UtcNow)) MainWindow.Show();
        if (_viewModel.IsBusy || _userOpened || _quiet.NeedsAttention || _settingsWindow is not null || _openingSettings) return;
        MainWindow.Hide();
        if (!_sounds.IsCompleted || _router!.HasConnectedClient) return;
        using var handoff = new ActivationGate();
        if (!handoff.TryEnter()) return;
        _closing = true;
        _quietTimer.Stop();
        _router.StopAccepting();
        await _viewModel.DisposeAsync();
        await _router.DisposeAsync();
        Shutdown();
    }

    private async Task<ConfirmedImageBatch?> ShowConversionAsync(ConversionViewModel planner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ConfirmedImageBatch?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _userOpened = true;
        MainWindow.Show();
        var window = new ConversionWindow { Owner = MainWindow, DataContext = planner };
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

    private async Task<ConfirmedPngOptimization?> ShowOptimizationAsync(OptimizationViewModel planner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ConfirmedPngOptimization?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _userOpened = true;
        MainWindow.Show();
        var window = new OptimizationWindow { Owner = MainWindow, DataContext = planner };
        _optimizationWindow = window;
        void Confirmed(ConfirmedPngOptimization confirmed) { completion.TrySetResult(confirmed); window.Close(); }
        planner.Confirmed += Confirmed;
        window.Closed += (_, _) => completion.TrySetResult(null);
        window.Show();
        using var registration = cancellationToken.Register(() => Dispatcher.BeginInvoke(() => window.Close()));
        try
        {
            await planner.InitializeAsync(cancellationToken);
            return await completion.Task;
        }
        finally { planner.Confirmed -= Confirmed; _optimizationWindow = null; }
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
        if (_viewModel.IsBusy && MessageBox.Show("Cancel pending work and exit? Completed results will not be undone.",
            "Context Suite", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _closing = true;
        _quietTimer.Stop();
        _router!.StopAccepting();
        await _viewModel.DisposeAsync();
        await _router.DisposeAsync();
        Shutdown();
    }
}
