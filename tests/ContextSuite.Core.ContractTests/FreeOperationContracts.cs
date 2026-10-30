using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class FreeOperationContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "free-operation-" + Guid.NewGuid().ToString("N"));
        var accessDirectory = Path.Combine(root, "Access");
        Directory.CreateDirectory(accessDirectory);
        var trial = Path.Combine(accessDirectory, "trial.json");
        var license = Path.Combine(accessDirectory, "license-production.bin");
        var licenseBytes = new byte[] { 0, 1, 255, 9, 2 };
        await File.WriteAllBytesAsync(license, licenseBytes);
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "source.png"), new string('A', 64), 100,
            ImageFormat.Png, 10, 10, 8, 1, false, false, "sRGB", []);
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.WebP), new("convert", new()))
            .Confirm(false, false, false);
        var optimize = PngOptimizationPlan.Create(Guid.NewGuid(), [facts], new("optimize", new())).Confirm(false, false);
        var service = new OperationAdmissionService();
        var expired = "{\"SchemaVersion\":1,\"StartedUtc\":\"2000-01-01T00:00:00Z\",\"LastObservedUtc\":\"2000-01-09T00:00:00Z\"}";
        foreach (var legacyRecord in new[] { expired, "not valid JSON" })
        {
            await File.WriteAllTextAsync(trial, legacyRecord);
            var original = await File.ReadAllBytesAsync(trial);
            using (var trialLock = new FileStream(trial, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var licenseLock = new FileStream(license, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var conversion = await service.AdmitConversionAsync(plan, default);
                var optimization = await service.AdmitOptimizationAsync(optimize, default);
                check(conversion.IsAllowed && conversion.BatchId == plan.Plan.BatchId &&
                    optimization.IsAllowed && optimization.BatchId == optimize.Plan.BatchId,
                    "free operation: locked expired/corrupt legacy records do not affect confirmed work");
            }
            check((await File.ReadAllBytesAsync(trial)).SequenceEqual(original) &&
                (await File.ReadAllBytesAsync(license)).SequenceEqual(licenseBytes),
                "free operation: legacy trial and protected license bytes remain untouched");
        }
        File.Delete(trial); // Only this test's disposable legacy fixture.
        Directory.CreateDirectory(trial); // An unusable old access path must also be irrelevant.
        check((await service.AdmitConversionAsync(plan, default)).IsAllowed &&
            Directory.GetFiles(accessDirectory).Length == 1,
            "free operation: unusable legacy trial path creates no access files or lock files");
        Directory.Delete(trial);
        check((await service.AdmitOptimizationAsync(optimize, default)).IsAllowed && !File.Exists(trial),
            "free operation: missing legacy trial remains missing after confirmed optimization");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await service.AdmitConversionAsync(plan, cancelled.Token); check(false, "free operation cancellation"); }
        catch (OperationCanceledException) { check(true, "free operation: cancellation still prevents admission"); }
        try { await service.AdmitConversionAsync((ConfirmedImageBatch)null!, default); check(false, "free operation null plan"); }
        catch (ArgumentNullException) { check(true, "free operation: missing confirmation is refused"); }
        await using var worker = new WorkerClient(Path.Combine(root, "missing-worker.exe"));
        var executor = new ImageBatchExecutor(worker, new OutputPublisher(Path.Combine(root, "Publications"), null!), service);
        try
        {
            await executor.ExecuteAdmittedAsync(plan, new(new(true, "Ready."), Guid.NewGuid()), null, default);
            check(false, "free operation foreign batch");
        }
        catch (InvalidDataException)
        {
            check(worker.ProcessId is null && !Directory.Exists(Path.Combine(root, "Publications")),
                "free operation: foreign batch admission is refused before worker or publication");
        }
    }
}
