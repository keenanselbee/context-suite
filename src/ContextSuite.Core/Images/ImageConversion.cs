using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Dds;

namespace ContextSuite.Core.Images;

public enum ImageFormat { Png, Jpeg, WebP, Bmp, Tga, Dds }
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
    bool HasGrayscaleProfile = false, DdsInfo? Texture = null)
{
    public void Validate()
    {
        Resolution?.Validate();
        if ((Format == ImageFormat.Dds) != (Texture is not null) || Texture is { } texture &&
            (texture.Width != Width || texture.Height != Height || texture.FileBytes != FileBytes || !texture.IsSupported2D))
            throw new InvalidDataException("DDS image facts do not match their admitted texture header.");
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
    uint? MaximumDimension = null, uint? MatteRgb = null, ImageMetadataMode Metadata = ImageMetadataMode.Preserve,
    DdsConversionOptions? Texture = null, uint? ExtractMip = null)
{
    public bool IsLossy => Target == ImageFormat.Jpeg || (Target == ImageFormat.WebP && !WebPLossless) || (Target == ImageFormat.Dds && Texture?.IsCompressed == true);
    public DdsRepresentation? OutputRepresentation => Target != ImageFormat.Dds || Texture is null ? null : new(
        DdsConversionOptions.LinearFormat(Texture.Format) switch
        {
            DdsFormat.Bc1 => DdsCompression.BC1, DdsFormat.Bc2 => DdsCompression.BC2, DdsFormat.Bc3 => DdsCompression.BC3,
            DdsFormat.Bc4 or DdsFormat.Bc4Snorm => DdsCompression.BC4, DdsFormat.Bc5 or DdsFormat.Bc5Snorm => DdsCompression.BC5,
            DdsFormat.Bc7 => DdsCompression.BC7, DdsFormat.R8 => DdsCompression.R8, DdsFormat.Rg8 => DdsCompression.RG8,
            DdsFormat.Rgba8 => DdsCompression.RGBA8, DdsFormat.Bgra8 => DdsCompression.BGRA8,
            _ => throw new InvalidDataException("Unsupported DDS output name.")
        }, Texture.IsSrgb ? TextureTransfer.Srgb : TextureTransfer.Linear, Texture.IsSigned);
    public bool SupportsAlpha => Target is not (ImageFormat.Jpeg or ImageFormat.Bmp) &&
        (Target != ImageFormat.Dds || Texture is { Channels: 4, Alpha: not (DdsAlphaPolicy.Flatten or DdsAlphaPolicy.Discard) });
    public string Extension => Target switch
    {
        ImageFormat.Png => "png", ImageFormat.Jpeg => "jpg", ImageFormat.WebP => "webp", ImageFormat.Bmp => "bmp", ImageFormat.Tga => "tga", ImageFormat.Dds => "dds",
        _ => throw new InvalidDataException("Unknown image target.")
    };

    public void Validate()
    {
        Texture?.Validate();
        if ((Target == ImageFormat.Dds && Texture is null) || ExtractMip > 14 ||
            (ExtractMip is not null && (Texture is null || Target == ImageFormat.Dds)) || (Texture is not null && MaximumDimension is not null))
            throw new InvalidDataException("DDS conversion requires texture options; mip extraction must be explicit and resizing is not yet implemented.");
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
        if (source.Texture is { } sourceTexture && options.ExtractMip is { } mip && mip < sourceTexture.MipLevels)
        { width = Math.Max(1, width >> (int)mip); height = Math.Max(1, height >> (int)mip); }
        if (options.MaximumDimension is uint maximum && Math.Max(width, height) > maximum)
        {
            var scale = (double)maximum / Math.Max(width, height);
            width = Math.Max(1, (uint)Math.Round(width * scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (uint)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        }
        var depth = options.Target == ImageFormat.Png && source.BitDepth > 8 ? 16u : 8u;
        var warnings = ImmutableArray.CreateBuilder<OperationWarning>();
        var blocked = source.UnsupportedReason;
        if (source.Format == options.Target && source.Format != ImageFormat.Dds) blocked ??= "Same-format re-encoding belongs to Optimize.";
        if (source.HasTransparency && !options.SupportsAlpha && options.MatteRgb is null && options.Target != ImageFormat.Dds)
            blocked ??= "Select and preview an explicit background color for transparency.";
        if (source.Format == ImageFormat.Dds || options.Target == ImageFormat.Dds)
        {
            if (options.Texture is not { } textureOptions) blocked ??= "Choose DDS interpretation and texture policies.";
            else
            {
                try
                {
                    if (source.Texture is { } texture) textureOptions.ValidateSource(texture, source.HasTransparency && textureOptions.Purpose == DdsPurpose.Color);
                    else
                    {
                        if (textureOptions.ColorOperation == DdsColorOperation.Reinterpret) throw new InvalidDataException("Only DDS inputs permit declaration-only changes.");
                        if (textureOptions.IsCompressed && (width % 4 != 0 || height % 4 != 0)) throw new InvalidDataException("BC output requires dimensions divisible by four; no automatic resize is applied.");
                        if (textureOptions.Purpose == DdsPurpose.Color && textureOptions.SourceInterpretation != DdsInterpretation.Srgb)
                            throw new InvalidDataException("Choose sRGB interpretation for color-managed image input, or Data for numeric textures.");
                        if (textureOptions.Purpose != DdsPurpose.Color && source.ProfileNames.Any(p => p.ToLowerInvariant() is "icc" or "icm"))
                            throw new InvalidDataException("A profiled image cannot silently be treated as numeric texture data.");
                    }
                }
                catch (InvalidDataException error) { blocked ??= error.Message; }
                if (options.Target != ImageFormat.Dds)
                {
                    if (options.Target != ImageFormat.Png || textureOptions.Purpose != DdsPurpose.Color)
                        blocked ??= "Initial DDS image export supports color PNG only; numeric texture export needs a separate range policy.";
                    if (source.Texture is not { } input || options.ExtractMip is not { } level || level >= input.MipLevels)
                        blocked ??= "Explicitly select the DDS mip level to export.";
                    warnings.Add(new("dds-extraction", "Only the explicitly selected mip is exported to PNG; the original DDS is kept by default."));
                }
                warnings.Add(new("dds-policy", $"Texture purpose: {textureOptions.Purpose}; source interpretation: {textureOptions.SourceInterpretation}; mip policy: {textureOptions.Mips}; alpha: {textureOptions.Alpha}."));
                if (textureOptions.IsCompressed)
                    warnings.Add(new("dds-compression", $"{textureOptions.Format} uses lossy block compression. BC1 retains only binary alpha/fourth-channel values; BC2 quantizes them to 16 levels. Inspect the encoded preview."));
                if (textureOptions.Purpose == DdsPurpose.Normal)
                    warnings.Add(new("dds-normal", "BC5 stores X/Y and reconstructs positive Z. RGB negative-Z normals and one-channel inputs are rejected; normal-vector filtering can change stored values."));
                if (textureOptions.Purpose == DdsPurpose.Data && textureOptions.Format == DdsFormat.Bc1)
                    warnings.Add(new("dds-bc1-data", "BC1 data output requires an all-opaque fourth channel; transparent BC1 blocks cannot retain the other data channels. Choose BC3/BC7 when the fourth channel carries data."));
                if (textureOptions.ColorOperation == DdsColorOperation.Reinterpret)
                    warnings.Add(new("dds-reinterpret", "Only the declaration changes. Pixel values and compressed blocks are retained; appearance may change."));
                if (source.Texture is { } retainedTexture && textureOptions.ColorOperation != DdsColorOperation.Reinterpret && textureOptions.PreservesPayload(retainedTexture))
                    warnings.Add(new("dds-preserved-pixels", "The pixel representation is unchanged: payload bytes are retained. Copy mode still creates the requested named file."));
                if (textureOptions.Channels < 4) warnings.Add(new("dds-channels", $"Keep {textureOptions.RedChannel}" + (textureOptions.Channels == 2 ? $" and {textureOptions.GreenChannel}" : "") + "; all other channels are discarded."));
                if (textureOptions.Mips == DdsMipPolicy.Generate) warnings.Add(new("dds-mips", "Rebuild mip levels with area filtering; existing authored mip content is replaced. Tiny cutout mips can only approximate alpha coverage."));
                if (textureOptions.Header == DdsHeaderMode.Legacy) warnings.Add(new("dds-legacy", "Legacy headers do not retain explicit sRGB or alpha-mode declarations. Verify the target game's interpretation."));
                if (textureOptions.Alpha is DdsAlphaPolicy.Flatten or DdsAlphaPolicy.Discard or DdsAlphaPolicy.Cutout)
                    warnings.Add(new("dds-alpha", "The selected alpha policy changes transparency or discards it; review the encoded preview."));
            }
            if (options.Target == ImageFormat.Dds && options.Metadata == ImageMetadataMode.Preserve &&
                (source.Resolution is not null || source.ProfileNames.Length != 0 || source.HasOtherMetadata))
                blocked ??= "DDS cannot preserve image profiles or descriptive metadata; explicitly choose metadata removal.";
            if (options.Target == ImageFormat.Dds && source.ProfileNames.Any(p => p is "icc" or "icm"))
                warnings.Add(new("dds-profile", "Color input is transformed to sRGB before the selected DDS transfer; embedded profiles are not retained. Data input must not contain a color profile."));
        }
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
