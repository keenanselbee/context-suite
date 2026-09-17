using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class HeifWorkerContracts
{
    public static async Task RunAsync(string executable, string withoutEngine, string damagedEngine, string bridgeEvidence, string root, string[] fixtures)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a new absolute evidence directory.");
        Directory.CreateDirectory(root);
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        var reports = new List<object>();
        var snapshots = new Dictionary<string, (string Hash, DateTime Written)>();
        var sources = new List<ImageSourceFacts>();
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        foreach (var folder in fixtures)
        {
            using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "results.json")));
            Check(baseline.RootElement.GetProperty("Passed").GetBoolean(), "Passing authored fixture baseline");
            var depth = baseline.RootElement.GetProperty("BitDepth").GetInt32();
            foreach (var row in baseline.RootElement.GetProperty("Decoded").EnumerateArray())
            {
                var name = row.GetProperty("file").GetString()!;
                var sourcePath = Path.Combine(folder, "fixtures", name);
                var path = Path.Combine(originals, depth + "-" + name);
                Check(Hash(sourcePath) == baseline.RootElement.GetProperty("Fixtures").GetProperty(sourcePath).GetString(), "Bound fixture identity");
                File.Copy(sourcePath, path);
                snapshots.Add(path, (Hash(path), File.GetLastWriteTimeUtc(path)));
                if (name.Contains("-pq.") || name.Contains("-hlg.") || name.Contains("-collection."))
                {
                    await RejectAsync(path, ImageFailure.UnsupportedInput);
                    continue;
                }
                var facts = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
                Check(facts.UnsupportedReason is null && facts.BitDepth == depth && facts.Orientation == 1 &&
                    facts.Width == row.GetProperty("facts").GetProperty("width").GetUInt32() &&
                    facts.Height == row.GetProperty("facts").GetProperty("height").GetUInt32(), "Worker geometry and precision " + Path.GetFileName(path));
                sources.Add(facts);
                foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg })
                {
                    facts = facts with { ItemId = Guid.NewGuid() };
                    var options = new ImageConversionOptions(target, Metadata: ImageMetadataMode.Automatic,
                        MatteRgb: target == ImageFormat.Jpeg ? 0xffffffu : null);
                    var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new(OutputDirectory: output)));
                    var confirmed = plan.Confirm(true, false, false);
                    var reservation = await publisher.ReserveAsync(new(facts.ItemId, path, options.Extension, plan.Settings));
                    var result = await worker.ConvertAsync(new(confirmed.Plan.Items[0], options, reservation.TemporaryPath), default);
                    Check(!File.Exists(reservation.Record.OutputPath) && result.Validation.MatchesPlan, "Worker validates without publishing " + target);
                    var published = await publisher.PublishAsync(reservation, result.Validation);
                    var reopened = await worker.ProbeAsync(new(Guid.NewGuid(), published.OutputPath!), default);
                    Check(published.Outcome == PublicationOutcome.CopyCreated && reopened.Format == target &&
                        reopened.Width == facts.Width && reopened.Height == facts.Height && reopened.ProfileNames.Contains("icc") &&
                        reopened.BitDepth == (target == ImageFormat.Png && depth > 8 ? 16 : 8), "Application publishes verified named copy " + target);
                    reports.Add(new { Source = path, Target = target.ToString(), published.OutputPath, result.OutputBytes });
                }
            }
        }
        var valid = sources.First(source => source.Format == ImageFormat.Heic);
        using (var boundaries = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bridgeEvidence, "results.json"))))
        {
            Check(boundaries.RootElement.GetProperty("Passed").GetBoolean(), "Passing malformed-grid baseline");
            foreach (var row in boundaries.RootElement.GetProperty("GridRefusals").EnumerateArray())
            {
                var path = row.GetProperty("file").GetString()!;
                Check(Hash(path) == row.GetProperty("sha256").GetString(), "Malformed-grid identity");
                var process = worker.ProcessId;
                try { await worker.ProbeAsync(new(Guid.NewGuid(), path), default); throw new InvalidOperationException("Malformed grid accepted"); }
                catch (MediaWorkerException error) { Check(error.Failure is ImageFailure.InvalidInput or ImageFailure.UnsupportedInput or ImageFailure.ResourceLimit, "Typed malformed-grid refusal"); }
                await worker.ProbeAsync(new(Guid.NewGuid(), valid.Path), default);
                Check(worker.ProcessId == process && Hash(path) == row.GetProperty("sha256").GetString(), "Same worker recovers with malformed source unchanged");
            }
        }
        var png = Path.Combine(originals, "ordinary.png"); ImageWorkerContracts.WritePng(png);
        snapshots.Add(png, (Hash(png), File.GetLastWriteTimeUtc(png)));
        foreach (var unavailable in new[] { withoutEngine, damagedEngine })
        {
            await using var absent = new WorkerClient(unavailable, Path.Combine(root, "unavailable-engine-scratch"));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { await absent.ProbeAsync(new(Guid.NewGuid(), valid.Path), default); throw new InvalidOperationException("Absent decoder accepted"); }
                catch (MediaWorkerException error) { Check(error.Failure == ImageFailure.EngineUnavailable && error.Message.Contains("Repair or update"), "Missing or altered optional engine gives an actionable typed failure"); }
            }
            var pid = absent.ProcessId;
            Check((await absent.ProbeAsync(new(Guid.NewGuid(), png), default)).Format == ImageFormat.Png && absent.ProcessId == pid,
                "Unavailable HEIF engine does not poison ordinary image processing");
            using var process = Process.GetProcessById(pid!.Value);
            Check(!process.Modules.Cast<ProcessModule>().Any(module => module.ModuleName is "ContextSuite.Heif.Native.dll" or "heif.dll" or "libde265.dll"),
                "Unavailable or altered decoder is refused before native loading");
        }
        var settings = new SuiteSettings { Convert = new(OutputDirectory: Path.Combine(root, "direct-output")) };
        Directory.CreateDirectory(settings.Convert.OutputDirectory!);
        var directSources = new[] { valid.Path, sources.First(source => source.Format == ImageFormat.Avif && !source.HasTransparency).Path, png };
        await using (var vm = Create("quiet", new OutputPublisher(Path.Combine(root, "quiet-records"), new NoRecycle())))
        {
            var quiet = new QuietWorkflow(); var starts = 0; var ends = 0;
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Routine PNG conversion requested a planner");
            vm.QuickBatchStarted += (request, _) => { starts++; quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow); };
            vm.QuickBatchCompleted += (request, rows) => { ends++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [.. directSources])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Count == 3 && vm.Rows.All(row => row.Result.State is OperationState.Succeeded or OperationState.Unchanged) &&
                starts == 1 && ends == 1 && !quiet.NeedsAttention && !vm.HasProblems, "Mixed direct PNG command stays quiet and preserves existing PNG");
            var previousOutput = vm.Rows[0].OutputPath;
            var previousHash = Hash(previousOutput);
            var unsupported = snapshots.Keys.First(path => path.EndsWith("-pq.heic", StringComparison.Ordinal));
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [unsupported, valid.Path])); await vm.WaitForIdleAsync();
            Check(vm.Rows[3].Result.State == OperationState.Unsupported && vm.Rows[4].Result.State == OperationState.Succeeded && quiet.NeedsAttention,
                "Unsupported HDR is visible while later mixed-selection input succeeds");
            Check(vm.Rows[4].OutputPath != previousOutput && Hash(previousOutput) == previousHash, "Repeated conversion preserves the prior named output");
        }
        await using (var vm = Create("matte", new OutputPublisher(Path.Combine(root, "matte-records"), new NoRecycle())))
        {
            var prompts = 0;
            vm.ConversionRequested += async (planner, token) =>
            {
                prompts++; await planner.InitializeAsync();
                Check(planner.Target == ImageFormat.Jpeg && planner.TargetFixed && planner.NeedsBackgroundChoice && !planner.CanConfirm,
                    "Transparent HEIF JPEG command requires its matte choice");
                planner.MatteChoice = 1; planner.WarningsAcknowledged = true;
                await planner.RefreshPreviewAsync(); token.ThrowIfCancellationRequested();
                ConfirmedImageBatch? confirmed = null; planner.Confirmed += value => confirmed = value;
                Check(planner.CanConfirm, "Real-worker matte preview enables confirmation");
                planner.ConfirmCommand.Execute(null); return confirmed;
            };
            vm.Admit(new(Guid.NewGuid(), "convert", "jpeg", [sources.First(source => source.HasTransparency).Path]));
            await vm.WaitForIdleAsync();
            Check(prompts == 1 && vm.Rows.Single().Result.State == OperationState.Succeeded, "Explicit matte JPEG command publishes successfully");
        }
        await using (var vm = Create("retry", new OutputPublisher(Path.Combine(root, "retry-records"), new NoRecycle(), io: new FailPublication())))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "png", [valid.Path])); await vm.WaitForIdleAsync();
            Check(vm.Rows.Single().Result.State == OperationState.Failed && vm.CanRetry, "Failed publication remains retryable");
            var retryOutput = Path.Combine(root, "changed-retry-output"); Directory.CreateDirectory(retryOutput);
            vm.Settings = settings with { Convert = new(OutputDirectory: retryOutput) };
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            Check(vm.Rows.Count == 2 && vm.Rows.Last().Result.State == OperationState.Succeeded && !vm.CanRetry &&
                Path.GetDirectoryName(vm.Rows.Last().OutputPath) == retryOutput, "Retry publishes the original command with current settings");
        }
        var optionsForFault = new ImageConversionOptions(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic);
        var stale = valid with { ItemId = Guid.NewGuid(), Sha256 = new string('0', 64) };
        var stalePlan = ImageConversionPlanner.Create(Guid.NewGuid(), [stale], optionsForFault, new("convert", new(OutputDirectory: output)));
        var staleReservation = await publisher.ReserveAsync(new(stale.ItemId, stale.Path, "png", stalePlan.Settings));
        try
        {
            await worker.ConvertAsync(new(stalePlan.Confirm(true, false, false).Plan.Items[0], optionsForFault, staleReservation.TemporaryPath), default);
            throw new InvalidOperationException("Stale HEIF plan accepted");
        }
        catch (MediaWorkerException error) { Check(error.Failure == ImageFailure.SourceChanged, "Changed HEIF source plan gets a typed refusal"); }
        await publisher.AbandonAsync(staleReservation, false);
        Check(!File.Exists(staleReservation.TemporaryPath) && !File.Exists(staleReservation.Record.OutputPath), "Stale HEIF work publishes nothing");
        var faultPlan = ImageConversionPlanner.Create(Guid.NewGuid(), sources.Take(2).Select(source => source with { ItemId = Guid.NewGuid() }),
            optionsForFault, new("convert", new(OutputDirectory: output)));
        var executor = new ImageBatchExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "fault-trial.json")));
        var killed = false;
        var faultResults = await executor.ExecuteAsync(faultPlan.Confirm(true, false, false), (_, result) =>
        {
            if (killed || result.Message != "Encoding and validating output") return;
            using var owned = Process.GetProcessById(worker.ProcessId!.Value);
            owned.Kill(true); owned.WaitForExit(5000); killed = true;
        }, default);
        Check(killed && faultResults.Results[0].State == OperationState.Failed && faultResults.Results[1].State == OperationState.Succeeded,
            "Owned worker death at dispatch fails one item and later HEIF restarts successfully");
        using (var cancellation = new CancellationTokenSource())
        {
            faultPlan = ImageConversionPlanner.Create(Guid.NewGuid(), sources.Take(2).Select(source => source with { ItemId = Guid.NewGuid() }),
                optionsForFault, new("convert", new(OutputDirectory: output)));
            var cancelled = await executor.ExecuteAsync(faultPlan.Confirm(true, false, false), (_, result) =>
            { if (result.Message == "Encoding and validating output") cancellation.Cancel(); }, cancellation.Token);
            Check(cancelled.Results.All(result => result.State == OperationState.Cancelled), "Cancellation at dispatch cancels pending HEIF work");
        }
        Check(snapshots.All(pair => Hash(pair.Key) == pair.Value.Hash && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written), "All source bytes and write times preserved");
        Check(!Directory.EnumerateFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(), "No abandoned output reservations remain");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Outputs = reports, Details = checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} HEIF worker/application checks; {reports.Count} outputs published.");

        MainViewModel Create(string name, OutputPublisher outputPublisher) => new(new WorkerClient(executable, Path.Combine(root, name + "-workers")),
            settings, outputPublisher, new LocalTrialStore(Path.Combine(root, name + "-trial.json")));
        async Task RejectAsync(string path, ImageFailure expected)
        {
            try { await worker.ProbeAsync(new(Guid.NewGuid(), path), default); throw new InvalidOperationException("Unsupported image accepted"); }
            catch (MediaWorkerException error) { Check(error.Failure == expected, "Typed feature refusal " + Path.GetFileName(path)); }
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); }
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HEIF integration must never recycle originals.");
    }
    private sealed class FailPublication : PublicationIo
    {
        private bool _failed;
        public override void Checkpoint(PublicationStage stage)
        {
            if (stage != PublicationStage.Validated || _failed) return;
            _failed = true; throw new IOException("Authored publication failure.");
        }
    }
}
