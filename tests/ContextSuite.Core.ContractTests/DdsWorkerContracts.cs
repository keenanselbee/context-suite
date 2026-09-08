using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Dds;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class DdsWorkerContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "dds-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "texture ü.dds");
        var original = DdsContracts.Fixture(DdsFormat.Rgba8Srgb);
        for (var p = 148; p < original.Length; p += 4) { original[p] = 255; original[p + 3] = 255; }
        File.WriteAllBytes(path, original);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"));
        var source = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
        check(source.Texture is { Format: DdsFormat.Rgba8Srgb }, "DDS IPC: actual native probe returns public header facts");
        var trialPath = Path.Combine(root, "trial", "trial.json");
        var clock = new Clock();
        var trial = new LocalTrialStore(trialPath, clock);
        await using (var vm = new ConversionViewModel(worker, trial, Guid.NewGuid(), [new(source.ItemId, path, source, null)], new("convert", new()), false))
        {
            await vm.InitializeAsync();
            vm.Target = ImageFormat.Dds;
            vm.DdsMips = DdsMipPolicy.Generate;
            vm.WarningsAcknowledged = true;
            check(vm.IsDdsWorkflow && !vm.CanConfirm && vm.Rows[0].ProposedOutput.EndsWith(" - BC7-sRGB.dds", StringComparison.Ordinal),
                "DDS VM: named representation requires a current encoded preview");
            await vm.RefreshPreviewAsync();
            check(vm.CanConfirm && vm.AfterPreview?.PixelWidth == 4 && !File.Exists(trialPath), "DDS VM: preview enables consent without starting trial");
            vm.DdsFormat = DdsFormat.Bc3Srgb;
            check(!vm.CanConfirm && !vm.WarningsAcknowledged && vm.AfterPreview is null, "DDS VM: policy changes invalidate preview and consent");
            vm.Target = ImageFormat.Png; vm.DdsMip = "9";
            check(!vm.CanConfirm && vm.Rows[0].ProposedOutput.Length == 0, "DDS VM: nonexistent export mip blocked");
            vm.DdsMip = "0"; vm.WarningsAcknowledged = true;
            await vm.RefreshPreviewAsync();
            check(vm.CanConfirm && vm.IsDdsExport, "DDS VM: explicit existing PNG export mip previews safely");
        }
        var policy = new ImageConversionOptions(ImageFormat.Dds, Texture: new(Format: DdsFormat.Bc7Srgb, Mips: DdsMipPolicy.Generate));
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source, source with { ItemId = Guid.NewGuid() }], policy, new("convert", new()));
        var publisher = new OutputPublisher(Path.Combine(root, "journal"), new NoRecycle());
        var executor = new ImageBatchExecutor(worker, publisher, trial);
        var result = await executor.ExecuteAsync(plan.Confirm(true, false, false), (_, state) =>
        { if (state.State == OperationState.Succeeded) clock.Utc += TimeSpan.FromHours(73); }, default);
        check(result.Admission.IsAllowed && result.Results.All(item => item.State == OperationState.Succeeded), "DDS orchestration: admitted batch completes after trial expiry");
        var outputs = Directory.GetFiles(root, "* - BC7-sRGB*.dds");
        check(outputs.Length == 2 && outputs.Any(file => file.EndsWith(" (2).dds", StringComparison.Ordinal)) &&
            outputs.All(file => DdsParser.Parse(File.ReadAllBytes(file), new FileInfo(file).Length) is { Format: DdsFormat.Bc7Srgb, MipLevels: 3 }),
            "DDS orchestration: app publishes validated, collision-safe named textures");
        check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(original)), "DDS orchestration: sources unchanged");
        var expired = await executor.ExecuteAsync(plan.Confirm(true, false, false), null, default);
        check(!expired.Admission.IsAllowed && Directory.GetFiles(root, "* - BC7-sRGB*.dds").Length == 2, "DDS orchestration: expired admission publishes nothing");
        var invalid = Path.Combine(root, "truncated.dds"); File.WriteAllBytes(invalid, original[..149]);
        var process = worker.ProcessId;
        try { await worker.ProbeAsync(new(Guid.NewGuid(), invalid), default); check(false, "DDS IPC: truncated payload rejected"); }
        catch (MediaWorkerException) { check(true, "DDS IPC: truncated payload rejected"); }
        await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
        check(worker.ProcessId == process, "DDS IPC: bad texture does not crash the worker or prevent later input");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => 0;
    }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("DDS copy-only contracts must never recycle.");
    }
}
