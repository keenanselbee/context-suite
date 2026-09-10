using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application.TestHost;

internal static class DirectCommandContracts
{
    public static async Task<int> RunAsync(string executable)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ContextSuite.Production.slnx"))) repository = repository.Parent;
        if (repository is null || !File.Exists(executable)) throw new InvalidOperationException("Repository and staged worker are required.");
        var root = Path.Combine(repository.FullName, ".codex-temp", "license-workflow", "direct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LicenseWorkflowFixture.Prepare(root);
        var source = Path.Combine(root, "fixture.png");
        var original = SHA256.HashData(File.ReadAllBytes(source));
        var manager = new PaidLicenseManager(new LicenseWorkflowFixture(), new LicenseStore(Path.Combine(root, "Access", "license.bin"), LicenseEnvironment.Sandbox));
        var access = new OperationAccess(new LocalTrialStore(Path.Combine(root, "Access", "trial.json")), manager);
        var recycler = new RetainFiles();
        var publisher = new OutputPublisher(Path.Combine(root, "Publications"), recycler, PublicationSupport.ReplacementAvailable);
        await using var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "WorkerScratch")),
            new SuiteSettings { PlayCompletionSound = false }, publisher, access);
        var expectDdsPrompt = false;
        vm.ConversionRequested += (planner, _) =>
        {
            if (!expectDdsPrompt || planner.Target != ImageFormat.Dds || !planner.TargetFixed || !planner.IsDdsWorkflow ||
                planner.MetadataMode != ImageMetadataMode.Preserve || planner.DdsMetadataChoices.Count != 2)
                throw new InvalidOperationException("Unexpected dialog or missing focused DDS choices.");
            return Task.FromResult<ConfirmedImageBatch?>(null);
        };
        var passed = 0;
        await Run("convert", "tga", source);
        Check(vm.Rows.Single().Result.State == OperationState.Failed && vm.Rows[0].Status.Contains("Activate", StringComparison.OrdinalIgnoreCase),
            "expired direct conversion gives activation guidance");
        await manager.ActivateAsync(LicenseWorkflowFixture.TestKey);
        vm.RetryFailed();
        await vm.WaitForIdleAsync();
        Check(vm.Rows.Count == 2 && vm.Rows[1].Action == "tga" && vm.Rows[1].Result.State == OperationState.Succeeded &&
            vm.Rows[1].OutputPath.EndsWith(" - Converted.tga"), "activation and retry retain selected files and target");
        Check(vm.DisplayRows.Count() == 1 && !vm.HasProblems && !vm.CanRetry && vm.Summary.StartsWith("1 completed") && !vm.Summary.Contains("failed"),
            "successful retry removes the stale failure from visible results and disables another attempt");
        Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original), "default conversion preserves original bytes");
        await manager.DeactivateAsync();
        await Run("optimize", "balanced", source);
        Check(vm.Rows[^1].Result.State == OperationState.Failed, "deactivation blocks the next optimization");
        await manager.ActivateAsync(LicenseWorkflowFixture.TestKey);
        vm.RetryFailed();
        await vm.WaitForIdleAsync();
        Check(vm.Rows[^1].Action == "balanced" && vm.Rows[^1].Path == source &&
            vm.Rows[^1].Result.State is OperationState.Succeeded or OperationState.Unchanged,
            "reactivation and retry preserve optimization preset and file");
        Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original) && recycler.Calls == 0,
            "default optimization keeps originals and never recycles");

        // Use the metadata-free TGA copy to exercise replacement independently
        // of the PNG fixture's metadata, which can mandate copies on conversion.
        var tga = vm.Rows[1].OutputPath;
        var tgaBytes = File.ReadAllBytes(tga);
        vm.Settings = new() { Convert = new(true), Optimize = new(true), PlayCompletionSound = false };
        vm.Admit(new(Guid.NewGuid(), "convert", "png", [tga]));
        vm.Settings = new() { PlayCompletionSound = false };
        await vm.WaitForIdleAsync();
        var replacement = vm.Rows[^1].Result;
        Check(vm.Rows[^1].Settings.Preferences.ReplaceOriginals && replacement.State == OperationState.Succeeded,
            "queued conversion retains saved replacement preference after Settings changes");
        Check(File.ReadAllBytes(tga).SequenceEqual(tgaBytes) && File.Exists(vm.Rows[^1].OutputPath),
            "different-format publication validates output and retains original on cleanup refusal");
        Check(PublicationSupport.ReplacementAvailable
            ? recycler.Calls == 1 && replacement.Publication is { Outcome: PublicationOutcome.OriginalRetained, HasWarning: true }
            : recycler.Calls == 0 && replacement.Publication?.Outcome == PublicationOutcome.CopyCreated,
            "replacement obeys platform gate and reports retained recovery evidence");

        // The test recycler always refuses deletion. Native recycling is a
        // separate explicitly authorized acceptance gate.
        vm.Settings = new() { Optimize = new(true), PlayCompletionSound = false };
        await Run("optimize", "lossless", source);
        var optimized = vm.Rows[^1].Result;
        Check(optimized.State is OperationState.Succeeded or OperationState.Unchanged, "direct optimization accepts saved output choice");
        if (PublicationSupport.ReplacementAvailable && optimized.State == OperationState.Succeeded)
            Check(optimized.Publication is { Outcome: PublicationOutcome.BackupRetained, HasWarning: true } &&
                vm.Rows[^1].OutputPath == source && recycler.Retained.Any(path => SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original)),
                "same-path optimization retains original backup when cleanup is refused");
        expectDdsPrompt = true;
        await Run("convert", "dds", tga);
        Check(vm.Rows[^1].Result.State == OperationState.Cancelled && File.ReadAllBytes(tga).SequenceEqual(tgaBytes),
            "DDS menu command retains focused texture and metadata choices; cancellation keeps source");
        Console.WriteLine($"Passed {passed} direct-command workflow checks. Simulated license, disposable files, no native recycling. Evidence: {root}");
        return 0;

        async Task Run(string operation, string action, string path)
        {
            vm.Admit(new(Guid.NewGuid(), operation, action, [path]));
            await vm.WaitForIdleAsync();
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++; Console.WriteLine("PASS " + name);
        }
    }

    private sealed class RetainFiles : IFileRecycler
    {
        public int Calls { get; private set; }
        public List<string> Retained { get; } = [];
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
        {
            Calls++; Retained.Add(path);
            return Task.FromResult(new RecycleResult(false, "Retained for isolated command verification."));
        }
    }
}
