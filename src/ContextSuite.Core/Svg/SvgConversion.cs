using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Images;

namespace ContextSuite.Core.Svg;

public sealed record SvgProbe(Guid ItemId, string Path)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path)) throw new InvalidDataException("Invalid SVG probe.");
    }
}

public sealed record SvgSourceFacts(Guid ItemId, string Path, string Sha256, long FileBytes,
    double IntrinsicWidth, double IntrinsicHeight, uint MaximumSide, ulong MaximumPixels,
    string FontFingerprint, ImmutableArray<string> FontNotices)
{
    public const int MaximumFileBytes = 1024 * 1024;

    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path) || !ValidHash(Sha256) || FileBytes is < 1 or > MaximumFileBytes ||
            !double.IsFinite(IntrinsicWidth) || !double.IsFinite(IntrinsicHeight) ||
            IntrinsicWidth is <= 0 or > 1_000_000 || IntrinsicHeight is <= 0 or > 1_000_000 ||
            !((MaximumSide == 4096 && MaximumPixels == 16_000_000) || (MaximumSide == 1024 && MaximumPixels == 1_000_000)) ||
            !ValidHash(FontFingerprint) || FontNotices.IsDefault || FontNotices.Length > 32 ||
            FontNotices.Any(notice => string.IsNullOrWhiteSpace(notice) || notice.Length > 256 || notice.Any(char.IsControl)))
            throw new InvalidDataException("SVG facts exceed the supported limits.");
    }

    internal static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

public sealed record SvgPixelSize(uint Width, uint Height);

public sealed record SvgConversionOptions(uint? Width = null, ImageFormat Target = ImageFormat.Png, uint? MatteRgb = null)
{
    public string Extension => Target == ImageFormat.Jpeg ? "jpg" : "png";

    public void Validate()
    {
        if (Width is 0 or > 4096) throw new InvalidDataException("Image width must be between 1 and 4096 pixels.");
        if (Target is not (ImageFormat.Png or ImageFormat.Jpeg) || MatteRgb > 0xffffff ||
            (Target == ImageFormat.Png && MatteRgb is not null) || (Target == ImageFormat.Jpeg && MatteRgb is null))
            throw new InvalidDataException("SVG output requires PNG or JPEG with an explicit JPEG background.");
    }

    public SvgPixelSize GetSize(SvgSourceFacts source)
    {
        Validate(); source.Validate();
        double width, height;
        if (Width is { } selected)
        {
            width = selected;
            height = Math.Max(1, Math.Round(selected * source.IntrinsicHeight / source.IntrinsicWidth, MidpointRounding.AwayFromZero));
        }
        else
        {
            var scale = Math.Min(1, Math.Min(source.MaximumSide / Math.Max(source.IntrinsicWidth, source.IntrinsicHeight),
                Math.Sqrt(source.MaximumPixels / (source.IntrinsicWidth * source.IntrinsicHeight))));
            width = Math.Max(1, Math.Floor(source.IntrinsicWidth * scale));
            height = Math.Max(1, Math.Floor(source.IntrinsicHeight * scale));
        }
        if (width > source.MaximumSide || height > source.MaximumSide || width * height > source.MaximumPixels)
            throw new InvalidDataException("This image size exceeds the SVG rendering limit. Choose a smaller width.");
        return new((uint)width, (uint)height);
    }
}

public sealed record SvgConversionPlan(Guid BatchId, ImmutableArray<SvgSourceFacts> Sources,
    SvgConversionOptions Options, BatchSettings Settings)
{
    public const int MaximumSources = 1024;
    public const long MaximumBatchPixels = 512_000_000;

    public static SvgConversionPlan Create(Guid batchId, IEnumerable<SvgSourceFacts> sources, SvgConversionOptions options, BatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var selected = sources.Take(MaximumSources + 1).ToImmutableArray();
        if (batchId == Guid.Empty || settings?.Operation != "convert" || settings.Preferences is null ||
            selected.IsDefaultOrEmpty || selected.Length > MaximumSources || selected.Any(source => source is null))
            throw new InvalidDataException("Invalid SVG conversion batch.");
        long pixels = 0, text = 0;
        foreach (var source in selected)
        {
            var size = options.GetSize(source);
            pixels += (long)size.Width * size.Height;
            text += source.Path.Length + source.FontNotices.Sum(notice => (long)notice.Length);
        }
        if (selected.Select(source => source.ItemId).Distinct().Count() != selected.Length ||
            selected.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length ||
            pixels > MaximumBatchPixels || text > 1_000_000)
            throw new InvalidDataException("SVG selection exceeds the batch limit or repeats a source.");
        settings.SelectOutput(false, false, false, false);
        return new(batchId, selected, options, settings);
    }

    public ConfirmedSvgConversion Confirm()
    {
        _ = Create(BatchId, Sources, Options, Settings);
        return new(this);
    }
}

public sealed class ConfirmedSvgConversion
{
    internal ConfirmedSvgConversion(SvgConversionPlan plan) { Plan = plan; }
    public SvgConversionPlan Plan { get; }
}

public sealed record SvgWork(SvgSourceFacts Source, SvgConversionOptions Options, Guid OutputId, string TemporaryPath)
{
    public const int MaximumOutputBytes = 128 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null || Options is null) throw new InvalidDataException("Missing SVG conversion facts.");
        _ = Options.GetSize(Source);
        if (OutputId == Guid.Empty || OutputId == Source.ItemId || !PdfFileProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(Source.Path), System.IO.Path.GetFullPath(TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid SVG output reservation.");
    }
}

public sealed record SvgWorkResult(OutputValidation Validation, Guid SourceId, uint Width, uint Height, long OutputBytes,
    string SourceSha256, string FontFingerprint, string EngineIdentity, ImageFormat Format = ImageFormat.Png);
