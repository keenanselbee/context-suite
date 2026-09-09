using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class PngInterruptionContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "png-interruption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var largeSource = Path.Combine(root, "noise.png");
        var fallbackSource = Path.Combine(root, "fallback-noise.png");
        ImageInterruptionContracts.WriteNoisePng(largeSource, 2048);
        ImageInterruptionContracts.WriteNoisePng(fallbackSource, 1024);
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"), clock);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        foreach (var policy in new[] { PngOptimizationPlan.Policy, PngOptimizationPlan.BalancedPolicy, PngOptimizationPlan.SmallestPolicy })
        foreach (var fault in policy != PngOptimizationPlan.SmallestPolicy ? new[] { "cancel", "worker-crash", "encoder-crash", "timeout" } :
            new[] { "cancel", "worker-crash", "encoder-crash", "timeout", "cancel-second" })
        {
            // Full-size noise makes palette refinement slow; the smaller deterministic noise
            // still rejects palette quality and exposes the real RGB7 fallback within the observation budget.
            var sourcePath = fault == "cancel-second" ? fallbackSource : largeSource;
            var original = SHA256.HashData(File.ReadAllBytes(sourcePath));
            var facts = await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), default, forOptimization: true);
            var reservation = await publisher.ReserveAsync(new(facts.ItemId, facts.Path, "png", new("optimize", new())));
            using var cancellation = new CancellationTokenSource();
            var work = worker.OptimizeAsync(new(facts, reservation.TemporaryPath, policy), cancellation.Token);
            var workerId = worker.ProcessId!.Value;
            Process? encoder = null;
            int? firstEncoder = null;
            var timer = Stopwatch.StartNew();
            try
            {
                while (!work.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(25) && encoder is null)
                {
                    foreach (var process in Process.GetProcessesByName("oxipng").Concat(Process.GetProcessesByName("ContextSuite.Palette")))
                    {
                        try
                        {
                            var info = new IntPtr[6];
                            if (NtQueryInformationProcess(process.Handle, 0, info, info.Length * IntPtr.Size, out _) == 0 &&
                                info[5].ToInt64() == workerId && process.TotalProcessorTime > TimeSpan.FromMilliseconds(30))
                            {
                                firstEncoder ??= process.Id;
                                if (fault != "cancel-second" || process.Id != firstEncoder) { encoder = process; break; }
                            }
                        }
                        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                        process.Dispose();
                    }
                    if (encoder is null) await Task.Delay(10);
                }
                if (encoder is null || work.IsCompleted) throw new InvalidOperationException("Did not observe the owned PNG encoder doing real work: " + fault);
                check(new FileInfo(reservation.TemporaryPath).Length == 0, "PNG interruption: encoder has no output path authority: " + fault);
                if (fault.StartsWith("cancel", StringComparison.Ordinal)) cancellation.Cancel();
                else if (fault == "worker-crash")
                {
                    using var parent = Process.GetProcessById(workerId);
                    parent.Kill(entireProcessTree: false); // Job ownership, not the test, must kill the encoder.
                }
                else if (fault == "encoder-crash") encoder.Kill();
                else
                {
                    check(clock.Current!.Due == TimeSpan.FromSeconds(120), "PNG interruption: bounded application deadline");
                    clock.Current.Expire();
                }
                try { await work.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted PNG work succeeded"); }
                catch (OperationCanceledException) when (fault.StartsWith("cancel", StringComparison.Ordinal)) { check(true, "PNG interruption: cancellation stays cancellation: " + policy + "/" + fault); }
                catch (MediaWorkerException error) when (fault != "cancel")
                {
                    check(error.Failure == (fault == "worker-crash" ? ImageFailure.WorkerTerminated : fault == "timeout" ? ImageFailure.TimedOut : ImageFailure.EngineFailure),
                        "PNG interruption: typed failure: " + fault + " (" + error.Failure + ")");
                }
                await encoder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                check(encoder.HasExited, "PNG interruption: no orphaned encoder: " + fault);
                var abandoned = await publisher.AbandonAsync(reservation, fault.StartsWith("cancel", StringComparison.Ordinal));
                check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    SHA256.HashData(File.ReadAllBytes(sourcePath)).SequenceEqual(original) && !abandoned.IsCommitted,
                    "PNG interruption: no publication or source mutation: " + fault);
                await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), default, forOptimization: true);
                check(worker.ProcessId is not null, "PNG interruption: later work remains available: " + fault);
            }
            finally
            {
                if (!work.IsCompleted) { cancellation.Cancel(); try { await work; } catch (Exception) { } }
                encoder?.Dispose();
            }
        }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Interruption tests must not recycle.");
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass,
        [Out] IntPtr[] information, int length, out int returnLength);
}
