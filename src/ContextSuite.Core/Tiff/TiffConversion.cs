using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Tiff;

public enum TiffTarget { Png, Jpeg, Pdf }

public sealed record TiffPageFacts(int Index, uint Width, uint Height, uint Orientation, bool HasTransparency,
    ImageResolution? Resolution, string? IccSha256, string? UnsupportedReason = null)
{
    public uint OutputWidth => Orientation is >= 5 and <= 8 ? Height : Width;
    public uint OutputHeight => Orientation is >= 5 and <= 8 ? Width : Height;

    public string? GetUnsupportedReason(TiffTarget target)
    {
        if (UnsupportedReason is not null || target != TiffTarget.Pdf) return UnsupportedReason;
        try { _ = CreatePdfPage(); return null; }
        catch (InvalidDataException) { return "This TIFF page's physical size exceeds PDF limits. Convert its pages to PNG or JPEG instead."; }
    }

    public RasterPdfPage CreatePdfPage()
    {
        Validate();
        double x = 96, y = 96;
        if (Resolution is { } resolution)
        {
            var rx = (double)(Orientation is >= 5 and <= 8 ? resolution.Y : resolution.X) / resolution.Divisor;
            var ry = (double)(Orientation is >= 5 and <= 8 ? resolution.X : resolution.Y) / resolution.Divisor;
            if (resolution.Unit == 1) y = 96 * ry / rx;
            else { x = rx * (resolution.Unit == 3 ? 2.54 : 1); y = ry * (resolution.Unit == 3 ? 2.54 : 1); }
        }
        var page = new RasterPdfPage(OutputWidth, OutputHeight, 8, 3, OutputWidth * 72 / x, OutputHeight * 72 / y, HasTransparency);
        page.Validate(); return page;
    }

    public void Validate()
    {
        Resolution?.Validate();
        if (Index is < 0 or >= TiffSourceFacts.MaximumPages || Width is 0 or > 16384 || Height is 0 or > 16384 ||
            (ulong)Width * Height > TiffSourceFacts.MaximumPagePixels || Orientation is < 1 or > 8 ||
            IccSha256 is not null && !TiffSourceFacts.ValidHash(IccSha256) || UnsupportedReason?.Length > 1024)
            throw new InvalidDataException("TIFF page facts exceed the supported limits.");
    }
}

public sealed record TiffSourceFacts(Guid ItemId, string Path, string Sha256, long FileBytes, ImmutableArray<TiffPageFacts> Pages)
{
    public const long MaximumFileBytes = 128L * 1024 * 1024;
    public const int MaximumPages = 128;
    public const long MaximumPagePixels = 16_000_000;
    public const long MaximumDocumentPixels = 128_000_000;

    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path) || !ValidHash(Sha256) || FileBytes is < 8 or > MaximumFileBytes ||
            Pages.IsDefaultOrEmpty || Pages.Length > MaximumPages)
            throw new InvalidDataException("Invalid TIFF source facts.");
        for (var i = 0; i < Pages.Length; i++)
        {
            if (Pages[i] is null || Pages[i].Index != i) throw new InvalidDataException("TIFF pages must retain their complete source order.");
            Pages[i].Validate();
        }
        if (Pages.Sum(page => (long)page.Width * page.Height) > MaximumDocumentPixels)
            throw new InvalidDataException("TIFF decoded pages exceed the document pixel budget.");
    }

    internal static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}

public sealed record TiffProbe(Guid ItemId, string Path)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path)) throw new InvalidDataException("Invalid TIFF probe.");
    }
}

public sealed record TiffConversionOptions(TiffTarget Target, uint? MatteRgb = null)
{
    public string Extension => Target switch { TiffTarget.Png => "png", TiffTarget.Jpeg => "jpg", TiffTarget.Pdf => "pdf", _ => throw new InvalidDataException("Invalid TIFF target.") };
    public void Validate()
    {
        if (!Enum.IsDefined(Target) || MatteRgb > 0xffffff || MatteRgb is not null && Target != TiffTarget.Jpeg)
            throw new InvalidDataException("Invalid TIFF output or background choice.");
    }
}

public sealed record TiffConversionPlan(Guid BatchId, ImmutableArray<TiffSourceFacts> Sources, TiffConversionOptions Options, BatchSettings Settings)
{
    public const long MaximumBatchPixels = 512_000_000;
    public const int MaximumBatchPages = 4096;
    public bool NeedsMatte => Options.Target == TiffTarget.Jpeg && Options.MatteRgb is null &&
        Sources.Any(source => source.Pages.Any(page => page.UnsupportedReason is null && page.HasTransparency));
    public bool HasExecutableItems => Options.Target == TiffTarget.Pdf
        ? Sources.Any(source => source.Pages.All(page => page.GetUnsupportedReason(Options.Target) is null))
        : Sources.Any(source => source.Pages.Any(page => page.UnsupportedReason is null));

    public static TiffConversionPlan Create(Guid batchId, IEnumerable<TiffSourceFacts> sources, TiffConversionOptions options, BatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var selected = sources.Take(MaximumBatchPages + 1).ToImmutableArray();
        if (batchId == Guid.Empty || settings?.Operation != "convert" || settings.Preferences is null ||
            selected.IsDefaultOrEmpty || selected.Length > MaximumBatchPages || selected.Any(source => source is null))
            throw new InvalidDataException("Invalid TIFF batch.");
        foreach (var source in selected) source.Validate();
        if (selected.Select(source => source.ItemId).Distinct().Count() != selected.Length ||
            selected.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length ||
            selected.Sum(source => source.Pages.Length) > MaximumBatchPages ||
            selected.Sum(source => source.Pages.Sum(page => (long)page.Width * page.Height)) > MaximumBatchPixels ||
            selected.Sum(source => (long)source.Path.Length + source.Pages.Sum(page => (long)(page.UnsupportedReason?.Length ?? 0))) > 1_000_000)
            throw new InvalidDataException("TIFF selection exceeds the batch limits or repeats a source.");
        settings.SelectOutput(false, false, false, false);
        return new(batchId, selected, options, settings);
    }

    public ConfirmedTiffConversion Confirm()
    {
        _ = Create(BatchId, Sources, Options, Settings);
        if (!HasExecutableItems) throw new InvalidDataException("No TIFF pages can use this output.");
        if (NeedsMatte) throw new InvalidDataException("Choose a background for transparent TIFF pages before JPEG conversion.");
        return new(this);
    }
}

public sealed class ConfirmedTiffConversion
{
    internal ConfirmedTiffConversion(TiffConversionPlan plan) { Plan = plan; }
    public TiffConversionPlan Plan { get; }
}

public sealed record TiffWork(TiffSourceFacts Source, TiffConversionOptions Options, int PageIndex, Guid OutputId, string TemporaryPath)
{
    public const int MaximumOutputBytes = 128 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null || Options is null) throw new InvalidDataException("Missing TIFF work facts.");
        Source.Validate(); Options.Validate();
        if (Options.Target == TiffTarget.Pdf ? PageIndex != -1 : PageIndex < 0 || PageIndex >= Source.Pages.Length)
            throw new InvalidDataException("Invalid TIFF page selection.");
        if (OutputId == Guid.Empty || OutputId == Source.ItemId || !PdfFileProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(Source.Path), System.IO.Path.GetFullPath(TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid TIFF output reservation.");
        var pages = Options.Target == TiffTarget.Pdf ? Source.Pages : [Source.Pages[PageIndex]];
        if (pages.Any(page => page.GetUnsupportedReason(Options.Target) is not null) ||
            Options.Target == TiffTarget.Jpeg && Options.MatteRgb is null && pages.Any(page => page.HasTransparency))
            throw new InvalidDataException("TIFF conversion needs supported pages and an explicit JPEG background when transparent.");
    }
}

public sealed record TiffWorkResult(OutputValidation Validation, Guid SourceId, int PageIndex, int PageCount,
    uint Width, uint Height, long OutputBytes, string SourceSha256, string EngineIdentity);

public sealed record TiffPreviewRequest(TiffSourceFacts Source, int PageIndex, uint MatteRgb)
{
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing TIFF preview source.");
        Source.Validate();
        if (PageIndex < 0 || PageIndex >= Source.Pages.Length || MatteRgb > 0xffffff || Source.Pages[PageIndex].UnsupportedReason is not null)
            throw new InvalidDataException("Invalid TIFF preview page or background.");
    }
}
