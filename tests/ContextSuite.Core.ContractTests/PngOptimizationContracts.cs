using System.IO.Compression;
using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class PngOptimizationContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var quiet = new QuietWorkflow();
        var quietId = Guid.NewGuid();
        quiet.Begin(quietId, true, now);
        check(!quiet.ShowProgress(now.AddMilliseconds(1999)) && quiet.ShowProgress(now.AddSeconds(2)), "quiet: delayed progress boundary");
        check(quiet.Complete(quietId, [new("source", OperationState.Unchanged, "No smaller result")]) &&
            !quiet.Complete(quietId, [new("source", OperationState.Succeeded, "Saved")]) && !quiet.ShowProgress(now.AddHours(1)), "quiet: unchanged succeeds, exactly one signal, timer cleared");
        foreach (var state in new[] { OperationState.Failed, OperationState.Unsupported, OperationState.Cancelled })
        {
            quiet = new(); quiet.Begin(quietId, true, now);
            check(!quiet.Complete(quietId, [new("source", state, "Outcome")]) && quiet.NeedsAttention == (state != OperationState.Cancelled), "quiet: no chime for " + state);
        }
        quiet = new(); quiet.Begin(quietId, false, now);
        check(!quiet.Complete(quietId, [new("source", OperationState.Succeeded, "Saved")]) && !quiet.NeedsAttention, "quiet: mute succeeds without attention");
        foreach (var action in new[] { "auto", "lossless", "balanced", "smallest" })
        {
            var request = new OperationRequest(Guid.NewGuid(), "optimize", action, [Path.GetFullPath("test.png")]);
            request.Validate(false);
            check(request.IsQuickOptimization, "quick: accepted explicit preset " + action);
            Reject(() => (request with { Operation = "convert" }).Validate(false), "preset under wrong operation");
        }
        foreach (var action in new[] { "png", "jpeg", "webp", "bmp", "tga" })
        {
            var request = new OperationRequest(Guid.NewGuid(), "convert", action, [Path.GetFullPath("test.png")]);
            request.Validate(false);
            check(request.IsQuickConversion && request.IsQuickAction && !request.IsQuickOptimization, "quick: accepted explicit conversion " + action);
            Reject(() => (request with { Operation = "optimize" }).Validate(false), "conversion under wrong operation");
        }
        var root = Path.Combine(scratch, "png-planner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var directSource = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "direct.png"), new string('0', 64), 100,
            ImageFormat.Png, 10, 20, 8, 1, false, false, "sRGB", []);
        ImageBatchPlan QuickPlan(ImageSourceFacts facts, ImageFormat target = ImageFormat.WebP) =>
            ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(target, WebPLossless: target == ImageFormat.WebP), new("convert", new()));
        check(QuickPlan(directSource).CanConfirmQuickCopy && QuickPlan(directSource with { Resolution = new(300,300,2) }).CanConfirmQuickCopy,
            "quick Convert: preservation and resolution-only normalization need no dialog");
        check(QuickPlan(directSource with { Format = ImageFormat.Jpeg, IsLossy = true }, ImageFormat.Png).CanConfirmQuickCopy &&
            QuickPlan(directSource, ImageFormat.Bmp).CanConfirmQuickCopy, "quick Convert: informational format notices need no consent");
        foreach (var guarded in new[] { directSource with { ProfileNames = ["exif"] }, directSource with { HasOtherMetadata = true },
            directSource with { BitDepth = 16 } })
            check(!QuickPlan(guarded).CanConfirmQuickCopy, "quick Convert: metadata or precision consequence requires review");
        check(!QuickPlan(directSource with { HasTransparency = true }, ImageFormat.Jpeg).CanConfirmQuickCopy &&
            !QuickPlan(directSource with { Format = ImageFormat.WebP, IsLossy = true }, ImageFormat.Jpeg).CanConfirmQuickCopy,
            "quick Convert: matte and lossy transcode never bypassed");
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "source.png"), new string('0', 64), 100,
            ImageFormat.Png, 10, 20, 16, 6, true, false, "sRGB", ["exif"], HasOtherMetadata: true);
        var plan = PngOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new()));
        check(plan.Confirm(false, false).Plan == plan && plan.Items[0].CanExecute, "PNG plan: preserves metadata and orientation without conversion normalization");
        Reject(() => PngOptimizationPlan.Create(Guid.NewGuid(), [source], new("convert", new())), "wrong settings owner");
        Reject(() => PngOptimizationPlan.Create(Guid.NewGuid(), [source, source], new("optimize", new())), "duplicate item identity");
        Reject(() => PngOptimizationPlan.Create(Guid.NewGuid(), [], new("optimize", new())), "empty selection");
        Reject(() => (plan with { ReplaceOriginal = true }).Confirm(true, true), "replacement without permission");
        var replacement = PngOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new(true)), true);
        Reject(() => replacement.Confirm(false, true), "replacement without confirmation");
        Reject(() => replacement.Confirm(true, false), "replacement without platform verification");
        check(replacement.Confirm(true, true).Plan.ReplaceOriginal, "PNG plan: explicit permitted replacement");
        var mixed = PngOptimizationPlan.Create(Guid.NewGuid(), [source, source with { ItemId = Guid.NewGuid(), Format = ImageFormat.Jpeg }], new("optimize", new()));
        check(mixed.Items.Count(i => i.CanExecute) == 1 && mixed.Confirm(false, false).Plan.Items.Length == 2, "PNG plan: mixed formats explicitly unsupported");
        Reject(() => (mixed with { Items = [mixed.Items[1] with { BlockReason = null }] }).Confirm(false, false), "forged executable item");
        new WorkerCommand(1, Guid.NewGuid(), "png-optimize", Optimization: new(source, Path.Combine(root, "temp"))).Validate();
        check(true, "PNG IPC: typed optimization command admitted");
        Reject(() => new WorkerCommand(1, Guid.NewGuid(), "image-convert", Optimization: new(source, "temp")).Validate(), "mismatched worker operation");
        Reject(() => new WorkerCommand(1, Guid.NewGuid(), "png-probe", Probe: new(Guid.NewGuid(), source.Path), Optimization: new(source, "temp")).Validate(), "contradictory worker payload");

        var trialPath = Path.Combine(root, "trial.json");
        var trial = new LocalTrialStore(trialPath);
        var viewModel = new OptimizationViewModel(trial, plan.BatchId, [new(source.ItemId, source.Path, source, null)], plan.Settings, false);
        check(!viewModel.CanConfirm, "PNG UI: waits for access status");
        await viewModel.InitializeAsync();
        check(viewModel.CanConfirm && !File.Exists(trialPath) && viewModel.Rows[0].ProposedOutput.EndsWith("source - Optimized.png"), "PNG UI: safe named copy, reading planner does not start trial");
        check(viewModel.SelectedDetails.Contains("source - Optimized.png") && viewModel.SelectedStatus.Contains("Lossless") &&
            viewModel.OutputNotice.Contains("beside") && viewModel.ReplacementNotice.Contains("unavailable"),
            "PNG UI: selected detail retains destination, preset and replacement explanation");
        viewModel.SelectedIndex = -1;
        check(viewModel.SelectedDetails == "" && viewModel.SelectedStatus.Contains("Select a file"), "PNG UI: empty selection is safe");
        viewModel.SelectedIndex = 0;
        ConfirmedPngOptimization? confirmed = null;
        viewModel.Confirmed += value => confirmed = value;
        viewModel.ConfirmCommand.Execute(null);
        check(confirmed is not null && !File.Exists(trialPath), "PNG UI: confirmation is immutable, admission alone starts trial");
        check(viewModel.Preset == PngOptimizationPreset.Lossless, "PNG UI: Lossless remains default");
        viewModel.Preset = PngOptimizationPreset.Balanced;
        check(viewModel.CanConfirm && confirmed!.Plan.Preset == PngOptimizationPreset.Lossless,
            "PNG UI: lossy-ineligible source uses lossless fallback; confirmed snapshot unchanged");
        var lossySource = source with { BitDepth = 8, PngLossyBlockReason = null };
        var lossyView = new OptimizationViewModel(trial, Guid.NewGuid(), [new(lossySource.ItemId, lossySource.Path, lossySource, null)], plan.Settings, false);
        await lossyView.InitializeAsync();
        foreach (var preset in new[] { PngOptimizationPreset.Balanced, PngOptimizationPreset.Smallest })
        {
            lossyView.Preset = preset;
            check(lossyView.CanConfirm && lossyView.PolicyDescription.Contains("lossy") && lossyView.Plan!.Preset == preset,
                "PNG UI: explicit lossy choice and warning: " + preset);
            var snapshot = lossyView.Plan!.Confirm(false, false);
            check(snapshot.Plan.SelectedPolicy == (preset == PngOptimizationPreset.Balanced ? PngOptimizationPlan.BalancedPolicy : PngOptimizationPlan.SmallestPolicy),
                "PNG plan: immutable versioned precision policy: " + preset);
        }
        Reject(() => PngOptimizationPlan.Create(Guid.NewGuid(), [lossySource], plan.Settings, preset: (PngOptimizationPreset)999), "unknown preset");
        var replacementView = new OptimizationViewModel(trial, Guid.NewGuid(), [new(lossySource.ItemId, lossySource.Path, lossySource, null)], new("optimize", new(true)), true);
        await replacementView.InitializeAsync();
        replacementView.ReplaceOriginal = true;
        replacementView.ReplacementConfirmed = true;
        check(replacementView.CanConfirm, "PNG UI: explicitly permitted replacement can be confirmed");
        check(replacementView.OutputNotice.Contains("Replace originals") && replacementView.SelectedDetails.Contains("Replace only after validation") &&
            !replacementView.SelectedDetails.Contains("Existing names add"), "PNG UI: replacement notices do not promise a numbered copy");
        replacementView.Preset = PngOptimizationPreset.Smallest;
        check(!replacementView.ReplacementConfirmed && !replacementView.CanConfirm, "PNG UI: changed loss policy requires fresh replacement consent");
        var admission = await trial.AdmitAsync(confirmed!);
        check(admission.IsAllowed && File.Exists(trialPath), "PNG trial: first optimization starts the same 72-hour trial");
        viewModel.ReplaceOriginal = true;
        check(!viewModel.CanConfirm, "PNG UI: unauthorized replacement cannot execute");

        void Reject(Action action, string name)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, "PNG rejects " + name); return; }
            throw new InvalidOperationException("PNG accepted " + name);
        }
    }

    public static async Task RunWorkerAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "png-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"));
        var sourcePath = Path.Combine(root, "sample 空白.png");
        ImageWorkerContracts.WritePng(sourcePath, transparent: true, compression: CompressionLevel.NoCompression);
        var original = SHA256.HashData(File.ReadAllBytes(sourcePath));
        var facts = await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), default, forOptimization: true);
        var clock = new TestClock();
        var trial = new LocalTrialStore(Path.Combine(root, "trial.json"), clock);
        var publisher = new OutputPublisher(Path.Combine(root, "publications"), new NoRecycle(), false);
        var executor = new PngOptimizationExecutor(worker, publisher, trial);
        var plan = PngOptimizationPlan.Create(Guid.NewGuid(), [facts, facts with { ItemId = Guid.NewGuid() }], new("optimize", new()));
        var execution = await executor.ExecuteAsync(plan.Confirm(false, false), (_, result) =>
        {
            if (result.State == OperationState.Succeeded) clock.Utc = clock.Utc.AddDays(4);
        }, default);
        check(execution.Admission.IsAllowed && execution.Results.All(result => result.State == OperationState.Succeeded), "PNG batch: both admitted items finish after trial expires");
        var first = execution.Results[0].Publication!;
        check(first.OutputBytes < first.SourceBytes && first.OutputPath!.EndsWith("sample 空白 - Optimized.png") &&
            execution.Results[1].Publication!.OutputPath!.EndsWith("sample 空白 - Optimized (2).png"), "PNG batch: smaller copies with Windows collision numbering");
        check(SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original), "PNG batch: original hash retained");
        check(execution.Results[0].Message.Contains("saved") && !Directory.EnumerateFiles(root, ".context-suite-*.tmp").Any(), "PNG batch: savings reported and temporary files cleaned");
        var declined = await executor.ExecuteAsync(plan.Confirm(false, false), null, default);
        check(!declined.Admission.IsAllowed && declined.Results.All(result => result.State == OperationState.Failed), "PNG batch: subsequent expired batch cannot execute");

        var activeTrial = new LocalTrialStore(Path.Combine(root, "second-trial.json"));
        var optimizedFacts = await worker.ProbeAsync(new(Guid.NewGuid(), first.OutputPath!), default, forOptimization: true);
        var noChange = PngOptimizationPlan.Create(Guid.NewGuid(), [optimizedFacts], new("optimize", new()));
        var stable = await new PngOptimizationExecutor(worker, publisher, activeTrial).ExecuteAsync(noChange.Confirm(false, false), null, default);
        check(stable.Results.Single().State == OperationState.Unchanged && !File.Exists(Path.Combine(root, "sample 空白 - Optimized - Optimized.png")), "PNG batch: no smaller result is unchanged, not a failure or extra file");
        var stale = PngOptimizationPlan.Create(Guid.NewGuid(), [facts with { ItemId = Guid.NewGuid(), Sha256 = new string('0', 64) }], new("optimize", new()));
        var staleResult = await new PngOptimizationExecutor(worker, publisher, activeTrial).ExecuteAsync(stale.Confirm(false, false), null, default);
        check(staleResult.Results.Single().State == OperationState.Failed && SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original), "PNG batch: stale planned source fails safely");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var cancelledPath = Path.Combine(root, "cancelled-trial.json");
            try
            {
                await new PngOptimizationExecutor(worker, publisher, new LocalTrialStore(cancelledPath)).ExecuteAsync(plan.Confirm(false, false), null, cancelled.Token);
                throw new InvalidOperationException("Cancelled PNG admission succeeded");
            }
            catch (OperationCanceledException) { check(!File.Exists(cancelledPath), "PNG batch: pre-cancelled admission never starts trial"); }
        }
        var corrupt = Path.Combine(root, "corrupt.png");
        File.WriteAllText(corrupt, "unsupported fixture");
        await using (var convertVm = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "direct-convert-scratch")),
            new SuiteSettings { Convert = new(true), PlayCompletionSound = false }, publisher,
            new LocalTrialStore(Path.Combine(root, "direct-convert-trial.json"))))
        {
            var prompts = 0;
            var completedBatches = 0;
            convertVm.ConversionRequested += (planner, _) =>
            {
                prompts++;
                check(planner.Target == ImageFormat.Jpeg && !planner.CanConfirm, "quick Convert: transparent JPEG asks for preselected matte decision");
                return Task.FromResult<ConfirmedImageBatch?>(null);
            };
            convertVm.QuickBatchCompleted += (_, _) => completedBatches++;
            var direct = new OperationRequest(Guid.NewGuid(), "convert", "webp", [sourcePath, corrupt]);
            convertVm.Admit(direct);
            convertVm.Admit(direct);
            convertVm.Admit(new(Guid.NewGuid(), "convert", "jpeg", [sourcePath]));
            convertVm.Admit(new(Guid.NewGuid(), "convert", "png", [sourcePath]));
            await convertVm.WaitForIdleAsync();
            check(prompts == 1 && completedBatches == 3 && convertVm.Rows.Count == 4 &&
                convertVm.Rows[0].Result.State == OperationState.Succeeded &&
                convertVm.Rows[1].Result.State == OperationState.Unsupported && convertVm.Rows[2].Result.State == OperationState.Cancelled,
                "quick Convert: safe WebP bypasses planner, mixed failure isolated, duplicate ignored, decision cancellation respected");
            check(convertVm.Rows[3].Result.State == OperationState.Unchanged && convertVm.Rows[3].OutputPath == "" &&
                convertVm.Summary.Contains("already in target format"), "quick Convert: matching target is a quiet no-op, not an error or extra copy");
            check(convertVm.Rows[0].Result.Publication?.OutputPath?.EndsWith(" - Converted.webp") == true &&
                SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original),
                "quick Convert: named validated copy despite replacement preference; original unchanged");
        }
        await using var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "ui-scratch")), publisher: publisher,
            trial: new LocalTrialStore(Path.Combine(root, "ui-trial.json")));
        var calls = 0;
        vm.OptimizationRequested += async (planner, token) =>
        {
            calls++;
            await planner.InitializeAsync(token);
            check(planner.Rows.Count == 2 && planner.Rows.Count(row => row.CanExecute) == 1, "PNG UI handoff: mixed selection remains one planner");
            ConfirmedPngOptimization? result = null;
            planner.Confirmed += value => result = value;
            planner.ConfirmCommand.Execute(null);
            return result;
        };
        vm.Admit(new(Guid.NewGuid(), "optimize", "choose-preset", [sourcePath, corrupt]));
        await vm.WaitForIdleAsync();
        check(calls == 1 && vm.Rows[0].Result.State == OperationState.Succeeded && vm.Rows[1].Result.State == OperationState.Unsupported &&
            vm.Summary.Contains("Optimization saved"), "PNG UI handoff: per-file outcomes and aggregate saved bytes");
        check(SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original), "PNG UI handoff: source still unchanged");
        await using (var quickVm = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "quick-scratch")),
            new SuiteSettings { Optimize = new(true), PlayCompletionSound = false }, publisher,
            new LocalTrialStore(Path.Combine(root, "quick-trial.json"))))
        {
            var started = 0;
            var finished = 0;
            quickVm.OptimizationRequested += (_, _) => throw new InvalidOperationException("Quick action opened a planner");
            quickVm.QuickBatchStarted += (_, _) => started++;
            quickVm.QuickBatchCompleted += (_, rows) =>
            {
                finished++;
                check(rows.All(row => !row.Settings.PlayCompletionSound), "quick: immutable mute snapshot");
            };
            var quick = new OperationRequest(Guid.NewGuid(), "optimize", "auto", [sourcePath, corrupt]);
            quickVm.Admit(quick);
            quickVm.Admit(quick);
            quickVm.Admit(new(Guid.NewGuid(), "optimize", "lossless", [sourcePath]));
            quickVm.Settings = new();
            await quickVm.WaitForIdleAsync();
            check(started == 2 && finished == 2 && quickVm.Rows.Count == 3 && !quickVm.IsBusy, "quick: duplicate IDs ignored, queued batches drain once");
            check(quickVm.Rows[0].Result.State == OperationState.Succeeded && quickVm.Rows[1].Result.State == OperationState.Unsupported &&
                quickVm.Rows[2].Result.State == OperationState.Succeeded, "quick: mixed selection does not block valid files or later batches");
            check(quickVm.Rows.Where(row => row.HasOutput).All(row => row.OutputPath != row.Path) &&
                SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original), "quick: copies even when replacement permitted, original untouched");
        }
        var precisionPath = Path.Combine(root, "precision.png");
        ImageInterruptionContracts.WriteNoisePng(precisionPath, 1024);
        var precisionFacts = await worker.ProbeAsync(new(Guid.NewGuid(), precisionPath), default, forOptimization: true);
        check(precisionFacts.PngLossyBlockReason is null, "PNG worker: lossy eligibility crosses IPC");
        foreach (var preset in new[] { PngOptimizationPreset.Auto, PngOptimizationPreset.Balanced, PngOptimizationPreset.Smallest })
        {
            var precisionPlan = PngOptimizationPlan.Create(Guid.NewGuid(), [precisionFacts with { ItemId = Guid.NewGuid() }], new("optimize", new()), preset: preset);
            var precisionResult = await new PngOptimizationExecutor(worker, publisher, activeTrial).ExecuteAsync(precisionPlan.Confirm(false, false), null, default);
            check(precisionResult.Results.Single().State == OperationState.Succeeded && precisionResult.Results.Single().Message.Contains("lossy") &&
                precisionResult.Results.Single().EngineIdentity!.EndsWith(precisionPlan.SelectedPolicy), "PNG worker: lossy preset safe publication and reporting: " + preset);
            var outputFacts = await worker.ProbeAsync(new(Guid.NewGuid(), precisionResult.Results.Single().Publication!.OutputPath!), default, forOptimization: true);
            var repeatPlan = PngOptimizationPlan.Create(Guid.NewGuid(), [outputFacts], new("optimize", new()), preset: preset);
            var repeatResult = await new PngOptimizationExecutor(worker, publisher, activeTrial).ExecuteAsync(repeatPlan.Confirm(false, false), null, default);
            check(repeatResult.Results.Single().State == OperationState.Unchanged && !Directory.EnumerateFiles(root, ".context-suite-*.tmp").Any(),
                "PNG worker: repeated lossy operation is Unchanged with no reserved leftovers: " + preset);
        }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Copy-only PNG tests must never recycle.");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => 0;
    }
}
