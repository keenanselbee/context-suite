using System.Collections.Immutable;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Images;

public enum ImageOptimizationPreset { Lossless, Balanced, Smallest, Auto }

public sealed record LosslessImageOptimizationItem(ImageSourceFacts Source, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

// Retain the original transport/type names for compatibility. JPEG and still
// WebP now select a fixed quality recipe and always publish validated copies.
public sealed record LosslessImageOptimizationPlan(Guid BatchId, BatchSettings Settings,
    ImmutableArray<LosslessImageOptimizationItem> Items, ImageOptimizationPreset Preset = ImageOptimizationPreset.Lossless)
{
    public const string JpegPolicy = "jpeg-lossless-preserve-v1";
    public const string WebPPolicy = "webp-lossless-preserve-v1";
    public bool HasExecutableItems => !Items.IsDefaultOrEmpty && Items.Any(item => item.CanExecute);

    public static string PolicyFor(ImageFormat format, ImageOptimizationPreset preset = ImageOptimizationPreset.Lossless)
    {
        if (format is not (ImageFormat.Jpeg or ImageFormat.WebP or ImageFormat.Tga) || !Enum.IsDefined(preset) ||
            format == ImageFormat.Tga && preset is not (ImageOptimizationPreset.Auto or ImageOptimizationPreset.Lossless))
            throw new InvalidDataException("No optimization recipe for this image format or preset.");
        var family = format switch { ImageFormat.Jpeg => "jpeg", ImageFormat.WebP => "webp", _ => "tga" };
        return $"{family}-{preset.ToString().ToLowerInvariant()}-preserve-v1";
    }

    public static ImageOptimizationPreset PresetFor(ImageFormat format, string policy)
    {
        foreach (var preset in Enum.GetValues<ImageOptimizationPreset>())
        {
            if (format == ImageFormat.Tga && preset is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest) continue;
            if (PolicyFor(format, preset) == policy) return preset;
        }
        throw new InvalidDataException("Unknown image optimization policy.");
    }

    public static LosslessImageOptimizationPlan Create(Guid batchId, IEnumerable<ImageSourceFacts> sources,
        BatchSettings settings, ImageOptimizationPreset preset = ImageOptimizationPreset.Lossless)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null || !Enum.IsDefined(preset))
            throw new InvalidDataException("Invalid lossless image optimization batch or settings.");
        var items = sources.Take(4097).Select(source =>
        {
            source.Validate();
            return new LosslessImageOptimizationItem(source,
                source.Format is not (ImageFormat.Jpeg or ImageFormat.WebP or ImageFormat.Tga)
                    ? "This recipe supports JPEG, still WebP, and TGA only."
                    : source.Format == ImageFormat.Tga && preset is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest
                    ? "For TGA, choose Auto or Lossless for exact run-length compression."
                    : source.FileBytes > 128 * 1024 * 1024
                    ? "This image exceeds the optimization file-size limit."
                    : source.UnsupportedReason);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(item => item.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid lossless image optimization selection.");
        return new(batchId, settings, items, preset);
    }

    public ConfirmedLosslessImageOptimization Confirm()
    {
        if (Items.IsDefaultOrEmpty || Items.Any(item => item?.Source is null))
            throw new InvalidDataException("No lossless image optimization plan.");
        var expected = Create(BatchId, Items.Select(item => item.Source), Settings, Preset);
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
        _ = LosslessImageOptimizationPlan.PresetFor(Source.Format, Policy);
        if (Source.UnsupportedReason is not null || Source.FileBytes > 128 * 1024 * 1024 ||
            string.IsNullOrWhiteSpace(TemporaryPath) || !Path.IsPathFullyQualified(TemporaryPath) ||
            TemporaryPath.Length > 32700 || TemporaryPath.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(TemporaryPath), Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid lossless image optimization work or reservation.");
    }
}
