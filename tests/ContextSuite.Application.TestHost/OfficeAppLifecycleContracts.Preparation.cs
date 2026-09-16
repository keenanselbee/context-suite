using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.TestHost;

internal sealed partial class OfficeAppLifecycleContracts
{
    private bool IsPreparation => mode is "preparation-before" or "preparation-partial" or "preparation-retiring" or "preparation-locked";
    private OfficeExportWork? _preparedWork;
    private byte[]? _officeOriginal;
    private DateTime _officeWritten;

    private void PrepareInterruptedContext()
    {
        using var ready = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ready.json")));
        _preparedWork = ready.RootElement.GetProperty("Work").Deserialize<OfficeExportWork>()!;
        var contexts = Path.Combine(root, "WorkerScratch", "OfficeContexts");
        if (Path.GetDirectoryName(_preparedWork.DirectoryPath) != contexts || Directory.GetFiles(contexts, "*.ownership").Length != 1)
            throw new InvalidDataException("Expected one owned interrupted preparation.");
        _officeOriginal = File.ReadAllBytes(Path.Combine(root, "original.docx"));
        _officeWritten = File.GetLastWriteTimeUtc(Path.Combine(root, "original.docx"));
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (args.Exception is MediaWorkerException failure)
                lock (_checks) File.AppendAllText(Path.Combine(root, "worker-failures.log"), DateTime.UtcNow.ToString("O") + " " + failure.Failure + "\n");
        };
        if (mode == "preparation-locked")
            _held = new FileStream(_preparedWork.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private async Task ObservePreparationRecoveryAsync(App app, Task recovery, CancellationToken token)
    {
        var blocked = mode == "preparation-locked";
        if (blocked)
        {
            await Task.Delay(300, token);
            Check(!recovery.IsCompleted, "locked partial copy keeps actual startup recovery pending");
            await ForwardPreparationCommandAsync("analyze", "open-details", "fixture.bmp", token);
            Check(!recovery.IsCompleted && _model!.Rows.Single().Result.State == OperationState.Succeeded,
                "Analyze remains usable through the router during partial-copy recovery");
        }
        await recovery.WaitAsync(token);
        var contexts = Path.Combine(root, "WorkerScratch", "OfficeContexts");
        Check(app.MainWindow.IsVisible && _model!.RecoveryNotice.Contains(blocked ? "needs review" : "Run Convert again"),
            "actual startup presents the appropriate preparation recovery guidance");
        if (!blocked)
        {
            Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && !_model!.RecoveryNotice.Contains("were kept"),
                "actual application retires its abandoned preparation without claiming retained files");
            await ForwardPreparationCommandAsync("analyze", "open-details", "fixture.bmp", token);
            Check(_model!.Rows.Single().Result.State == OperationState.Succeeded, "Analyze succeeds after preparation recovery");
        }
        var office = blocked || mode == "preparation-partial";
        await ForwardPreparationCommandAsync("convert", office ? "pdf" : "tga", office ? "original.docx" : "fixture.bmp", token);
        var row = _model!.Rows.Last();
        File.WriteAllText(Path.Combine(root, "following-output.json"), JsonSerializer.Serialize(row.Result));
        if (blocked)
        {
            Check(row.Result.State == OperationState.Failed && row.Status.Contains("needs review") &&
                !Directory.EnumerateFiles(root, "*.pdf").Any() && File.Exists(_preparedWork!.SourcePath),
                "actual Office command refuses pending preparation cleanup without publishing a PDF");
        }
        else
        {
            Check(row.Result.State == OperationState.Succeeded && File.Exists(row.OutputPath) &&
                Path.GetExtension(row.OutputPath) == (office ? ".pdf" : ".tga"),
                "following direct command validates and publishes its named copy: " + row.Status);
            Check(!Directory.EnumerateFileSystemEntries(contexts).Any(), "following work leaves no Office context or journal");
        }
        Check(_model.RecoveryNotice.Contains(blocked ? "needs review" : "Run Convert again"),
            "following commands preserve the recovery notice");
    }

    private async Task ForwardPreparationCommandAsync(string operation, string action, string file, CancellationToken token)
    {
        using var child = StartChild(root, Environment.GetEnvironmentVariable("CONTEXTSUITE_TEST_WORKER")!, null,
            Request(root, operation, action, Path.Combine(root, file)));
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync(token); }
        finally
        {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(root, operation + "-stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(root, operation + "-stderr.log"), await stderr);
        }
        Check(child.ExitCode == 0, "following " + operation + " forwards through the actual application router");
        await _model!.WaitForIdleAsync().WaitAsync(token);
    }

    private void VerifyPreparationExit()
    {
        Check(File.ReadAllBytes(Path.Combine(root, "original.docx")).SequenceEqual(_officeOriginal!) &&
            File.GetLastWriteTimeUtc(Path.Combine(root, "original.docx")) == _officeWritten,
            "application recovery and following work preserve the original Office bytes and timestamp");
        OfficeSandboxOwner.RequireProfileAbsent(_preparedWork!.ProfileName);
        Check(true, "abandoned preparation has no native profile or mapping after application exit");
    }

    internal static async Task HoldPreparationAsync(string stage, string runtime, string scenario)
    {
        if (scenario is not ("preparation-before" or "preparation-partial" or "preparation-retiring" or "preparation-locked") ||
            !Path.GetFullPath(stage).Contains("\\.codex-temp\\office-app-lifecycle\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an owned application preparation fixture.");
        using var prepared = await OfficeContextPreparation.CreateAsync(Path.Combine(stage, "WorkerScratch", "OfficeContexts"), runtime,
            Path.Combine(stage, "original.docx"), "docx", "none", default, createContextRoot: true, copyProgress: (work, copied) =>
            {
                if (scenario == "preparation-retiring" || (scenario == "preparation-before" ? copied != 0 : copied == 0)) return;
                Ready(work, copied);
            });
        // A locked generated child forces live retirement to leave durable intent.
        using var locked = new FileStream(Path.Combine(prepared.Work.DirectoryPath, "temp", "held.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try { prepared.RetireUnstarted(); throw new InvalidOperationException("Expected obstructed cleanup."); }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode is 32 or 33) { }
        if (prepared.Journal.Changes[^1].Step != OfficeOwnershipStep.RetirementIntent) throw new InvalidDataException("Missing retirement intent.");
        Ready(prepared.Work, prepared.Work.SourceBytes);

        void Ready(OfficeExportWork work, long copied)
        {
            var ready = Path.Combine(stage, "ready.json");
            File.WriteAllText(ready + ".new", JsonSerializer.Serialize(new { Work = work, Copied = copied }));
            File.Move(ready + ".new", ready);
            using var hold = new ManualResetEvent(false);
            hold.WaitOne(); // Parent kills only this disposable preparation owner.
        }
    }

    internal static async Task<int> RunPreparationAsync(string worker, string fixture, string selected = "all", string layout = "full")
    {
        string[] scenarios = ["preparation-before", "preparation-partial", "preparation-retiring", "preparation-locked"];
        if (layout is not ("full" or "compact") || selected != "all" && !scenarios.Contains(selected))
            throw new InvalidDataException("Unknown preparation case or path layout.");
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ContextSuite.Production.slnx"))) repository = repository.Parent;
        if (repository is null || !File.Exists(worker) || !Path.GetFullPath(fixture).StartsWith(Path.Combine(repository.FullName, ".codex-temp") + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use a retained worker and disposable document fixture.");
        var batch = Path.Combine(repository.FullName, ".codex-temp", "office-app-lifecycle", Guid.NewGuid().ToString("N")[..(layout == "compact" ? 12 : 32)]);
        if (Directory.Exists(batch)) throw new IOException("Lifecycle directory already exists.");
        Directory.CreateDirectory(batch); Console.WriteLine("Preparation lifecycle evidence: " + batch);
        var reports = new List<JsonElement>();
        foreach (var scenario in scenarios.Where(value => selected == "all" || value == selected))
        {
            var stage = Path.Combine(batch, layout == "compact" ? scenario["preparation-".Length..] : scenario); Directory.CreateDirectory(stage);
            var original = Path.Combine(stage, "original.docx"); File.Copy(fixture, original);
            using (var zip = ZipFile.Open(original, ZipArchiveMode.Update))
            {
                var document = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Expected the authored Word fixture.");
                string xml;
                using (var reader = new StreamReader(document.Open())) xml = reader.ReadToEnd();
                document.Delete();
                // Keep the package's existing parts and content types. A valid
                // XML comment makes actual copying span more than one buffer.
                using var writer = new StreamWriter(zip.CreateEntry("word/document.xml", CompressionLevel.NoCompression).Open());
                writer.Write(xml + "\n<!--" + new string('a', 128 * 1024) + "-->");
            }
            var runtime = Path.Combine(Path.GetDirectoryName(worker)!, "office-engine");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--office-app-preparation-hold", stage, runtime, scenario }) start.ArgumentList.Add(argument);
            using (var owner = Process.Start(start) ?? throw new IOException("Could not start preparation owner."))
            {
                var stdout = owner.StandardOutput.ReadToEndAsync(); var stderr = owner.StandardError.ReadToEndAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                try
                {
                    while (!File.Exists(Path.Combine(stage, "ready.json")))
                    {
                        if (owner.HasExited) throw new IOException("Preparation owner exited before its checkpoint.");
                        await Task.Delay(20, deadline.Token);
                    }
                    using var ready = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "ready.json")));
                    var copied = ready.RootElement.GetProperty("Copied").GetInt64();
                    if (scenario == "preparation-before" ? copied != 0 : scenario != "preparation-retiring" && (copied <= 0 || copied >= new FileInfo(original).Length))
                        throw new InvalidDataException("Preparation did not stop at its declared copy boundary.");
                }
                finally
                {
                    if (!owner.HasExited) owner.Kill(); await owner.WaitForExitAsync();
                    await File.WriteAllTextAsync(Path.Combine(stage, "owner-stdout.log"), await stdout);
                    await File.WriteAllTextAsync(Path.Combine(stage, "owner-stderr.log"), await stderr);
                }
            }
            using (var child = StartChild(stage, worker, scenario, Request(stage, "convert", "settings", null)))
            {
                var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(210));
                try { await child.WaitForExitAsync(deadline.Token); }
                finally
                {
                    if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                    await File.WriteAllTextAsync(Path.Combine(stage, "stdout.log"), await stdout);
                    await File.WriteAllTextAsync(Path.Combine(stage, "stderr.log"), await stderr);
                }
                if (child.ExitCode != 0) throw new InvalidOperationException("Preparation lifecycle failed: " + stage);
            }
            var cleanup = await OfficeRecoveryCoordinator.RecoverAsync(Path.Combine(stage, "WorkerScratch", "OfficeContexts"), runtime, default);
            if (cleanup.Incomplete || cleanup.Entries.Any(entry => entry.State == OfficeRecoveryState.ReviewRequired) ||
                Directory.EnumerateFileSystemEntries(Path.Combine(stage, "WorkerScratch", "OfficeContexts")).Any())
                throw new IOException("Retained preparation remains after releasing the test obstruction: " + stage);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "lifecycle.json")));
            if (!result.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Incomplete lifecycle evidence.");
            reports.Add(result.RootElement.Clone()); Console.WriteLine("PASS actual App preparation lifecycle: " + scenario);
        }
        File.WriteAllText(Path.Combine(batch, "results.json"), JsonSerializer.Serialize(new { Passed = true, Layout = layout, Selected = selected, Reports = reports }));
        return 0;
    }
}
