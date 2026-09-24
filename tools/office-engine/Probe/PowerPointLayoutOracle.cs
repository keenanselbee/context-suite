using Glyph = OfficeLayoutGlyphs.Glyph;
using System.Text.RegularExpressions;

// Independent PDFium observations for the fixed D09 deck. Expectations are in
// source-authored points; they are not fitted to the renderer's output.
internal static class PowerPointLayoutOracle
{
    internal sealed record Label(string Text, double X, double Y, double Width, double Height,
        double Size, string Family = "Arial", bool Bold = false, bool Italic = false);
    internal sealed record Observation(bool Passed, bool TextMatches, bool PlacementMatches,
        bool FontsMatch, bool PixelsMatch, Glyph[] Glyphs);
    private static readonly Label[][] Labels = [
        [new("IMAGE_LAYOUT", 36, 24, 648, 40, 24, Bold: true), new("IMAGE_CAPTION", 72, 276, 400, 32, 18)],
        [new("DIAGRAM_TABLE", 36, 24, 648, 40, 24, Bold: true), new("START", 72, 90, 108, 36, 18),
         new("END", 324, 90, 108, 36, 18), new("ITEM", 78, 183, 204, 30, 18), new("VALUE", 294, 183, 204, 30, 18),
         new("ALPHA", 78, 219, 204, 30, 18), new("10", 294, 219, 204, 30, 18),
         new("BETA", 78, 255, 204, 30, 18), new("20", 294, 255, 204, 30, 18)],
        [new("MIXED_TYPOGRAPHY", 36, 24, 648, 40, 24, Bold: true), new("ARIAL_REGULAR", 72, 90, 576, 40, 20),
         new("ARIAL_BOLD", 72, 144, 576, 40, 24, Bold: true),
         new("TIMES_ITALIC", 72, 198, 576, 40, 22, "TimesNewRoman", Italic: true),
         new("COURIER_FIXED", 72, 252, 576, 40, 18, "CourierNew")]];

    internal static Observation Inspect(int index, string text, Glyph[] glyphs, byte[] pixels, int width, int height, int stride, double pageWidth, double pageHeight)
    {
        if (index is < 0 or > 2 || Math.Abs(pageWidth - 720) >= .1 || Math.Abs(pageHeight - 405) >= .1 ||
            width != Math.Ceiling(pageWidth * 4 / 3) || height != Math.Ceiling(pageHeight * 4 / 3) || stride < width * 4 || stride > width * 4 + 16 ||
            pixels.Length != stride * height) throw new InvalidDataException("Expected three 720x405-point slides at 96 DPI.");
        var scaleX = width / pageWidth; var scaleY = height / pageHeight;
        var expected = Labels[index];
        var tokens = Regex.Split(text.Trim(), @"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var textMatches = tokens.Order(StringComparer.Ordinal).SequenceEqual(expected.Select(label => label.Text).Order(StringComparer.Ordinal));
        var placement = true; var fonts = true;
        var joined = new string(glyphs.Select(glyph => glyph.Character).ToArray());
        foreach (var label in expected)
        {
            var start = joined.IndexOf(label.Text, StringComparison.Ordinal);
            if (start < 0) { placement = fonts = false; continue; }
            var found = glyphs.Skip(start).Take(label.Text.Length).ToArray();
            // Ink must fit the authored box, within one point of rounding, and
            // begin within four points horizontally of the explicit text origin.
            placement &= found.All(glyph => new[] { glyph.Left, glyph.Right, glyph.Top, glyph.Bottom, glyph.Size }.All(double.IsFinite) &&
                glyph.Left >= label.X - 1 && glyph.Right <= label.X + label.Width + 1 &&
                glyph.Top >= label.Y - 1 && glyph.Bottom <= label.Y + label.Height + 1) &&
                Math.Abs(found.Min(glyph => glyph.Left) - label.X) <= 4;
            fonts &= found.All(glyph => FontMatches(glyph, label));
        }
        if (index == 2)
            fonts &= expected.All(label => joined.Contains(label.Text, StringComparison.Ordinal)) &&
                expected.Select(label => glyphs[joined.IndexOf(label.Text, StringComparison.Ordinal)].ProgramSha256)
                    .Distinct(StringComparer.Ordinal).Count() == 4;
        var pixelMatches = index switch
        {
            0 => ColorRectangle(pixels, width, height, stride, scaleX, scaleY, 72, 108, 108, 72, 255, 0, 0) &&
                ColorRectangle(pixels, width, height, stride, scaleX, scaleY, 180, 108, 108, 72, 0, 255, 0) &&
                ColorRectangle(pixels, width, height, stride, scaleX, scaleY, 72, 180, 108, 72, 0, 0, 255) &&
                ColorRectangle(pixels, width, height, stride, scaleX, scaleY, 180, 180, 108, 72, 255, 255, 0),
            1 => Region(pixels, stride, scaleX, scaleY, 184, 107.5, 130, 1, 0, 0, 0, .95) &&
                Region(pixels, stride, scaleX, scaleY, 160, 116, 15, 5, 221, 238, 255, .99) &&
                Region(pixels, stride, scaleX, scaleY, 412, 116, 15, 5, 221, 255, 221, .99) &&
                // Stay inside each cell; the native table has visible row borders.
                Region(pixels, stride, scaleX, scaleY, 470, 187, 20, 20, 238, 238, 238, .99) &&
                Region(pixels, stride, scaleX, scaleY, 470, 223, 20, 20, 238, 238, 238, .99) &&
                Region(pixels, stride, scaleX, scaleY, 470, 259, 20, 20, 238, 238, 238, .99),
            _ => true
        };
        return new(textMatches && placement && fonts && pixelMatches, textMatches, placement, fonts, pixelMatches, glyphs);
    }

    private static bool FontMatches(Glyph glyph, Label label)
    {
        var name = Regex.Replace(glyph.Font, "^[A-Z]{6}\\+", "").Replace(" ", "");
        // Owner-approved 2026-09-24 for this fixed D09 fixture only; see
        // docs/powerpoint-layout-acceptance.md for the measured unit rounding.
        return glyph.Embedded && Math.Abs(glyph.Size - label.Size) <= .015 &&
            name.StartsWith(label.Family, StringComparison.Ordinal) &&
            name.Contains("Bold", StringComparison.OrdinalIgnoreCase) == label.Bold &&
            name.Contains("Italic", StringComparison.OrdinalIgnoreCase) == label.Italic;
    }

    private static bool ColorRectangle(byte[] pixels, int width, int height, int stride,
        double scaleX, double scaleY, int x, int y, int w, int h, int red, int green, int blue)
    {
        var left = width; var right = -1; var top = height; var bottom = -1; var count = 0;
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var offset = row * stride + column * 4;
            if (!Color(pixels, offset, red, green, blue)) continue;
            left = Math.Min(left, column); right = Math.Max(right, column);
            top = Math.Min(top, row); bottom = Math.Max(bottom, row); count++;
        }
        return Math.Abs(left - x * scaleX) <= 2 && Math.Abs(right + 1 - (x + w) * scaleX) <= 2 &&
            Math.Abs(top - y * scaleY) <= 2 && Math.Abs(bottom + 1 - (y + h) * scaleY) <= 2 &&
            count >= w * h * scaleX * scaleY * .97;
    }

    private static bool Region(byte[] pixels, int stride, double scaleX, double scaleY, double x, double y, double w, double h,
        int red, int green, int blue, double fraction)
    {
        var count = 0; var matches = 0;
        for (var row = (int)Math.Ceiling(y * scaleY); row < (y + h) * scaleY; row++)
        for (var column = (int)Math.Ceiling(x * scaleX); column < (x + w) * scaleX; column++)
        { count++; if (Color(pixels, row * stride + column * 4, red, green, blue)) matches++; }
        return count > 0 && matches >= count * fraction;
    }

    private static bool Color(byte[] bytes, int offset, int red, int green, int blue) =>
        Math.Abs(bytes[offset] - blue) <= 8 && Math.Abs(bytes[offset + 1] - green) <= 8 && Math.Abs(bytes[offset + 2] - red) <= 8;

    internal static bool NegativeControls(int index, string text, Glyph[] glyphs, byte[] pixels, int width, int height, int stride, double pageWidth, double pageHeight)
    {
        if (!Inspect(index, text, glyphs, pixels, width, height, stride, pageWidth, pageHeight).Passed) return false;
        var changed = glyphs.ToArray(); changed[0] = changed[0] with { Left = -10 };
        var font = glyphs.ToArray(); font[0] = font[0] with { Font = "UnexpectedFont" };
        var size = glyphs.ToArray(); size[0] = size[0] with { Size = size[0].Size + 1 };
        return !Inspect(index, text + " EXTRA", glyphs, pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            !Inspect(index, text.Replace(Labels[index][0].Text, "", StringComparison.Ordinal), glyphs, pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            !Inspect(index, text, glyphs[1..], pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            !Inspect(index, text, changed, pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            !Inspect(index, text, font, pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            !Inspect(index, text, size, pixels, width, height, stride, pageWidth, pageHeight).Passed &&
            (index == 2 || !Inspect(index, text, glyphs, new byte[pixels.Length], width, height, stride, pageWidth, pageHeight).Passed);
    }

}
