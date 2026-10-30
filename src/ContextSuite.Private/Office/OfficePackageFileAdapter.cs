using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Office;

public static class OfficePackageFileAdapter
{
    public static async Task<OfficePackageSource> ProbeAsync(OfficePackageProbe request, CancellationToken token)
    {
        request.Validate();
        using var source = MediaFiles.OpenRead(request.Path);
        var bytes = await ReadAsync(source, token);
        var format = OfficePackageOptimizer.Probe(bytes, token);
        if (!Path.GetExtension(request.Path).Equals("." + format, StringComparison.OrdinalIgnoreCase))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var result = new OfficePackageSource(request.ItemId, request.Path, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, format);
        result.Validate();
        return result;
    }

    public static async Task<OfficePackageResult> OptimizeAsync(OfficePackageWork work, CancellationToken token)
    {
        work.Validate();
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var bytes = await ReadAsync(source, token);
        if (bytes.Length != work.Source.FileBytes || Convert.ToHexString(SHA256.HashData(bytes)) != work.Source.Sha256)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        if (OfficePackageOptimizer.Probe(bytes, token) != work.Source.Format)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        using var output = new ReservedMediaOutput(work.TemporaryPath, OfficePackageSource.MaximumFileBytes);
        var optimized = OfficePackageOptimizer.Optimize(bytes, OfficePackageOptimizationPlan.PresetFor(work.Policy), token);
        if (optimized.Length > bytes.Length) throw new ImageFailureException(ImageFailure.ValidationFailed);
        var hash = Convert.ToHexString(SHA256.HashData(optimized));
        token.ThrowIfCancellationRequested();
        output.Write(optimized);
        output.Flush(true);
        output.Position = 0;
        if (output.Length != optimized.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != hash)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        return new(new(work.Source.ItemId, hash, true), bytes.Length, optimized.Length, work.Source.Sha256,
            work.Policy, ".NET Deflate; " + work.Policy +
                (OfficePackageOptimizationPlan.PresetFor(work.Policy) is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest ? "; curated image recipes" : ""));
    }

    private static async Task<byte[]> ReadAsync(FileStream source, CancellationToken token)
    {
        if (source.Length is <= 0 or > OfficePackageSource.MaximumFileBytes)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var bytes = new byte[checked((int)source.Length)];
        await source.ReadExactlyAsync(bytes, token);
        return bytes;
    }
}
