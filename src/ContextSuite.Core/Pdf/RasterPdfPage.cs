namespace ContextSuite.Core.Pdf;

// Geometry and sample layout for one decoded raster page. Source identity is
// owned by the caller and is deliberately absent from this PDF contract.
public sealed record RasterPdfPage(uint Width, uint Height, int BitsPerComponent, int ColorComponents,
    double WidthPoints, double HeightPoints, bool HasTransparency)
{
    public static RasterPdfPage FromImagePage(ImagePdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Validate();
        return new(page.Width, page.Height, page.BitsPerComponent, page.ColorComponents,
            page.WidthPoints, page.HeightPoints, page.Source.HasTransparency);
    }

    public void Validate()
    {
        if (Width is 0 or > 16384 || Height is 0 or > 16384 ||
            (ulong)Width * Height > 16_000_000 || BitsPerComponent is not (8 or 16) ||
            ColorComponents is not (1 or 3) || !double.IsFinite(WidthPoints) ||
            !double.IsFinite(HeightPoints) || WidthPoints is < 0.01 or > 14400 ||
            HeightPoints is < 0.01 or > 14400)
            throw new InvalidDataException("Invalid raster PDF page geometry or sample layout.");
    }

    public static void ValidatePages(IReadOnlyList<RasterPdfPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count is 0 or > ImagePdfPlan.MaximumPages)
            throw new InvalidDataException("Invalid raster PDF page count.");
        long pixels = 0;
        foreach (var page in pages)
        {
            if (page is null) throw new InvalidDataException("Missing raster PDF page.");
            page.Validate();
            pixels += (long)page.Width * page.Height;
            if (pixels > ImagePdfPlan.MaximumPixels)
                throw new InvalidDataException("Raster PDF pages exceed the pixel budget.");
        }
    }
}
