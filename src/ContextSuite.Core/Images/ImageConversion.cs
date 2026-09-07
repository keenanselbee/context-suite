using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Images;

public enum ImageFormat { Png, Jpeg, WebP, Bmp, Tga }
public enum ImageMetadataMode { Preserve, RemoveDescriptive }

// EXIF-compatible units: 1 = aspect only, 2 = pixels/inch, 3 = pixels/cm.
// A divisor retains PNG pixels/metre exactly when represented as pixels/cm.
public sealed record ImageResolution(uint X, uint Y, ushort Unit, uint Divisor = 1)
{
    public void Validate()
    {
        if (X == 0 || Y == 0 || Unit is < 1 or > 3 || Divisor == 0)
            throw new InvalidDataException("Image resolution is invalid.");
    }
}

public sealed record ImageSourceFacts(Guid ItemId, string Path, string Sha256, long FileBytes,
    ImageFormat Format, uint Width, uint Height, uint BitDepth, uint Orientation,
    bool HasTransparency, bool IsLossy, string ColorDescription,
    ImmutableArray<string> ProfileNames, bool HasOtherMetadata = false, string? UnsupportedReason = null, ImageResolution? Resolution = null,
    bool HasGrayscaleProfile = false)
{
    public void Validate()
    {
        Resolution?.Validate();
        if (ItemId == Guid.Empty || string.IsNullOrWhiteSpace(Path) || !System.IO.Path.IsPathFullyQualified(Path) ||
            Path.Length > 32700 || Path.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            Sha256 is null || Sha256.Length != 64 || !Sha256.All(char.IsAsciiHexDigit) || FileBytes <= 0 ||
            !Enum.IsDefined(Format) || Width is 0 or > 16384 || Height is 0 or > 16384 ||
            (ulong)Width * Height > 40_000_000 || BitDepth is 0 or > 16 || Orientation > 8 ||
            string.IsNullOrWhiteSpace(ColorDescription) || ColorDescription.Length > 256 ||
            ProfileNames.IsDefault || ProfileNames.Length > 32 ||
            ProfileNames.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 64) || UnsupportedReason?.Length > 1024)
            throw new InvalidDataException("Image facts are incomplete or exceed supported limits.");
    }
}

public sealed record ImageConversionOptions(ImageFormat Target, uint Quality = 90, bool WebPLossless = false,
    uint? MaximumDimension = null, uint? MatteRgb = null, ImageMetadataMode Metadata = ImageMetadataMode.Preserve)
{
    public bool IsLossy => Target == ImageFormat.Jpeg || (Target == ImageFormat.WebP && !WebPLossless);
    public bool SupportsAlpha => Target is not (ImageFormat.Jpeg or ImageFormat.Bmp);
    public string Extension => Target switch
    {
        ImageFormat.Png => "png", ImageFormat.Jpeg => "jpg", ImageFormat.WebP => "webp", ImageFormat.Bmp => "bmp", ImageFormat.Tga => "tga",
        _ => throw new InvalidDataException("Unknown image target.")
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Target) || !Enum.IsDefined(Metadata) || Quality is < 1 or > 100 ||
            MaximumDimension is 0 or > 16384 || MatteRgb > 0xffffff ||
            (WebPLossless && Target != ImageFormat.WebP) || (MatteRgb is not null && SupportsAlpha))
            throw new InvalidDataException("Conversion options are invalid or contradictory.");
    }
}

public sealed record ImageItemPlan(ImageSourceFacts Source, uint OutputWidth, uint OutputHeight, uint OutputDepth,
    ImmutableArray<OperationWarning> Warnings, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

public sealed record ImageBatchPlan(Guid BatchId, ImageConversionOptions Options, BatchSettings Settings,
    bool ReplaceOriginal, ImmutableArray<ImageItemPlan> Items)
{
    public bool HasExecutableItems => Items.Any(i => i.CanExecute);

    // Confirmation belongs to this exact immutable plan, not a reusable settings preference.
    public ConfirmedImageBatch Confirm(bool warningsAcknowledged, bool replacementConfirmed, bool replacementAvailable)
    {
        ImageConversionPlanner.ValidateBatch(this);
        if (!HasExecutableItems) throw new InvalidDataException("No selected files can execute this conversion.");
        if (Items.Any(i => i.CanExecute && !i.Warnings.IsEmpty) && !warningsAcknowledged)
            throw new InvalidDataException("Review and acknowledge the conversion consequences first.");
        Settings.SelectOutput(ReplaceOriginal, replacementConfirmed, false, replacementAvailable);
        return new ConfirmedImageBatch(this);
    }
}

public sealed class ConfirmedImageBatch
{
    internal ConfirmedImageBatch(ImageBatchPlan plan) { Plan = plan; }
    public ImageBatchPlan Plan { get; }
}

public static class ImageConversionPlanner
{
    public static ImageBatchPlan Create(Guid batchId, IEnumerable<ImageSourceFacts> sources,
        ImageConversionOptions options, BatchSettings settings, bool replaceOriginal = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var items = sources.Take(4097).Select(source => PlanItem(source, options)).ToImmutableArray();
        var plan = new ImageBatchPlan(batchId, options, settings, replaceOriginal, items);
        ValidateBatch(plan);
        return plan;
    }

    internal static void ValidateBatch(ImageBatchPlan plan)
    {
        if (plan.BatchId == Guid.Empty || plan.Options is null || plan.Settings is null ||
            plan.Settings.Operation != "convert" || plan.Settings.Preferences is null ||
            plan.Items.IsDefaultOrEmpty || plan.Items.Length > 4096 ||
            plan.Items.Any(i => i?.Source is null || i.Warnings.IsDefault) || plan.Items.Select(i => i.Source.ItemId).Distinct().Count() != plan.Items.Length)
            throw new InvalidDataException("The conversion batch is invalid.");
        plan.Options.Validate();
        foreach (var item in plan.Items)
        {
            var expected = PlanItem(item.Source, plan.Options);
            if (expected.OutputWidth != item.OutputWidth || expected.OutputHeight != item.OutputHeight ||
                expected.OutputDepth != item.OutputDepth || expected.BlockReason != item.BlockReason ||
                !expected.Warnings.SequenceEqual(item.Warnings))
                throw new InvalidDataException("The conversion plan no longer matches its facts and options.");
        }
    }

    private static ImageItemPlan PlanItem(ImageSourceFacts source, ImageConversionOptions options)
    {
        source.Validate();
        var width = source.Orientation is >= 5 and <= 8 ? source.Height : source.Width;
        var height = source.Orientation is >= 5 and <= 8 ? source.Width : source.Height;
        if (options.MaximumDimension is uint maximum && Math.Max(width, height) > maximum)
        {
            var scale = (double)maximum / Math.Max(width, height);
            width = Math.Max(1, (uint)Math.Round(width * scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (uint)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        }
        var depth = options.Target == ImageFormat.Png && source.BitDepth > 8 ? 16u : 8u;
        var warnings = ImmutableArray.CreateBuilder<OperationWarning>();
        var blocked = source.UnsupportedReason;
        if (source.Format == options.Target) blocked ??= "Same-format re-encoding belongs to Optimize.";
        if (source.HasTransparency && !options.SupportsAlpha && options.MatteRgb is null)
            blocked ??= "Select and preview an explicit background color for transparency.";
        if (options.Metadata == ImageMetadataMode.Preserve && (source.HasOtherMetadata ||
            source.ProfileNames.Any(p => p.ToLowerInvariant() is not ("icc" or "icm" or "exif" or "xmp"))))
            blocked ??= "Some source metadata cannot be preserved by this conversion. Choose descriptive-metadata removal explicitly.";
        if (options.Target is ImageFormat.Bmp or ImageFormat.Tga)
        {
            if (options.Metadata == ImageMetadataMode.Preserve && (source.Resolution is not null ||
                source.ProfileNames.Any(p => p.ToLowerInvariant() is not ("icc" or "icm"))))
                blocked ??= "BMP/TGA output cannot preserve this descriptive metadata or resolution. Choose metadata removal explicitly.";
            if (source.ProfileNames.Any(p => p.ToLowerInvariant() is "icc" or "icm"))
                warnings.Add(new("color-profile-conversion", "BMP/TGA pixels will be converted to sRGB; the embedded color profile is not retained in this output variant."));
            warnings.Add(new("untagged-srgb", "BMP/TGA output uses untagged sRGB pixels. Programs must interpret it as sRGB; embedded profiles are not supported in this output variant."));
        }
        if (source.IsLossy && options.IsLossy)
            warnings.Add(new("lossy-transcode", "This re-encodes already lossy pixels and can introduce further loss."));
        if (source.BitDepth > depth)
            warnings.Add(new("precision-reduction", "The target reduces channel precision to 8 bits."));
        if (options.Metadata == ImageMetadataMode.RemoveDescriptive)
            warnings.Add(new("metadata-removal", "Descriptive metadata, location, resolution/aspect hints and embedded thumbnails will be removed; color interpretation is retained."));
        if (source.HasTransparency && !options.SupportsAlpha && options.MatteRgb is not null)
            warnings.Add(new("alpha-flattening", "Transparency will be composited against the selected background."));
        if (source.HasGrayscaleProfile && source.HasTransparency && !options.SupportsAlpha)
            warnings.Add(new("color-profile-conversion", "Grayscale colors will be converted to sRGB so the selected background retains its color."));
        if (source.IsLossy && !options.IsLossy)
            warnings.Add(new("loss-not-restored", "A lossless target does not restore detail already lost in the source."));
        if (options.Metadata == ImageMetadataMode.Preserve && (source.Resolution is not null || source.ProfileNames.Any(p => p is "exif" or "xmp")))
            warnings.Add(new("metadata-normalization", "Orientation, dimensions and resolution metadata will be normalized using supported fields; embedded EXIF/XMP thumbnails are removed to avoid stale previews."));
        return new(source, width, height, depth, warnings.ToImmutable(), blocked);
    }
}
