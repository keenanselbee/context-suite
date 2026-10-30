using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Transport;

namespace ContextSuite.Private.Pdf;

public sealed record PdfOptimizationCandidate(byte[] Bytes, bool IsSmaller, string SourceSha256, string OutputSha256,
    PdfRewriteFacts Facts, string Policy = "pdf-structural-1");

public sealed partial class PdfProbeAdapter
{
    // Requires the pinned evaluation engine. Production adoption still needs
    // broader document fidelity and redistribution/payload acceptance.
    public async Task<PdfOptimizationCandidate> OptimizeAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
        => await OptimizeAsync(source, ImageOptimizationPreset.Lossless, cancellationToken);

    public async Task<PdfOptimizationCandidate> OptimizeAsync(ReadOnlyMemory<byte> source, ImageOptimizationPreset preset,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsEmpty || source.Length > WorkerCommand.MaximumPdfProbeBytes || !source.Span.StartsWith("%PDF-"u8))
            throw new InvalidDataException("PDF optimization input exceeds its current bounds.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var created = new List<string>();
        var leases = new List<FileStream>();
        var sourceHash = Convert.ToHexString(SHA256.HashData(source.Span));
        try
        {
            var input = await Snapshot(source);
            var encrypted = await RunAsync(["--is-encrypted", input], 65536, token);
            if (encrypted.ExitCode == 0) throw new NotSupportedException("Encrypted PDFs are not eligible for structural optimization.");
            if (encrypted.ExitCode != 2) throw new InvalidDataException("PDF encryption state is unavailable.");
            // Inspect all dictionaries before asking the engine to decode stream
            // payloads. The summary's signature count is never used for admission.
            var admission = await Require(["--suppress-recovery", "--json-output=2", "--json-stream-data=none", input], PdfRewriteInventory.MaximumBytes);
            PdfRewriteInventory.CheckAdmission(admission, token);
            await Require(["--suppress-recovery", "--check", input], 65536);
            var beforeJson = await InventoryJson(input);
            var before = PdfRewriteInventory.Read(beforeJson, token);
            var prepared = PdfImageOptimization.Prepare(beforeJson, preset, token);
            var arguments = new List<string> { "--suppress-recovery", "--object-streams=preserve", "--preserve-unreferenced",
                "--remove-unreferenced-resources=no", "--compress-streams=y", "--decode-level=generalized",
                "--recompress-flate", "--compression-level=9" };
            if (prepared.ChangedImages > 0)
            {
                var update = await Snapshot(prepared.Json);
                arguments.Add("--update-from-json=" + update);
            }
            arguments.Add(input);
            arguments.Add("-");
            var output = await Require(arguments.ToArray(), WorkerCommand.MaximumPdfProbeBytes);
            var candidate = await Snapshot(output);
            await Require(["--suppress-recovery", "--check", candidate], 65536);
            var after = PdfRewriteInventory.Read(await InventoryJson(candidate), token);
            PdfRewriteInventory.RequirePreserved(prepared.Expected, after);
            token.ThrowIfCancellationRequested();
            if (Convert.ToHexString(SHA256.HashData(source.Span)) != sourceHash) throw new IOException("PDF input snapshot changed during optimization.");
            var smaller = output.Length < source.Length;
            var bytes = smaller ? output : source.ToArray();
            return new(bytes, smaller, sourceHash, Convert.ToHexString(SHA256.HashData(bytes)), smaller ? after : before,
                PdfOptimizationPlan.PolicyFor(preset));
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("PDF optimization cancelled.", error, cancellationToken);
            if (deadline.IsCancellationRequested) throw new TimeoutException("PDF optimization exceeded its deadline.", error);
            throw;
        }
        finally
        {
            foreach (var lease in leases) await lease.DisposeAsync();
            foreach (var path in created)
            {
                RequireUnlinkedDirectory(_scratch);
                if (!File.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked PDF scratch entry retained.");
                File.Delete(path);
            }
        }

        async Task<string> Snapshot(ReadOnlyMemory<byte> bytes)
        {
            RequireUnlinkedDirectory(_scratch);
            var path = Path.Combine(_scratch, "pdf-optimize-" + Guid.NewGuid().ToString("N") + ".pdf");
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            { created.Add(path); await file.WriteAsync(bytes, token); }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked PDF snapshot is not allowed.");
            var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous); leases.Add(lease);
            if (!(await SHA256.HashDataAsync(lease, token)).AsSpan().SequenceEqual(SHA256.HashData(bytes.Span)))
                throw new IOException("PDF snapshot changed before inspection.");
            return NativeSnapshotPath(path);
        }
        async Task<byte[]> Require(string[] arguments, int maximum)
        {
            var result = await RunAsync(arguments, maximum, token);
            if (result.ExitCode != 0) throw new InvalidDataException("PDF structural operation failed or reported warnings.");
            return result.Output;
        }
        async Task<byte[]> InventoryJson(string path)
        {
            return await Require(["--suppress-recovery", "--json-output=2", "--decode-level=generalized", "--json-stream-data=inline", path],
                PdfRewriteInventory.MaximumBytes);
        }
    }
}
