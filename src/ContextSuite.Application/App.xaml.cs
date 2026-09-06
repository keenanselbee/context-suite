using System.Windows;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;

namespace ContextSuite.Application;

public partial class App : System.Windows.Application
{
    private ActivationRouter? _router;
    private MainViewModel? _viewModel;
    private bool _closing;

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
            _router = ActivationRouter.TryCreate();
            if (_router is null)
            {
                await ActivationRouter.ForwardAsync(request, CancellationToken.None);
                if (activationPath is not null) ActivationStore.Acknowledge(activationPath);
                Shutdown();
                return;
            }
            _viewModel = new MainViewModel(new WorkerClient(Path.Combine(AppContext.BaseDirectory, "ContextSuite.Worker.exe")));
            var window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Closing += OnClosing;
            _router.Start(incoming => Dispatcher.InvokeAsync(() =>
            {
                var reply = _viewModel.Admit(incoming);
                if (reply.Accepted)
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                }
                return reply;
            }).Task);
            var accepted = _viewModel.Admit(request);
            if (!accepted.Accepted) throw new InvalidDataException(accepted.Message);
            window.Show();
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

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_closing || _viewModel is null) return;
        if (_viewModel.IsBusy && MessageBox.Show("Cancel pending work and exit? Completed results will not be undone.",
            "Context Suite", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _closing = true;
        _router!.StopAccepting();
        await _viewModel.DisposeAsync();
        await _router.DisposeAsync();
        Shutdown();
    }
}
