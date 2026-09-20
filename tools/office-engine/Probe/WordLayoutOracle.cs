using System.Text;
using System.Text.RegularExpressions;
using Glyph = OfficeLayoutGlyphs.Glyph;

internal static class WordLayoutOracle
{
    internal sealed record Observation(bool Passed, bool CharactersMatch, bool DirectionMatches,
        bool PlacementMatches, bool FontsMatch, bool ImageMatches, Glyph[] Glyphs);

    internal static Observation Inspect(string text, Glyph[] glyphs, byte[] pixels, int width, int height, int stride,
        double pageWidth, double pageHeight)
    {
        if (Math.Abs(pageWidth - 612) >= .1 || Math.Abs(pageHeight - 792) >= .1 ||
            width != Math.Ceiling(pageWidth * 4 / 3) || height != Math.Ceiling(pageHeight * 4 / 3) ||
            stride < width * 4 || stride > width * 4 + 16 || pixels.Length != stride * height)
            throw new InvalidDataException("Expected authored Letter page at 96 DPI.");
        var expected = WordLayoutFixtures.Latin + WordLayoutFixtures.Cjk + WordLayoutFixtures.Hebrew + WordLayoutFixtures.Marker;
        var normalized = Canonical(text);
        var characters = normalized.Order().SequenceEqual(Canonical(expected).Order()) &&
            normalized.Contains(Canonical(WordLayoutFixtures.Latin), StringComparison.Ordinal) &&
            normalized.Contains(WordLayoutFixtures.Cjk, StringComparison.Ordinal) &&
            normalized.Contains(WordLayoutFixtures.Marker, StringComparison.Ordinal) &&
            Canonical(new string(glyphs.Select(glyph => glyph.Character).ToArray())).Order().SequenceEqual(Canonical(expected).Order());
        var cjk = WordLayoutFixtures.Cjk.Select(character => glyphs.Where(glyph => glyph.Character == character).ToArray()).ToArray();
        var hebrew = WordLayoutFixtures.Hebrew.Select(character => glyphs.Where(glyph => glyph.Character == character).ToArray()).ToArray();
        var direction = cjk.Concat(hebrew).All(group => group.Length == 1) &&
            cjk.Zip(cjk.Skip(1)).All(pair => pair.First[0].Right <= pair.Second[0].Left + 1) &&
            hebrew.Zip(hebrew.Skip(1)).All(pair => pair.First[0].Left >= pair.Second[0].Right - 1);
        var placement = glyphs.Length > 0 && glyphs.All(glyph =>
            new[] { glyph.Left, glyph.Right, glyph.Top, glyph.Bottom, glyph.Size }.All(double.IsFinite) &&
            glyph.Left >= 71 && glyph.Right <= 541 && glyph.Top >= 71 && glyph.Bottom <= 217);
        // Four 24-point lines separated by twelve points. Expected text stays in
        // its own 36-point band; the anchored image must not reflow those lines.
        var bands = new[] { (72.0, WordLayoutFixtures.Latin), (108.0, WordLayoutFixtures.Cjk),
            (144.0, WordLayoutFixtures.Hebrew), (180.0, WordLayoutFixtures.Marker) };
        foreach (var (top, value) in bands)
        {
            var line = glyphs.Where(glyph => glyph.Top >= top - 1 && glyph.Bottom <= top + 36 + 1).ToArray();
            placement &= line.Length > 0 && Canonical(new string(line.Select(glyph => glyph.Character).ToArray())).Order()
                .SequenceEqual(Canonical(value).Order());
            if (line.Length > 0)
                placement &= value == WordLayoutFixtures.Hebrew ? Math.Abs(line.Max(glyph => glyph.Right) - 540) <= 4 :
                    Math.Abs(line.Min(glyph => glyph.Left) - 72) <= 4;
        }
        var fonts = glyphs.All(glyph =>
        {
            var name = Regex.Replace(glyph.Font, "^[A-Z]{6}\\+", "").Replace(" ", "");
            var family = WordLayoutFixtures.Cjk.Contains(glyph.Character) ? "MicrosoftYaHei" : "Arial";
            return glyph.Embedded && glyph.ProgramSha256.Length == 64 && Math.Abs(glyph.Size - 18) <= .01 &&
                name.StartsWith(family, StringComparison.Ordinal) && !name.Contains("Bold", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Italic", StringComparison.OrdinalIgnoreCase);
        });
        var image = Rectangle(360, 240, 72, 96, red: true) && Rectangle(432, 240, 72, 96, red: false);
        return new(characters && direction && placement && fonts && image, characters, direction, placement, fonts, image, glyphs);

        bool Rectangle(int x, int y, int w, int h, bool red)
        {
            var left = width; var right = -1; var top = height; var bottom = -1; var count = 0;
            for (var row = 0; row < height; row++)
            for (var column = 0; column < width; column++)
            {
                var offset = row * stride + column * 4;
                if (pixels[offset + 1] > 8 || (red ? pixels[offset] > 8 || pixels[offset + 2] < 247 :
                    pixels[offset + 2] > 8 || pixels[offset] < 247)) continue;
                left = Math.Min(left, column); right = Math.Max(right, column);
                top = Math.Min(top, row); bottom = Math.Max(bottom, row); count++;
            }
            var sx = width / pageWidth; var sy = height / pageHeight;
            return Math.Abs(left - x * sx) <= 2 && Math.Abs(right + 1 - (x + w) * sx) <= 2 &&
                Math.Abs(top - y * sy) <= 2 && Math.Abs(bottom + 1 - (y + h) * sy) <= 2 && count >= w * h * sx * sy * .97;
        }
    }

    internal static bool NegativeControls(string text, Glyph[] glyphs, byte[] pixels, int width, int height, int stride,
        double pageWidth, double pageHeight)
    {
        bool Accepted(string value, Glyph[] chars, byte[] raster) => Inspect(value, chars, raster, width, height, stride, pageWidth, pageHeight).Passed;
        if (!Accepted(text, glyphs, pixels)) return false;
        var rtl = glyphs.ToArray();
        var first = Array.FindIndex(rtl, glyph => glyph.Character == WordLayoutFixtures.Hebrew[0]);
        rtl[first] = rtl[first] with { Left = 72, Right = 90 };
        var fallback = glyphs.ToArray();
        var chinese = Array.FindIndex(fallback, glyph => glyph.Character == WordLayoutFixtures.Cjk[0]);
        fallback[chinese] = fallback[chinese] with { Font = "WrongFallback" };
        var moved = glyphs.ToArray(); moved[0] = moved[0] with { Top = 300, Bottom = 320 };
        var unembedded = glyphs.ToArray(); unembedded[0] = unembedded[0] with { Embedded = false };
        return !Accepted(text.Normalize(NormalizationForm.FormD).Replace("\u0301", ""), glyphs, pixels) &&
            !Accepted(text + " EXTRA", glyphs, pixels) && !Accepted(text, glyphs[1..], pixels) &&
            !Accepted(text, rtl, pixels) && !Accepted(text, fallback, pixels) && !Accepted(text, moved, pixels) &&
            !Accepted(text, unembedded, pixels) && !Accepted(text, glyphs, new byte[pixels.Length]);
    }

    private static string Canonical(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(character => !char.IsWhiteSpace(character) && !OfficeLayoutGlyphs.IsDirectionMark(character)).ToArray());
}
