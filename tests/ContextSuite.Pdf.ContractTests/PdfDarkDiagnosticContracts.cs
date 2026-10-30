using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Pdf;

internal static class PdfDarkDiagnosticContracts
{
    public static void Run()
    {
        var cases = new (string Message, ImageFailure Failure)[]
        {
            ("Encrypted PDFs are unsupported.", ImageFailure.PdfDarkProtected),
            ("Active PDF navigation action is unsupported.", ImageFailure.PdfDarkProtected),
            ("Signed PDF form declarations are unsupported.", ImageFailure.PdfDarkProtected),
            ("PDF form resource kind is unsupported.", ImageFailure.PdfDarkStructure),
            ("Named PDF destination is missing.", ImageFailure.PdfDarkStructure),
            ("Conditional PDF layer usage is unsupported.", ImageFailure.PdfDarkStructure),
            ("Recolored image does not provide readable local text contrast.", ImageFailure.PdfDarkContrast),
            ("PDF opacity cannot reach the reading-palette contrast target.", ImageFailure.PdfDarkContrast),
            ("PDF fractional-fill text bounds are unknown.", ImageFailure.PdfDarkContrast),
            ("PDF knockout text over prior strokes is unsupported.", ImageFailure.PdfDarkContrast),
            ("PDF reading palette does not reach a stable repeat conversion.", ImageFailure.PdfDarkContrast),
            ("Ambiguous transparent image cannot be placed on dark paper safely.", ImageFailure.PdfDarkTransparency),
            ("Unsupported PDF soft mask.", ImageFailure.PdfDarkTransparency),
            ("Retained PDF soft mask matte is unsupported.", ImageFailure.PdfDarkTransparency),
            ("Fractional PDF transparency is unsupported.", ImageFailure.PdfDarkTransparency),
            ("PDF fractional fill overlaps an image backdrop.", ImageFailure.PdfDarkTransparency),
            ("PDF output intents are unsupported.", ImageFailure.PdfDarkColor),
            ("PDF gray ICC profile has unsupported features.", ImageFailure.PdfDarkColor),
            ("PDF gray ICC transform tag is unsupported.", ImageFailure.PdfDarkColor),
            ("Retained PDF ICC profile has unsupported features.", ImageFailure.PdfDarkColor),
            ("Retained PDF image color space is unsupported.", ImageFailure.PdfDarkColor),
            ("Only 8-bit PDF images can be classified.", ImageFailure.PdfDarkColor),
            ("Inline PDF images are outside the dark-mode policy.", ImageFailure.PdfDarkColor),
            ("Inline PDF images are unsupported.", ImageFailure.PdfDarkColor),
            ("PDF named paint color space is unsupported.", ImageFailure.PdfDarkColor),
            ("Named PDF destinations need bounded name resolution.", ImageFailure.PdfDarkStructure),
            ("PDF URI scheme is unsupported.", ImageFailure.PdfDarkStructure),
            ("PDF marked content maps to an unreachable structure element.", ImageFailure.PdfDarkStructure),
            ("PDF structure parent tree is missing.", ImageFailure.PdfDarkStructure),
            ("Tagged PDF page parent array is missing.", ImageFailure.PdfDarkStructure),
            ("Decorative PDF artifact contains marked content.", ImageFailure.PdfDarkStructure),
            ("PDF font bounding box cannot be measured for contrast.", ImageFailure.PdfDarkFont),
            ("Type 3 fonts can paint their own glyph colors and are unsupported.", ImageFailure.PdfDarkFont)
        };
        var count = 0;
        foreach (var (message, failure) in cases)
        {
            Check(3, message, failure);
            Check(3, "Page 1: " + message + "\r\n", failure, 1);
            Check(3, "Page 512: " + message, failure, 512);
            foreach (var prefix in new[] { "Page 0: ", "Page 513: ", "Page +1: ", "Page 01: ", "Page x: " })
                Check(3, prefix + message, ImageFailure.UnsupportedInput);
            Check(3, message + " private document content", ImageFailure.UnsupportedInput);
            Check(3, "private.pdf: " + message, ImageFailure.UnsupportedInput);
        }
        Check(3, "", ImageFailure.UnsupportedInput);
        Check(3, new string('X', 513), ImageFailure.UnsupportedInput);
        Check(3, "C:\\private\\lecture.pdf: parser error with confidential text", ImageFailure.UnsupportedInput);
        Check(3, "Page 1: Unsupported PDF soft mask.\nC:\\private\\lecture.pdf", ImageFailure.UnsupportedInput);
        Check(3, "Page 999999999999999999: Unsupported PDF soft mask.", ImageFailure.UnsupportedInput);
        Check(3, "Page 1: PDF fractional fill overlaps an image backdrop. secret.pdf", ImageFailure.UnsupportedInput);
        Check(3, "Direct PDF annotations require a pre-1.7 document.", ImageFailure.UnsupportedInput);
        Check(4, "Invalid PDF path coordinates.", ImageFailure.InvalidInput);
        Check(4, "Page 12: Invalid PDF path coordinate.", ImageFailure.InvalidInput, 12);
        Check(4, "Page 512: Retained PDF JPEG frame does not match its image geometry.", ImageFailure.InvalidInput, 512);
        Check(4, "Page 37: Retained PDF soft mask length is invalid.", ImageFailure.InvalidInput, 37);
        Check(4, "Retained PDF soft mask length is invalid.", ImageFailure.InvalidInput);
        Check(4, "Page 0: Retained PDF soft mask length is invalid.", ImageFailure.InvalidInput);
        Check(4, "Page 513: Retained PDF soft mask length is invalid.", ImageFailure.InvalidInput);
        Check(4, "Page 037: Retained PDF soft mask length is invalid.", ImageFailure.InvalidInput);
        Check(4, "Page 37: Retained PDF soft mask length is invalid. private.pdf", ImageFailure.InvalidInput);
        Check(4, "Page 37: Retained PDF soft mask length is invalid.\nprivate content", ImageFailure.InvalidInput);
        Check(4, "Page 1: Direct PDF annotations require a pre-1.7 document.", ImageFailure.InvalidInput, 1);
        Check(4, "Page 1: confidential.pdf: Invalid PDF path coordinate.", ImageFailure.InvalidInput);
        Check(4, "Page 513: Invalid PDF path coordinate.", ImageFailure.InvalidInput);
        Check(4, "Page 01: Invalid PDF path coordinate.", ImageFailure.InvalidInput);
        Check(4, "Page 1: Invalid PDF path coordinate. confidential content", ImageFailure.InvalidInput);
        Check(4, "C:\\private\\lecture.pdf: confidential document content", ImageFailure.InvalidInput);
        Check(4, new string('X', 2048), ImageFailure.InvalidInput);
        Check(4, "Type 3 fonts can paint their own glyph colors and are unsupported.", ImageFailure.InvalidInput);
        Check(5, "confidential resource", ImageFailure.ResourceLimit);
        Check(1, "confidential engine exception", ImageFailure.EngineFailure);
        Console.WriteLine($"Passed {count} bounded PDF dark diagnostic checks.");

        void Check(int exit, string diagnostic, ImageFailure expected, int? expectedPage = null)
        {
            var error = PdfDarkDiagnostic.Failure(exit, Encoding.UTF8.GetBytes(diagnostic));
            if (error is not ImageFailureException failure || failure.Failure != expected ||
                failure.PdfDarkPage != expectedPage || failure.Message != ImageFailureException.Describe(expected) ||
                failure.InnerException is not null)
                throw new InvalidOperationException("PDF dark diagnostic classification or privacy failed.");
            count++;
        }
    }
}
