using ContextSuite.Core.Images;

namespace ContextSuite.Core.Pdf;

public static class PdfDarkFailures
{
    public static string Describe(ImageFailure failure, int? page = null)
    {
        if (page is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(page));
        var message = failure switch
        {
            ImageFailure.InvalidInput => "This PDF appears damaged or contains invalid page data. Re-export it from the source document or try an undamaged copy. Original kept.",
            ImageFailure.UnsupportedInput => "This PDF uses a feature that dark mode cannot safely recolor. Try a static PDF export from the source document. Original kept.",
            ImageFailure.ResourceLimit => "This PDF exceeds a dark-mode processing limit. Try a smaller PDF. Original kept.",
            _ => ImageFailureException.Describe(failure)
        };
        return page is { } number ? $"Page {number}: {message}" : message;
    }

    public static bool IsFeatureLimit(ImageFailure failure) => failure is
        ImageFailure.PdfDarkContrast or ImageFailure.PdfDarkTransparency or
        ImageFailure.PdfDarkStructure or ImageFailure.PdfDarkColor or
        ImageFailure.PdfDarkFont or ImageFailure.PdfDarkProtected;

    public static bool IsUnsupported(ImageFailure failure) => IsFeatureLimit(failure) || failure is
        ImageFailure.InvalidInput or ImageFailure.UnsupportedInput or ImageFailure.ResourceLimit;
}
