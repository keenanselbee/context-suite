using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using Microsoft.Win32;

internal static class OfficeOwnerLossContracts
{
    private sealed record State(OfficeExportWork Work, string Journal, string Sid);
    private sealed record ProcessIdentity(int Id, long StartUtcTicks);
    private sealed record Ready(State State, ProcessIdentity Worker, ProcessIdentity Host, long FirstBytes, long SecondBytes);

    internal static async Task HoldAsync(string worker, string source, string format, string stage)
    {
        RequireStage(stage);
        var preparationTimer = Stopwatch.StartNew();
        var runtime = Path.Combine(Path.GetDirectoryName(worker)!, "office-engine");
        using var prepared = await OfficeContextPreparation.CreateAsync(stage, runtime, source, format,
            format == "xlsx" ? "cached" : "none", CancellationToken.None);
        var work = prepared.Work; var directory = work.DirectoryPath;
        var path = Path.Combine(stage, work.ItemId.ToString("N") + ".ownership");
        var journal = prepared.Journal;
        var contextSeconds = preparationTimer.Elapsed.TotalSeconds;
        var owner = OfficeSandboxOwner.Create(journal);
        var profileSeconds = preparationTimer.Elapsed.TotalSeconds - contextSeconds;
        WorkerClient? client = null;
        try
        {
            var state = new State(work, path, owner.Sid); Publish(Path.Combine(stage, "context.json"), state);
            owner.GrantDirectory(runtime, false);
            var runtimeGrantSeconds = preparationTimer.Elapsed.TotalSeconds - contextSeconds - profileSeconds;
            owner.GrantDirectory(Path.Combine(directory, "input"), false);
            foreach (var name in new[] { "output", "profile", "temp" }) owner.GrantDirectory(Path.Combine(directory, name), true);
            Publish(Path.Combine(stage, "preparation-timing.json"), new { ContextSeconds = contextSeconds, ProfileSeconds = profileSeconds,
                RuntimeGrantSeconds = runtimeGrantSeconds, TotalPreparationSeconds = preparationTimer.Elapsed.TotalSeconds });
            client = new WorkerClient(worker, Path.Combine(stage, "worker-scratch"));
            var started = DateTime.UtcNow;
            var operation = client.ExportOfficeAsync(work, CancellationToken.None, journal.RecordEngineIntent);
            var observation = await OfficeWorkerStopContracts.ObserveGrowthAsync(operation, work, owner.Sid, worker, started);
            using var native = observation.Process;
            using var process = Process.GetProcessById(client.ProcessId!.Value);
            Publish(Path.Combine(stage, "ready.json"), new Ready(state, Identity(process), Identity(native), observation.FirstBytes, observation.SecondBytes));
            var candidate = await operation;
            Publish(Path.Combine(stage, "completed.json"), candidate);
            await Task.Delay(Timeout.Infinite);
        }
        finally
        {
            if (client is not null) await client.DisposeAsync();
            if (journal.Changes.Any(change => change.Step == OfficeOwnershipStep.EngineIntent)) journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
            await owner.DisposeAsync();
        }
    }

    internal static async Task RunAsync(string workerPath, string largeFixtures, string ordinaryFixtures, string evidence,
        bool startupRecovery = false, string? applicationHost = null)
    {
        RequireStage(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Use fresh Office owner-loss evidence.");
        Directory.CreateDirectory(evidence);
        var checks = new List<string>(); var stops = new List<object>();
        var runtime = Path.Combine(Path.GetDirectoryName(workerPath)!, "office-engine");
        foreach (var (format, name) in new[] { ("docx", "Word \u00fc.docx"), ("xlsx", "Excel \u00fc.xlsx"), ("pptx", "PowerPoint \u00fc.pptx") })
        {
            var applicationRoot = Path.Combine(evidence, format); Directory.CreateDirectory(applicationRoot);
            var stage = applicationHost is null ? applicationRoot : Path.Combine(applicationRoot, "WorkerScratch", "OfficeContexts");
            Directory.CreateDirectory(stage);
            var source = Path.Combine(largeFixtures, name); var originalHash = Hash(source); var originalTime = File.GetLastWriteTimeUtc(source);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "--office-owner-loss-child", workerPath, source, format, stage }) start.ArgumentList.Add(value);
            using var owner = Process.Start(start) ?? throw new IOException("Office owner-loss child did not start.");
            _ = owner.Handle; var ownerIdentity = Identity(owner);
            Process? worker = null, host = null;
            OfficeOwnershipJournal? journal = null;
            OfficeSandboxOwner? recovered = null;
            // Runtime grant setup precedes the separate 100-second renderer
            // observation budget. Keep that setup inside a bounded outer wait.
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                var readyPath = Path.Combine(stage, "ready.json");
                while (!File.Exists(readyPath) && !owner.HasExited) await Task.Delay(5, deadline.Token);
                if (!File.Exists(readyPath)) throw new IOException("Office owner exited before growing-output observation: " + format);
                var ready = JsonSerializer.Deserialize<Ready>(File.ReadAllText(readyPath))!;
                worker = OpenProcess(ready.Worker); host = OpenProcess(ready.Host);
                Check(ready.FirstBytes > 0 && ready.SecondBytes > ready.FirstBytes &&
                    OfficeWorkerStopContracts.AppContainerSid(host) == ready.State.Sid &&
                    string.Equals(host.MainModule?.FileName, Path.Combine(runtime, "ContextSuite.OfficeHost.exe"), StringComparison.OrdinalIgnoreCase),
                    format + ": growing PDF belongs to the exact isolated native host");
                Check(!owner.HasExited && !worker.HasExited && !host.HasExited && !File.Exists(Path.Combine(stage, "completed.json")),
                    format + ": owner-only crash targets live owner, worker and renderer before completion");
                owner.Kill(); await owner.WaitForExitAsync(deadline.Token);
                if (startupRecovery)
                {
                    if (applicationHost is not null)
                    {
                        await RecoverApplicationAsync(applicationHost, workerPath, applicationRoot, deadline.Token);
                        Check(true, format + ": actual application startup recovers the abandoned context and exits after cleanup");
                    }
                    else
                    {
                        var report = await OfficeRecoveryCoordinator.RecoverAsync(stage, runtime, CancellationToken.None);
                        Check(!report.Incomplete && report.Entries.Count == 1 && report.Entries[0].Path == ready.State.Journal &&
                            report.Entries[0].State == OfficeRecoveryState.Recovered && report.NeedsAttention,
                            format + ": startup coordinator discovers and recovers the exact abandoned context");
                        Publish(Path.Combine(stage, "startup-recovery.json"), report);
                    }
                    journal = OpenJournal(stage, runtime, ready.State);
                }
                else (journal, recovered) = await RecoverAsync(stage, runtime, ready.State);
                Check(owner.HasExited && journal.Owner.OwnerProcessId == ownerIdentity.Id && journal.Owner.OwnerStartUtcTicks == ownerIdentity.StartUtcTicks,
                    format + ": reopened journal matches the terminated application owner");
                Check(worker.HasExited && host.HasExited, format + (startupRecovery ?
                    ": startup coordinator returns after worker and native host exit" : ": recovered ownership confirms worker and native host exit before cleanup"));
                var partialHash = Hash(ready.State.Work.CandidatePath); var partialBytes = new FileInfo(ready.State.Work.CandidatePath).Length;
                if (recovered is not null) await recovered.DisposeAsync();
                using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + ready.State.Sid);
                Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(ready.State.Work.ProfileName)),
                    format + ": recovered application removes its recorded native profile");
                Check(Hash(source) == originalHash && File.GetLastWriteTimeUtc(source) == originalTime && Hash(ready.State.Work.SourcePath) == originalHash,
                    format + ": original and read-only snapshot survive owner loss unchanged");
                Check((startupRecovery ? partialBytes >= ready.SecondBytes : Hash(ready.State.Work.CandidatePath) == partialHash) &&
                    !File.Exists(Path.Combine(stage, "completed.json")),
                    format + ": cleanup retains the interrupted PDF without accepting it as a completed export");
                foreach (var path in new[] { ready.State.Work.SourcePath, ready.State.Work.CandidatePath })
                    using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                Check(true, format + ": source snapshot and partial PDF are exclusively readable after recovery");
                Check(journal.Changes.Count == 27 && journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted &&
                    journal.Changes.Count(change => change.Step == OfficeOwnershipStep.ProcessesStopped) == 1,
                    format + ": reopened journal completes ordered process and profile cleanup exactly once");
                if (startupRecovery)
                {
                    journal.Dispose(); journal = null;
                    var journalHash = Hash(ready.State.Journal);
                    var repeat = await OfficeRecoveryCoordinator.RecoverAsync(stage, runtime, CancellationToken.None);
                    Check(!repeat.NeedsAttention && repeat.Entries.Count == 1 && repeat.Entries[0].State == OfficeRecoveryState.AlreadyClean &&
                        Hash(ready.State.Journal) == journalHash && Hash(ready.State.Work.CandidatePath) == partialHash,
                        format + ": repeated startup scan preserves the completed journal and interrupted output");
                }
                stops.Add(new { Format = format, ready.State.Work, ready.State.Sid, ready.State.Journal, ready.FirstBytes, ready.SecondBytes,
                    Owner = ownerIdentity, ready.Worker, ready.Host, PartialBytes = partialBytes, PartialSha256 = partialHash, Removed = true });
                Publish(Path.Combine(evidence, "progress.json"), new { Stops = stops });
                Console.WriteLine("Recovered active Office owner loss: " + format);
            }
            finally
            {
                if (!owner.HasExited) owner.Kill();
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await owner.WaitForExitAsync(shutdown.Token);
                // A failed observation still owns its generated profile. Recover
                // only its validated context record; never sweep other profiles.
                try
                {
                    if (recovered is not null) await recovered.DisposeAsync();
                    else await RecoverStageAsync(workerPath, stage);
                }
                finally { journal?.Dispose(); worker?.Dispose(); host?.Dispose(); }
            }
        }
        var following = Path.Combine(evidence, "following");
        await OfficeWorkerContracts.RunAsync(workerPath, ordinaryFixtures, following);
        using var followingResult = JsonDocument.Parse(File.ReadAllText(Path.Combine(following, "results.json")));
        checks.AddRange(followingResult.RootElement.GetProperty("Checks").EnumerateArray().Select(item => item.GetString()!));
        Publish(Path.Combine(evidence, "results.json"), new { Passed = true,
            Modes = new[] { applicationHost is not null ? "app-recovery" : startupRecovery ? "startup-recovery" : "owner-loss" }, Checks = checks,
            Stops = stops, Reports = followingResult.RootElement.GetProperty("Reports") });
        Console.WriteLine("Office owner-loss checks passed: " + checks.Count);

        void Check(bool condition, string name) { if (!condition) throw new IOException(name); checks.Add(name); }
    }

    private static async Task RecoverApplicationAsync(string executable, string worker, string root, CancellationToken token)
    {
        var requests = Path.Combine(root, "ActivationCleanup"); Directory.CreateDirectory(requests);
        var id = Guid.NewGuid(); var activation = Path.Combine(requests, id.ToString("D") + ".request");
        File.WriteAllText(activation, $"ContextSuiteActivation/1\nrequestId={id:D}\noperation=convert\naction=settings\npathCount=0\n");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--activation-file"); start.ArgumentList.Add(activation);
        start.Environment["CONTEXTSUITE_TEST_ROOT"] = root;
        start.Environment["CONTEXTSUITE_TEST_WORKER"] = worker;
        start.Environment["CONTEXTSUITE_TEST_OFFICE_LIFECYCLE"] = "native-recovery";
        using var app = Process.Start(start) ?? throw new IOException("Recovery application did not start.");
        var stdout = app.StandardOutput.ReadToEndAsync(); var stderr = app.StandardError.ReadToEndAsync();
        try { await app.WaitForExitAsync(token); }
        finally
        {
            if (!app.HasExited) { app.Kill(true); await app.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(root, "application-stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(root, "application-stderr.log"), await stderr);
        }
        if (app.ExitCode != 0) throw new IOException("Recovery application failed; retain its lifecycle evidence.");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "lifecycle.json")));
        if (!report.RootElement.GetProperty("Passed").GetBoolean() || report.RootElement.GetProperty("Mode").GetString() != "native-recovery" ||
            report.RootElement.GetProperty("Checks").GetArrayLength() != 6)
            throw new IOException("Missing complete native application recovery evidence.");
    }

    internal static async Task RecoverStageAsync(string worker, string stage)
    {
        RequireStage(stage);
        var path = Path.Combine(stage, "context.json");
        if (!File.Exists(path)) return;
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path))!;
        var runtime = Path.Combine(Path.GetDirectoryName(worker)!, "office-engine");
        using var journal = OpenJournal(stage, runtime, state);
        if (journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted) return;
        var recovered = await OfficeSandboxOwner.RecoverAsync(journal);
        await recovered.DisposeAsync();
    }

    private static async Task<(OfficeOwnershipJournal Journal, OfficeSandboxOwner Owner)> RecoverAsync(string stage, string runtime, State state)
    {
        for (var attempt = 0; ; attempt++)
        {
            OfficeOwnershipJournal? journal = null;
            try
            {
                journal = OpenJournal(stage, runtime, state);
                return (journal, await OfficeSandboxOwner.RecoverAsync(journal));
            }
            catch (Exception error) when (attempt < 49 && (error is Win32Exception native && native.NativeErrorCode is 32 or 33 ||
                error is IOException && (error.HResult & 0xffff) is 32 or 33))
            { journal?.Dispose(); await Task.Delay(100); }
            catch { journal?.Dispose(); throw; }
        }
    }
    private static OfficeOwnershipJournal OpenJournal(string stage, string runtime, State state)
    {
        state.Work.Validate();
        if (Path.GetDirectoryName(state.Journal) != stage || Path.GetDirectoryName(state.Work.DirectoryPath) != stage)
            throw new IOException("Office recovery context differs from its owned stage.");
        var journal = OfficeOwnershipJournal.Open(state.Journal, stage, runtime);
        if (journal.Owner.Work == state.Work) return journal;
        journal.Dispose(); throw new IOException("Office recovery work differs from its journal.");
    }
    private static ProcessIdentity Identity(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks);
    private static Process OpenProcess(ProcessIdentity identity)
    {
        var process = Process.GetProcessById(identity.Id); _ = process.Handle;
        if (Identity(process) == identity) return process;
        process.Dispose(); throw new IOException("Observed Office process identity changed.");
    }
    private static void RequireStage(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use an owned Office worker test stage.");
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Publish(string path, object value)
    {
        File.WriteAllText(path + ".pending", JsonSerializer.Serialize(value));
        File.Move(path + ".pending", path, overwrite: true);
    }
}
