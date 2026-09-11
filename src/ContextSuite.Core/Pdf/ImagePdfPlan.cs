using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Pdf;

public sealed record ImagePdfPage(ImageSourceFacts Source, uint Width, uint Height, int BitsPerComponent,
    int ColorComponents, double WidthPoints, double HeightPoints)
{
    public static ImagePdfPage Create(ImageSourceFacts source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Validate();
        if (source.Format == ImageFormat.Dds || source.UnsupportedReason is not null ||
            source.FileBytes > 128L * 1024 * 1024 || (long)source.Width * source.Height > 16_000_000)
            throw new NotSupportedException("This image cannot currently be placed in a PDF without losing supported image information.");
        var rotated = source.Orientation is >= 5 and <= 8;
        var width = rotated ? source.Height : source.Width;
        var height = rotated ? source.Width : source.Height;
        var resolution = source.Resolution;
        double x = 96, y = 96;
        if (resolution is not null)
        {
            var rx = (double)(rotated ? resolution.Y : resolution.X) / resolution.Divisor;
            var ry = (double)(rotated ? resolution.X : resolution.Y) / resolution.Divisor;
            if (resolution.Unit == 1) y = 96 * ry / rx;
            else { x = rx * (resolution.Unit == 3 ? 2.54 : 1); y = ry * (resolution.Unit == 3 ? 2.54 : 1); }
        }
        var widthPoints = width * 72 / x;
        var heightPoints = height * 72 / y;
        if (!double.IsFinite(widthPoints) || !double.IsFinite(heightPoints) || widthPoints is < 0.01 or > 14400 || heightPoints is < 0.01 or > 14400)
            throw new NotSupportedException("The image's physical page size exceeds this PDF policy.");
        return new(source, width, height, source.BitDepth > 8 ? 16 : 8, source.HasGrayscaleProfile ? 1 : 3, widthPoints, heightPoints);
    }

    public void Validate()
    {
        if (Source is null || this != Create(Source)) throw new InvalidDataException("Image PDF page facts do not match the source.");
    }
}

// The owner selected one combined PDF. Selection order is a proposal until the
// focused multi-image order review confirms it; no general planner is required.
public sealed record ImagePdfPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<ImagePdfPage> Pages)
{
    public const string Policy = "images-combined-pdf-1";
    public const int MaximumPages = 4096;
    public const long MaximumSourceBytes = 512L * 1024 * 1024;
    public const long MaximumPixels = 128_000_000;
    public const int MaximumOutputBytes = 128 * 1024 * 1024;
    public bool NeedsOrderReview => !Pages.IsDefault && Pages.Length > 1;
    public ContextSuite.Core.Operations.OutputPolicy Output => Settings.SelectOutput(false, false, false, false);

    public static ImagePdfPlan Create(Guid batchId, IEnumerable<ImageSourceFacts> orderedSources, BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "convert" || settings.Preferences is null)
            throw new InvalidDataException("Invalid combined PDF settings.");
        ArgumentNullException.ThrowIfNull(orderedSources);
        var sources = orderedSources.Take(MaximumPages + 1).ToImmutableArray();
        if (sources.IsEmpty || sources.Length > MaximumPages || sources.Any(source => source is null) ||
            sources.Select(source => source.ItemId).Distinct().Count() != sources.Length)
            throw new InvalidDataException("Invalid combined PDF selection.");
        var pages = sources.Select(ImagePdfPage.Create).ToImmutableArray();
        if (settings.Preferences.OutputDirectory is { } folder && !PdfFileProbe.ValidPath(folder) ||
            sources.Sum(source => (long)source.Path.Length + source.ColorDescription.Length +
                (source.UnsupportedReason?.Length ?? 0) + source.ProfileNames.Sum(name => name.Length)) > 1_000_000)
            throw new InvalidDataException("Combined PDF settings or source descriptions exceed the request budget.");
        if (sources.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length)
            throw new InvalidDataException("An image can appear only once in this combined PDF selection.");
        if (sources.Sum(source => source.FileBytes) > MaximumSourceBytes || pages.Sum(page => (long)page.Width * page.Height) > MaximumPixels)
            throw new NotSupportedException("The combined PDF selection exceeds this build's input budget.");
        settings.SelectOutput(false, false, false, false);
        return new(batchId, settings, pages);
    }

    public ConfirmedImagePdf Confirm(bool pageOrderReviewed)
    {
        if (Pages.IsDefaultOrEmpty) throw new InvalidDataException("No images selected for PDF.");
        foreach (var page in Pages) page.Validate();
        _ = Create(BatchId, Pages.Select(page => page.Source), Settings);
        if (NeedsOrderReview && !pageOrderReviewed) throw new InvalidDataException("Review the combined PDF page order first.");
        return new(this);
    }
}

public sealed class ConfirmedImagePdf
{
    internal ConfirmedImagePdf(ImagePdfPlan plan) { Plan = plan; }
    public ImagePdfPlan Plan { get; }
}
