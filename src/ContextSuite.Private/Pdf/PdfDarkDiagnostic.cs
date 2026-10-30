using System.Globalization;
using System.Text;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Pdf;

internal static class PdfDarkDiagnostic
{
    public static Exception Failure(int exitCode, ReadOnlyMemory<byte> bytes)
    {
        var classified = exitCode switch
        {
            3 => Classify(bytes, invalidInput: false),
            4 => Classify(bytes, invalidInput: true),
            5 => (ImageFailure.ResourceLimit, (int?)null),
            _ => (ImageFailure.EngineFailure, (int?)null)
        };
        return new ImageFailureException(classified.Item1, pdfDarkPage: classified.Item2);
    }

    private static (ImageFailure, int?) Classify(ReadOnlyMemory<byte> bytes, bool invalidInput)
    {
        // Only exact, content-free native policy messages select a public enum.
        // Neither parser diagnostics nor file paths are forwarded to IPC or UI.
        var fallback = invalidInput ? ImageFailure.InvalidInput : ImageFailure.UnsupportedInput;
        if (bytes.Length is 0 or > 512) return (fallback, null);
        var message = Encoding.UTF8.GetString(bytes.Span).Trim();
        int? page = null;
        if (message.StartsWith("Page ", StringComparison.Ordinal))
        {
            var colon = message.IndexOf(": ", StringComparison.Ordinal);
            if (colon is < 6 or > 8 || !int.TryParse(message.AsSpan(5, colon - 5),
                NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is < 1 or > 512 ||
                !message.AsSpan(5, colon - 5).SequenceEqual(number.ToString(CultureInfo.InvariantCulture)))
                return (fallback, null);
            page = number;
            message = message[(colon + 2)..];
        }
        if (invalidInput)
            return message is "Invalid PDF path coordinates." or "Invalid PDF path coordinate." or
                "Retained PDF JPEG frame does not match its image geometry." or
                "Retained PDF soft mask length is invalid." or
                "Direct PDF annotations require a pre-1.7 document."
                ? (ImageFailure.InvalidInput, page) : (ImageFailure.InvalidInput, null);
        var failure = message switch
        {
            "Encrypted PDFs are unsupported." or "Signed PDFs are unsupported." or
            "Interactive PDF forms are unsupported." or "Interactive PDF page features are unsupported."
                or "Active PDF catalog feature is unsupported." or "Active PDF name tree is unsupported."
                or "Active PDF annotation is unsupported." or "Active PDF link action is unsupported."
                or "Active PDF navigation action is unsupported." or "Signed PDF form declarations are unsupported."
                => ImageFailure.PdfDarkProtected,
            "Conditional PDF layer configuration is unsupported." or
            "Conditional PDF layer usage is unsupported." or
            "Unsupported PDF page-element layer usage." or
            "Unsupported PDF view-layer usage." or
            "PDF layer base state is unsupported." or
            "Conditional PDF layer membership is unsupported." or
            "Named PDF destinations need bounded name resolution." or
            "PDF URI scheme is unsupported." or
            "PDF marked content maps to an unreachable structure element." or
            "PDF structure parent tree is missing." or
            "Tagged PDF page parent array is missing." or
            "Decorative PDF artifact contains marked content." or
            "PDF form resource kind is unsupported." or "Named PDF destination is missing."
                => ImageFailure.PdfDarkStructure,
            "Overlapping page fills and form text need contrast-aware recoloring." or
            "Overlapping form fills and page text need contrast-aware recoloring." or
            "Overlapping PDF forms need contrast-aware recoloring." or
            "PDF knockout text over prior strokes is unsupported." or
            "PDF reading palette does not reach a stable repeat conversion." or
            "PDF opacity cannot reach the reading-palette contrast target." or
            "Text over a retained light image needs local contrast validation." or
            "Unbounded dark fill and light text need contrast-aware recoloring." or
            "Light text with a dark filled region needs measurable bounds for contrast-aware recoloring." or
            "Ambiguous dark fill and light text need contrast-aware recoloring." or
            "Covered dark panel and light text need contrast-aware recoloring." or
            "PDF fractional-fill text bounds are unknown." or
            "Recolored image does not provide readable local text contrast." or
            "Intervening paint prevents image/text contrast validation." or
            "PDF translucent panel cannot retain readable text contrast." or
            "Text over a recolored image needs local image contrast validation."
                => ImageFailure.PdfDarkContrast,
            "Ambiguous transparent image cannot be placed on dark paper safely." or
            "Text over a transparent image requires composite contrast validation." or
            "Fractional PDF text or strokes are unsupported." or
            "Fractional PDF image or form placement is unsupported." or
            "Fractional PDF transparency is unsupported." or
            "Unbounded or clipped fractional PDF fills are unsupported." or
            "PDF fractional fill needs a verified dark paper backdrop and bounded opacity." or
            "PDF fractional fill overlaps an image backdrop." or
            "PDF fractional fill overlaps a form backdrop." or
            "PDF fractional fill has an ambiguous painted backdrop." or
            "PDF page transparency group is unsupported." or
            "Form transparency group is outside the dark-mode policy." or
            "Unsupported PDF soft mask." or
            "Retained PDF soft mask has unsupported features." or
            "Retained PDF soft mask decode array is unsupported." or
            "Retained PDF soft mask matte is unsupported."
                => ImageFailure.PdfDarkTransparency,
            "PDF uses a color, pattern, shading or inline image outside the dark-mode policy." or
            "PDF image color or mask is outside the supported policy." or
            "PDF ICC image profile white point is unsupported." or
            "PDF output intents are unsupported." or
            "PDF gray ICC profile has unsupported features." or
            "PDF gray ICC transform tag is unsupported." or
            "Retained PDF ICC profile has unsupported features." or
            "Retained PDF image color space is unsupported." or
            "Only 8-bit PDF images can be classified." or
            "Inline PDF images are outside the dark-mode policy." or
            "Inline PDF images are unsupported." or
            "PDF named paint color space is unsupported."
                => ImageFailure.PdfDarkColor,
            "Type 3 fonts can paint their own glyph colors and are unsupported." or
            "Unexpected PDF font subtype is unsupported." or
            "Type 0 font descendants are unsupported." or
            "Unexpected Type 0 descendant font is unsupported." or
            "PDF font bounding box cannot be measured for contrast."
                => ImageFailure.PdfDarkFont,
            _ => ImageFailure.UnsupportedInput
        };
        return (failure, failure == ImageFailure.UnsupportedInput ? null : page);
    }
}
