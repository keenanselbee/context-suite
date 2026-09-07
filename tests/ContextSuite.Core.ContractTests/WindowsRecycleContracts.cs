using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Images;

namespace ContextSuite.Core.ContractTests;

internal static class WindowsRecycleContracts
{
    public static async Task<int> RunImagesAsync(string scratch, string executable)
    {
        if (!PublicationSupport.ReplacementAvailable) throw new InvalidOperationException("Real-image replacement requires the verified Windows platform.");
        var root = Path.Combine(Path.GetFullPath(scratch), "image-recycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var evidence = new List<object>();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "scratch"));
        try
        {
            foreach (var samePath in new[] { true, false })
            {
                // The same-path case is PNG bytes in a .jpg file: a real format conversion,
                // not an exception to Convert's prohibition on same-format optimization.
                var source = Path.Combine(root, samePath ? "mislabeled.jpg" : "different.png");
                ImageWorkerContracts.WritePng(source);
                var original = SHA256.HashData(await File.ReadAllBytesAsync(source));
                var facts = await worker.ProbeAsync(new(Guid.NewGuid(), source), CancellationToken.None);
                var options = new ImageConversionOptions(ImageFormat.Jpeg);
                var settings = new BatchSettings("convert", new ToolSettings(true));
                var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, settings, true).Confirm(true, true, true);
                var recycler = new RecordingRecycler();
                var publisher = new OutputPublisher(Path.Combine(root, "records"), recycler, replacementVerified: PublicationSupport.ReplacementAvailable);
                var reservation = await publisher.ReserveAsync(new(facts.ItemId, source, "jpg", settings, true, true));
                var encoded = await worker.ConvertAsync(new(plan.Plan.Items[0], options, reservation.TemporaryPath), CancellationToken.None);
                var result = await publisher.PublishAsync(reservation, encoded.Validation);
                evidence.Add(new { SamePath = samePath, Result = result, Recycle = recycler.Last, Encoded = encoded });
                if (result.Outcome != PublicationOutcome.SourceReplaced || recycler.Last?.RecycledPath is null ||
                    !SHA256.HashData(await File.ReadAllBytesAsync(recycler.Last.RecycledPath)).SequenceEqual(original))
                    throw new InvalidOperationException("Real-image publication did not retain the exact original in the Recycle Bin.");
                var output = await worker.ProbeAsync(new(Guid.NewGuid(), result.OutputPath!), CancellationToken.None);
                if (output.Format != ImageFormat.Jpeg || output.Width != 3 || output.Height != 2 ||
                    (samePath != string.Equals(source, result.OutputPath, StringComparison.OrdinalIgnoreCase)) || (!samePath && File.Exists(source)))
                    throw new InvalidOperationException("Real-image replacement has incorrect contents or final paths.");
                Console.WriteLine($"PASS: real PNG-to-JPEG {(samePath ? "same-path" : "different-extension")} replacement; decoded output and exact recycled source.");
            }
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Evidence: " + root + ". Only two disposable test originals were recycled; both remain recoverable. No trial data was used.");
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "failure.json"), JsonSerializer.Serialize(new { Error = error.ToString(), Evidence = evidence }));
            Console.Error.WriteLine(error + "\nEvidence: " + root);
            return 1;
        }
    }

    public static async Task<int> RunAsync(string scratch)
    {
        var root = Path.Combine(Path.GetFullPath(scratch), "recycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var evidence = new List<object>();
        try
        {
            var recycler = new RecordingRecycler();
            foreach (var samePath in new[] { true, false })
            {
                var source = Path.Combine(root, samePath ? "original.png" : "different.png");
                var original = "Disposable Context Suite original " + Guid.NewGuid().ToString("D");
                await File.WriteAllTextAsync(source, original);
                FileFingerprint fingerprint;
                await using (var stream = PublicationFiles.OpenRead(source))
                    fingerprint = await PublicationFiles.FingerprintAsync(stream, CancellationToken.None);
                using (var guard = new RecycleProgressSink(source, fingerprint, CancellationToken.None))
                {
                    if (guard.PreDeleteItem(0, 0) >= 0 || guard.PreDeleteItem(0x80, 0) >= 0)
                        throw new InvalidOperationException("Recycle guard accepted an unproven/permanent deletion.");
                    guard.PostDeleteItem(0, 0, 0, 0);
                    if (guard.Recycled) throw new InvalidOperationException("Recycle guard accepted a permanent-delete result.");
                }
                var publisher = new OutputPublisher(Path.Combine(root, "records"), recycler, replacementVerified: true);
                var intent = new OutputIntent(Guid.NewGuid(), source, samePath ? "png" : "webp",
                    new BatchSettings("convert", new ToolSettings(true)), ReplaceOriginal: true, ReplacementConfirmed: true);
                var reservation = await publisher.ReserveAsync(intent);
                await File.WriteAllTextAsync(reservation.TemporaryPath, "validated test output");
                var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(reservation.TemporaryPath)));
                var result = await publisher.PublishAsync(reservation, new(intent.ItemId, hash, true));
                evidence.Add(new { SamePath = samePath, Result = result, Recycle = recycler.Last });
                if (result.Outcome != PublicationOutcome.SourceReplaced || recycler.Last?.RecycledPath is null)
                    throw new InvalidOperationException("Windows did not confirm a filesystem-backed recycled original: " + result.Message);
                if (await File.ReadAllTextAsync(recycler.Last.RecycledPath) != original)
                    throw new InvalidOperationException("The recycled original is not byte-identical.");
                if (await File.ReadAllTextAsync(result.OutputPath!) != "validated test output")
                    throw new InvalidOperationException("Published output changed.");
                Console.WriteLine($"PASS: native {(samePath ? "same-path" : "different-extension")} publication and exact original in Recycle Bin.");
            }
            var cancelledPath = Path.Combine(root, "cancelled-original.txt");
            await File.WriteAllTextAsync(cancelledPath, "must remain");
            FileFingerprint cancelledFingerprint;
            await using (var stream = PublicationFiles.OpenRead(cancelledPath))
                cancelledFingerprint = await PublicationFiles.FingerprintAsync(stream, CancellationToken.None);
            var cancelled = await recycler.RecycleAsync(cancelledPath, cancelledFingerprint, new CancellationToken(true));
            if (cancelled.Recycled || await File.ReadAllTextAsync(cancelledPath) != "must remain")
                throw new InvalidOperationException("Cancelled recycling did not preserve the original.");
            evidence.Add(new { Cancelled = cancelled });
            var rejected = await OnStaAsync(() => ProbePermanentDeleteGuard(cancelledPath, cancelledFingerprint));
            evidence.Add(new { PermanentDeleteProbe = rejected });
            if (!rejected || await File.ReadAllTextAsync(cancelledPath) != "must remain")
                throw new InvalidOperationException("The native permanent-delete proposal was not safely aborted.");
            await File.WriteAllTextAsync(cancelledPath, "changed original must remain");
            var changed = await recycler.RecycleAsync(cancelledPath, cancelledFingerprint, CancellationToken.None);
            evidence.Add(new { ChangedOriginal = changed });
            if (changed.Recycled || await File.ReadAllTextAsync(cancelledPath) != "changed original must remain")
                throw new InvalidOperationException("Native recycling accepted a changed original.");
            Console.WriteLine("PASS: permanent-delete callback guard and pre-operation cancellation.");
            Console.WriteLine("PASS: native Shell permanent-delete proposal aborted; changed source retained.");
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Evidence: " + root + ". Only this run's disposable originals were recycled; no Recycle Bin contents were purged.");
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "failure.json"), JsonSerializer.Serialize(new { Error = error.ToString(), Evidence = evidence }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(error + "\nEvidence: " + root);
            return 1;
        }
    }

    private static bool ProbePermanentDeleteGuard(string path, FileFingerprint fingerprint)
    {
        // Test only: ask Shell to propose permanent deletion of this freshly created disposable file.
        // The real production sink must veto it before deletion; no user Recycle Bin settings change.
        var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!;
        var operation = (IRecycleFileOperation)Activator.CreateInstance(type)!;
        IShellRecycleItem? item = null;
        using var sink = new RecycleProgressSink(path, fingerprint, CancellationToken.None);
        try
        {
            var iid = typeof(IShellRecycleItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            // Suppress the destructive-confirmation dialog only in this disposable-file probe.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x0004 | 0x0010 | 0x0400 | 0x00100000));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
            var result = operation.PerformOperations();
            var abortedResult = operation.GetAnyOperationsAborted(out var aborted);
            return sink.RefusedPermanentDelete && !sink.Recycled && (result < 0 || (abortedResult >= 0 && aborted));
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    private static Task<bool> OnStaAsync(Func<bool> action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellRecycleItem item);

    private sealed class RecordingRecycler : IFileRecycler
    {
        private readonly WindowsFileRecycler _native = new();
        public RecycleResult? Last { get; private set; }
        public async Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
        {
            Last = await _native.RecycleAsync(path, expected, cancellationToken);
            return Last;
        }
    }
}
