using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;
using System.Windows.Media.Imaging;

namespace ContextSuite.Application.TestHost;

internal static class LicenseWorkflowContracts
{
    public static async Task<int> RunAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ContextSuite.Production.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Run this test from the repository build output.");
        var root = Path.Combine(repository.FullName, ".codex-temp", "license-workflow", "contracts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LicenseWorkflowFixture.Prepare(root);
        var passed = 0;
        var file = Path.Combine(root, "fixture.png");
        using (var stream = File.OpenRead(file))
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Check(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth == 64 && decoder.Frames[0].PixelHeight == 48, "independent PNG fixture decodes");
        }
        var trialPath = Path.Combine(root, "Access", "trial.json");
        var paths = new ApplicationPaths("unused", "unused", "unused", "unused", "unused")
            { ActivationCleanupDirectory = Path.Combine(root, "ActivationCleanup") };
        Directory.CreateDirectory(paths.ActivationCleanupDirectory);
        var stale = Path.Combine(paths.ActivationCleanupDirectory, Guid.NewGuid().ToString("D") + ".request");
        var retained = Path.Combine(root, Guid.NewGuid().ToString("D") + ".request");
        File.WriteAllText(stale, "disposable request"); File.WriteAllText(retained, "outside cleanup scope");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(retained, DateTime.UtcNow.AddDays(-2));
        ActivationStore.CleanupStale(paths.ActivationCleanupDirectory);
        Check(!File.Exists(stale) && File.Exists(retained) && ApplicationPaths.Production.ActivationCleanupDirectory == ActivationStore.DirectoryPath,
            "test request cleanup stays isolated and production default is unchanged");
        var trialBytes = await File.ReadAllBytesAsync(trialPath);
        Check((await new LocalTrialStore(trialPath).ReadStatusAsync()).State == LocalTrialState.Expired, "fixture starts with expired trial");
        try { LicenseWorkflowFixture.Prepare(root); throw new Exception("Used profile was reset."); }
        catch (InvalidOperationException) { Check(Enumerable.SequenceEqual(trialBytes, await File.ReadAllBytesAsync(trialPath)), "used profile cannot be reset"); }
        try { LicenseWorkflowFixture.Prepare(repository.FullName); throw new Exception("Repository root was accepted."); }
        catch (InvalidOperationException) { Check(true, "non-isolated target is rejected"); }
        var provider = new LicenseWorkflowFixture();
        var installation = Guid.NewGuid();
        Check((await provider.ActivateAsync("NOT-A-REAL-KEY", installation, default)).State == LicenseReplyState.ActivationDeclined, "only explicit test key accepted");
        var activated = await provider.ActivateAsync(LicenseWorkflowFixture.TestKey, installation, default);
        Check(activated is { State: LicenseReplyState.Granted, Receipt: not null }, "test activation granted without network");
        var validated = await provider.ValidateAsync(LicenseWorkflowFixture.TestKey, installation, activated.Receipt!.ActivationId, default);
        Check(validated.Receipt == activated.Receipt, "validation retains simulated receipt");
        Check((await provider.DeactivateAsync(LicenseWorkflowFixture.TestKey, activated.Receipt.ActivationId, default)).State == LicenseReplyState.Deactivated,
            "simulated deactivation acknowledged");
        try { await provider.ActivateAsync(LicenseWorkflowFixture.TestKey, installation, new CancellationToken(true)); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { Check(true, "cancellation is retained"); }
        Console.WriteLine($"Passed {passed} isolated license-workflow harness checks. No app window or provider request. Fixtures retained: {root}");
        return 0;

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed++; Console.WriteLine("PASS " + message);
        }
    }
}
