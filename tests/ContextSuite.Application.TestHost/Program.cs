using ContextSuite.Application.Infrastructure;

namespace ContextSuite.Application.TestHost;

internal static class Program
{
    // Test assembly only: compile and exercise the actual App, windows and view models,
    // with ordinary local-trial enforcement but isolated storage. Never copied to production.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--activation-file") return 2;
        var configuredRoot = Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_ROOT");
        var worker = Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_WORKER");
        if (configuredRoot is null || worker is null) return 2;
        var root = Path.GetFullPath(configuredRoot);
        if (!Directory.Exists(root) || !root.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        var app = new App(new(Path.Combine(root, "settings.json"), Path.Combine(root, "Access", "trial.json"),
            Path.Combine(root, "Publications"), Path.GetFullPath(worker), Path.Combine(root, "WorkerScratch")));
        app.InitializeComponent();
        // These environment variables are read exclusively by this test entry point, never by the app.
        return app.Run();
    }
}
