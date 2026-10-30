using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Pdf;

public sealed partial class PdfProbeAdapter
{
    public async Task<PdfFileSource> ProbeFileAsync(PdfFileProbe request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request.Validate();
        using var source = MediaFiles.OpenRead(request.Path);
        var bytes = await ReadFileAsync(source, token);
        var facts = await ProbeAsync(bytes, token);
        return new(request.ItemId, request.Path, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, facts);
    }

    public async Task<PdfWorkResult> OptimizeFileAsync(PdfOptimizationWork work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var bytes = await ReadFileAsync(source, token);
        if (bytes.Length != work.Source.FileBytes || Convert.ToHexString(SHA256.HashData(bytes)) != work.Source.Sha256)
            throw new InvalidDataException("PDF changed after planning.");
        // Hold the original read handle and the checked, empty reservation through
        // validation and copying. The engine only receives owned scratch snapshots.
        using var output = new ReservedMediaOutput(work.TemporaryPath, PdfFileSource.MaximumFileBytes);
        var result = await OptimizeAsync(bytes, PdfOptimizationPlan.PresetFor(work.Policy), token);
        if (result.Policy != work.Policy || result.SourceSha256 != work.Source.Sha256 || result.Bytes.Length > bytes.Length ||
            result.IsSmaller != (result.Bytes.Length < bytes.Length) || Convert.ToHexString(SHA256.HashData(result.Bytes)) != result.OutputSha256 ||
            (!result.IsSmaller && result.OutputSha256 != work.Source.Sha256))
            throw new InvalidDataException("PDF candidate does not match its preservation policy.");
        token.ThrowIfCancellationRequested();
        output.Write(result.Bytes);
        output.Flush(true);
        output.Position = 0;
        if (output.Length != result.Bytes.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != result.OutputSha256)
            throw new IOException("Reserved PDF output changed while copying.");
        return new(new(work.Source.ItemId, result.OutputSha256, true), bytes.Length, result.Bytes.Length, result.SourceSha256,
            work.Policy, "qpdf 12.4.1; " + work.Policy);
    }

    private static async Task<byte[]> ReadFileAsync(FileStream source, CancellationToken token)
    {
        if (source.Length is <= 0 or > PdfFileSource.MaximumFileBytes) throw new NotSupportedException("PDF exceeds the current optimization size limit.");
        var bytes = new byte[checked((int)source.Length)];
        await source.ReadExactlyAsync(bytes, token);
        return bytes;
    }
}
