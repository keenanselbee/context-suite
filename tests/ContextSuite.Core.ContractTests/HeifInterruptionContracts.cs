using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Core.ContractTests;

internal static class HeifInterruptionContracts
{
    public static async Task RunAsync(string executable, string fixtures, string root)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use Windows x64 and a fresh absolute evidence directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>(); var observations = new List<object>();
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"), clock);
        foreach (var extension in new[] { "heic", "avif" })
        {
            var originalPath = Path.Combine(fixtures, "stress." + extension);
            var sourcePath = Path.Combine(root, "stress." + extension); File.Copy(originalPath, sourcePath);
            var hash = Hash(sourcePath); var written = File.GetLastWriteTimeUtc(sourcePath);
            foreach (var fault in new[] { "cancel", "crash", "timeout" })
            {
                var source = await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), default);
                Check(source.Width == 3072 && source.Height == 3072 && source.BitDepth == 8, "Stress source successfully probes before fault: " + extension + " " + fault);
                var options = new ImageConversionOptions(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic);
                var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new()));
                var reservation = await publisher.ReserveAsync(new(source.ItemId, sourcePath, "png", plan.Settings));
                var processId = worker.ProcessId!.Value;
                using var process = Process.GetProcessById(processId);
                var modules = process.Modules.Cast<ProcessModule>().Where(module =>
                    module.ModuleName.Equals(extension == "heic" ? "libde265.dll" : "heif.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                Check(modules.Length == 1, "Expected codec library is loaded: " + extension);
                var start = modules[0].BaseAddress.ToInt64(); var end = start + modules[0].ModuleMemorySize;
                using var cancel = new CancellationTokenSource();
                var conversion = worker.ConvertAsync(new(plan.Confirm(true, false, false).Plan.Items[0], options, reservation.TemporaryPath), cancel.Token);
                var timer = Stopwatch.StartNew();
                var sampled = false; var threadId = 0; long instruction = 0;
                while (!conversion.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20) && !sampled)
                {
                    process.Refresh();
                    foreach (ProcessThread thread in process.Threads)
                    {
                        using (thread)
                        {
                            using var handle = OpenThread(0x004a, false, (uint)thread.Id); // suspend/resume, context, query
                            if (handle.IsInvalid || GetProcessIdOfThread(handle) != processId) continue;
                            var previous = SuspendThread(handle);
                            if (previous == uint.MaxValue) continue;
                            try
                            {
                                var allocation = Marshal.AllocHGlobal(1248);
                                try
                                {
                                    var context = new IntPtr((allocation.ToInt64() + 15) & ~15L);
                                    Marshal.Copy(new byte[1232], 0, context, 1232);
                                    Marshal.WriteInt32(context, 48, 0x100001); // x64 CONTEXT_CONTROL
                                    if (!GetThreadContext(handle, context)) throw new Win32Exception(Marshal.GetLastWin32Error());
                                    var rip = Marshal.ReadInt64(context, 248);
                                    if (rip < start || rip >= end || conversion.IsCompleted) continue;
                                    threadId = thread.Id; instruction = rip; sampled = true;
                                    // Only this owned worker is suspended, with an observed instruction
                                    // inside its codec. Deliver the fault before resuming that thread.
                                    if (fault == "cancel") cancel.Cancel();
                                    else if (fault == "timeout")
                                    {
                                        Check(clock.Current!.Due == TimeSpan.FromSeconds(120), "Conversion retains the production 120-second deadline");
                                        clock.Current.Expire();
                                    }
                                    else process.Kill(true);
                                    break;
                                }
                                finally { Marshal.FreeHGlobal(allocation); }
                            }
                            finally
                            {
                                if (ResumeThread(handle) == uint.MaxValue && !process.HasExited && fault != "crash")
                                    throw new Win32Exception(Marshal.GetLastWin32Error());
                            }
                        }
                    }
                    if (!sampled) await Task.Delay(1);
                }
                if (!sampled)
                {
                    cancel.Cancel(); try { await conversion; } catch (Exception) { }
                    await publisher.AbandonAsync(reservation, true);
                    throw new InvalidOperationException("No active codec instruction observed: " + extension + " " + fault);
                }
                Check(sampled, "Fault delivered during active native decoding: " + extension + " " + fault);
                try { await conversion.WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Interrupted conversion returned success"); }
                catch (OperationCanceledException) when (fault == "cancel") { Check(true, "User cancellation retains its category"); }
                catch (MediaWorkerException error) when (fault != "cancel")
                { Check(error.Failure == (fault == "timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated), "Fault has the expected typed result: " + fault); }
                var abandoned = await publisher.AbandonAsync(reservation, fault == "cancel");
                Check(!File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    abandoned.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    Hash(sourcePath) == hash && File.GetLastWriteTimeUtc(sourcePath) == written, "No partial publication or source changes: " + extension + " " + fault);
                var recovered = await worker.ProbeAsync(new(Guid.NewGuid(), sourcePath), default);
                Check(worker.ProcessId != processId && recovered.Sha256 == source.Sha256, "Fresh worker processes the same source after native interruption");
                observations.Add(new { extension, fault, ProcessId = processId, ThreadId = threadId, Module = modules[0].ModuleName,
                    InstructionOffset = instruction - start, Milliseconds = timer.Elapsed.TotalMilliseconds });
            }
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Observations = observations }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} active HEIF interruption checks across six observed codec executions.");
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); }
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Interruption tests must never recycle.");
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessIdOfThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadContext(SafeFileHandle thread, IntPtr context);
}
