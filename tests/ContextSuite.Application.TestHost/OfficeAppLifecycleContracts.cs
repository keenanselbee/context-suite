using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application.TestHost;

// Runs the actual App dispatcher/router lifecycle in disposable child processes.
// Schema-only journals: no Windows profiles, ACL grants or Office rendering.
internal sealed class OfficeAppLifecycleContracts(string root, string mode) : IDisposable
{
    private readonly List<string> _checks = [];
    private readonly List<string> _shown = [];
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private FileStream? _held;
    private string? _journal;
    private byte[]? _journalBytes;
    private byte[] _sourceBytes = [];
    private MainViewModel? _model;
    private Exception? _failure;
    private double? _closeAt;

    internal void Prepare(string worker)
    {
        if (mode is not ("missing" or "completed" or "review-forward" or "locked-forward" or "locked-close"))
            throw new InvalidDataException("Unknown Office application lifecycle case.");
        if (Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != "ActivationCleanup"))
            throw new InvalidDataException("Lifecycle preparation requires a fresh isolated directory.");
        var pixels = new byte[16 * 16 * 3]; Array.Fill(pixels, (byte)90);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Rgb24, null, pixels, 48)));
        using (var output = File.Create(Path.Combine(root, "fixture.bmp"))) encoder.Save(output);
        _sourceBytes = File.ReadAllBytes(Path.Combine(root, "fixture.bmp"));
        File.WriteAllText(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(new SuiteSettings { PlayCompletionSound = false }));
        if (mode == "missing") return;
        var contexts = Path.Combine(root, "WorkerScratch", "OfficeContexts"); Directory.CreateDirectory(contexts);
        var id = Guid.NewGuid(); var directory = Path.Combine(contexts, "office-" + id.ToString("N")); Directory.CreateDirectory(directory);
        var work = new OfficeExportWork(id, directory, "ContextSuite.Office.Evaluation." + id.ToString("N"), "docx", "none", 1, new string('0', 64));
        _journal = Path.Combine(contexts, id.ToString("N") + ".ownership");
        using (var journal = OfficeOwnershipJournal.Create(_journal, work, Path.Combine(Path.GetDirectoryName(worker)!, "office-engine")))
        {
            if (mode == "completed")
            {
                journal.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(work.ProfileName),
                    OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName), new string('A', 48)));
                journal.Record(new(OfficeOwnershipStep.CleanupIntent));
                journal.Record(new(OfficeOwnershipStep.DeleteIntent)); journal.Record(new(OfficeOwnershipStep.ProfileDeleted));
            }
        }
        _journalBytes = File.ReadAllBytes(_journal);
        if (mode.StartsWith("locked-", StringComparison.Ordinal)) _held = new FileStream(_journal, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    internal void Attach(App app)
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window { IsVisible: true } window) _shown.Add(window.GetType().Name);
            }));
        app.Startup += async (_, _) =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                while (app.MainWindow?.DataContext is not MainViewModel) await Task.Delay(20, deadline.Token);
                _model = (MainViewModel)app.MainWindow.DataContext;
                var recovery = (Task)typeof(App).GetField("_officeRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
                if (mode is "missing" or "completed")
                {
                    await _model.WaitForIdleAsync().WaitAsync(deadline.Token);
                    Check(_model.Rows.Count == 1 && _model.Rows[0].Result.State == OperationState.Succeeded,
                        "direct conversion completes: " + _model.Summary + " " + string.Join("; ", _model.Rows.Select(row => row.Status)));
                    return; // Exercise ordinary quiet exit without requesting it.
                }
                if (mode is "review-forward" or "locked-forward")
                {
                    if (mode == "review-forward") await recovery.WaitAsync(deadline.Token);
                    else
                    {
                        await Task.Delay(300, deadline.Token);
                        Check(!recovery.IsCompleted, "forwarding begins while actual recovery is pending");
                    }
                    var child = StartChild(root, Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_WORKER")!, null,
                        Request(root, "analyze", "open-details", Path.Combine(root, "fixture.bmp")));
                    using (child)
                    {
                        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                        try { await child.WaitForExitAsync(deadline.Token); }
                        finally
                        {
                            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                            await File.WriteAllTextAsync(Path.Combine(root, "forward-stdout.log"), await stdout);
                            await File.WriteAllTextAsync(Path.Combine(root, "forward-stderr.log"), await stderr);
                        }
                        Check(child.ExitCode == 0, "second application forwards through the actual router and exits");
                    }
                    await _model.WaitForIdleAsync().WaitAsync(deadline.Token);
                    if (mode == "locked-forward") Check(!recovery.IsCompleted, "forwarded Analyze completes while recovery is still pending");
                    await recovery.WaitAsync(deadline.Token);
                    Check(_model.RecoveryNotice.Contains("needs review") && app.MainWindow.IsVisible,
                        "retained ownership opens the application's recovery notice");
                    Check(_model.Rows.Count == 1 && _model.Rows[0].Result.State == OperationState.Succeeded &&
                        _model.RecoveryNotice.Contains("needs review"), "forwarded Analyze succeeds without losing the recovery notice");
                }
                else
                {
                    await Task.Delay(300, deadline.Token);
                    Check(!recovery.IsCompleted, "locked ownership keeps actual startup recovery pending");
                }
                Check(!_model.IsBusy, "explicit close occurs without cancelling media work");
                _closeAt = _elapsed.Elapsed.TotalSeconds;
                app.MainWindow.Close();
                if (mode == "locked-close")
                {
                    await Task.Delay(150, deadline.Token);
                    Check(!recovery.IsCompleted && app.MainWindow is not null,
                        "application remains alive while the in-progress recovery attempt finishes");
                }
            }
            catch (Exception error) { _failure = error; app.Shutdown(1); }
        };
        app.Exit += (_, e) =>
        {
            try
            {
                if (_failure is not null) throw new InvalidOperationException("Lifecycle driver failed.", _failure);
                Check(e.ApplicationExitCode == 0 && _model is not null, "actual application exits successfully");
                var recovery = (Task)typeof(App).GetField("_officeRecovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
                Check(recovery.IsCompletedSuccessfully, "application exits only after recovery completes");
                if (mode is "missing" or "completed")
                {
                    Check(_model!.Rows.Count == 1 && _model.Rows[0].Result.State == OperationState.Succeeded &&
                        _model.Rows[0].OutputPath == Path.Combine(root, "fixture - Converted.tga") &&
                        File.Exists(_model.Rows[0].OutputPath), "ordinary direct conversion publishes a named copy and exits quietly");
                    Check(_model.RecoveryNotice.Length == 0, "absent or completed storage adds no recovery notice");
                    Check(_shown.Count == 0, "ordinary successful conversion shows no application window");
                }
                if (mode == "missing") Check(!Directory.Exists(Path.Combine(root, "WorkerScratch", "OfficeContexts")), "startup does not create absent Office storage");
                if (mode == "locked-close") Check(_closeAt is not null && _elapsed.Elapsed.TotalSeconds - _closeAt >= 3,
                    "explicit shutdown awaits the bounded sharing retry rather than abandoning recovery");
                Check(File.ReadAllBytes(Path.Combine(root, "fixture.bmp")).SequenceEqual(_sourceBytes), "original fixture remains unchanged");
                _held?.Dispose(); _held = null;
                if (_journal is not null) Check(File.ReadAllBytes(_journal).SequenceEqual(_journalBytes!), "lifecycle preserves retained journal bytes");
                File.WriteAllText(Path.Combine(root, "lifecycle.json"), JsonSerializer.Serialize(new { Passed = true, Mode = mode,
                    Checks = _checks, ShownWindows = _shown, ElapsedSeconds = _elapsed.Elapsed.TotalSeconds, CloseAtSeconds = _closeAt }));
            }
            catch (Exception error)
            {
                e.ApplicationExitCode = 1;
                File.WriteAllText(Path.Combine(root, "lifecycle-error.txt"), error.ToString());
            }
        };
    }

    private void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        _checks.Add(message);
    }
    public void Dispose() => _held?.Dispose();

    internal static async Task<int> RunAsync(string worker)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ContextSuite.Production.slnx"))) repository = repository.Parent;
        if (repository is null || !File.Exists(worker)) throw new InvalidOperationException("Repository and verified staged worker required.");
        var root = Path.Combine(repository.FullName, ".codex-temp", "office-app-lifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Console.WriteLine("Lifecycle evidence: " + root);
        var reports = new List<JsonElement>();
        foreach (var mode in new[] { "missing", "completed", "review-forward", "locked-forward", "locked-close" })
        {
            var stage = Path.Combine(root, mode); Directory.CreateDirectory(stage);
            var quiet = mode is "missing" or "completed";
            var activation = Request(stage, "convert", quiet ? "tga" : "settings", quiet ? Path.Combine(stage, "fixture.bmp") : null);
            using var child = StartChild(stage, worker, mode, activation);
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await child.WaitForExitAsync(deadline.Token); }
            finally
            {
                if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                await File.WriteAllTextAsync(Path.Combine(stage, "stdout.log"), await stdout);
                await File.WriteAllTextAsync(Path.Combine(stage, "stderr.log"), await stderr);
            }
            if (child.ExitCode != 0) throw new InvalidOperationException("Application lifecycle failed: " + stage);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "lifecycle.json")));
            if (!result.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Incomplete lifecycle evidence.");
            reports.Add(result.RootElement.Clone()); Console.WriteLine("PASS actual App lifecycle: " + mode);
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Reports = reports }));
        return 0;
    }

    private static string Request(string root, string operation, string action, string? source)
    {
        var directory = Path.Combine(root, "ActivationCleanup"); Directory.CreateDirectory(directory);
        var id = Guid.NewGuid(); var path = Path.Combine(directory, id.ToString("D") + ".request");
        File.WriteAllText(path, $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount={(source is null ? 0 : 1)}\n" +
            (source is null ? "" : "path=" + source + "\n"));
        return path;
    }

    private static Process StartChild(string root, string worker, string? mode, string activation)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--activation-file"); start.ArgumentList.Add(activation);
        start.Environment["CONTEXTSUITE_TEST_ROOT"] = root; start.Environment["CONTEXTSUITE_TEST_WORKER"] = Path.GetFullPath(worker);
        start.Environment.Remove("CONTEXTSUITE_TEST_LICENSE_WORKFLOW"); start.Environment.Remove("CONTEXTSUITE_TEST_OFFICE_LIFECYCLE");
        if (mode is not null) start.Environment["CONTEXTSUITE_TEST_OFFICE_LIFECYCLE"] = mode;
        return Process.Start(start) ?? throw new IOException("Could not start lifecycle application.");
    }
}
