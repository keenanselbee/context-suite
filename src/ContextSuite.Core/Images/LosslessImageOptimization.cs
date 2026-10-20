using System.Collections.Immutable;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Images;

public sealed record LosslessImageOptimizationItem(ImageSourceFacts Source, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

// JPEG and still lossless WebP use fixed same-format recipes. New handlers
// publish copies until replacement has its own equivalent failure evidence.
public sealed record LosslessImageOptimizationPlan(Guid BatchId, BatchSettings Settings,
    ImmutableArray<LosslessImageOptimizationItem> Items)
{
    public const string JpegPolicy = "jpeg-lossless-preserve-v1";
    public const string WebPPolicy = "webp-lossless-preserve-v1";
    public bool HasExecutableItems => !Items.IsDefaultOrEmpty && Items.Any(item => item.CanExecute);

    public static string PolicyFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => JpegPolicy,
        ImageFormat.WebP => WebPPolicy,
        _ => throw new InvalidDataException("No lossless optimization recipe for this image format.")
    };

    public static LosslessImageOptimizationPlan Create(Guid batchId, IEnumerable<ImageSourceFacts> sources,
        BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null)
            throw new InvalidDataException("Invalid lossless image optimization batch or settings.");
        var items = sources.Take(4097).Select(source =>
        {
            source.Validate();
            return new LosslessImageOptimizationItem(source,
                source.Format is not (ImageFormat.Jpeg or ImageFormat.WebP)
                    ? "This lossless recipe supports JPEG and still lossless WebP only."
                    : source.Format == ImageFormat.WebP && source.IsLossy
                    ? "This WebP is lossy. Its image data cannot use the lossless optimization recipe."
                    : source.FileBytes > 128 * 1024 * 1024
                    ? "This image exceeds the optimization file-size limit."
                    : source.UnsupportedReason);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(item => item.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid lossless image optimization selection.");
        return new(batchId, settings, items);
    }

    public ConfirmedLosslessImageOptimization Confirm()
    {
        if (Items.IsDefaultOrEmpty || Items.Any(item => item?.Source is null))
            throw new InvalidDataException("No lossless image optimization plan.");
        var expected = Create(BatchId, Items.Select(item => item.Source), Settings);
        if (!HasExecutableItems || !expected.Items.SequenceEqual(Items))
            throw new InvalidDataException("No valid lossless image optimization plan.");
        Settings.SelectOutput(false, false, false, false);
        return new(this);
    }
}

public sealed class ConfirmedLosslessImageOptimization
{
    internal ConfirmedLosslessImageOptimization(LosslessImageOptimizationPlan plan) { Plan = plan; }
    public LosslessImageOptimizationPlan Plan { get; }
}

public sealed record LosslessImageOptimizationWork(ImageSourceFacts Source, string TemporaryPath, string Policy)
{
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing lossless optimization source.");
        Source.Validate();
        if (Source.UnsupportedReason is not null || Source.FileBytes > 128 * 1024 * 1024 ||
            Source.Format == ImageFormat.WebP && Source.IsLossy || Policy != LosslessImageOptimizationPlan.PolicyFor(Source.Format) ||
            string.IsNullOrWhiteSpace(TemporaryPath) || !Path.IsPathFullyQualified(TemporaryPath) ||
            TemporaryPath.Length > 32700 || TemporaryPath.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(TemporaryPath), Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid lossless image optimization work or reservation.");
    }
}
