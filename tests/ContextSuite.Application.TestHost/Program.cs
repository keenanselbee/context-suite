using ContextSuite.Application.Infrastructure;

namespace ContextSuite.Application.TestHost;

internal static class Program
{
    // Test assembly only: compile and exercise the actual App, windows and view models,
    // with ordinary local-trial enforcement but isolated storage. Never copied to production.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--view-contracts"]) return ViewContracts.Run();
        if (args.Length != 2 || args[0] != "--activation-file") return 2;
        var configuredRoot = Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_ROOT");
        var worker = Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_WORKER");
        if (configuredRoot is null || worker is null) return 2;
        var root = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(root) || !root.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        var app = new App(new(Path.Combine(root, "settings.json"), Path.Combine(root, "Access", "trial.json"),
            Path.Combine(root, "Publications"), Path.GetFullPath(worker), Path.Combine(root, "WorkerScratch")));
        app.InitializeComponent();
        if (Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_QUIET_TRACE") == "1")
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var shown = new List<double>();
            MainViewModel? observed = null;
            app.Dispatcher.Hooks.OperationCompleted += (_, _) =>
            {
                if (observed is null && app.MainWindow?.DataContext is MainViewModel vm)
                {
                    observed = vm;
                    vm.QuickBatchCompleted += (_, _) => File.WriteAllText(Path.Combine(root, $"progress-{Environment.ProcessId}.json"),
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            ElapsedSeconds = timer.Elapsed.TotalSeconds, vm.IsBusy, vm.Summary, vm.RecoveryNotice,
                            Rows = vm.Rows.Select(row => new { row.Action, State = row.Result.State.ToString(), row.Status }).ToArray()
                        }));
                }
            };
            System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.FrameworkElement.LoadedEvent,
                new System.Windows.RoutedEventHandler((sender, _) =>
                {
                    if (sender is System.Windows.Window window && window.IsVisible) shown.Add(timer.Elapsed.TotalSeconds);
                }));
            app.Exit += (_, e) =>
            {
                var vm = observed;
                File.WriteAllText(Path.Combine(root, $"quiet-{Environment.ProcessId}.json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    ExitCode = e.ApplicationExitCode, ShownAtSeconds = shown,
                    States = vm?.Rows.Select(row => row.Result.State.ToString()).ToArray() ?? [],
                    Actions = vm?.Rows.Select(row => row.Action).ToArray() ?? [],
                    Outputs = vm?.Rows.Where(row => row.HasOutput).Select(row => row.OutputPath).ToArray() ?? []
                }));
            };
        }
        // These environment variables are read exclusively by this test entry point, never by the app.
        return app.Run();
    }
}
