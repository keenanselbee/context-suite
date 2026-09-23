using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// Independent PDF text boxes and rendered pixels. Bounds precede the first export;
// chart height tolerance is three raster pixels, not a fitted image comparison.
internal static class ExcelLayoutOracle
{
    internal sealed record Box(double Left, double Right, double Bottom, double Top);
    internal sealed record Glyph(char Character, Box Bounds);
    internal sealed record Bar(int Left, int Right, int Top, int Bottom, int Pixels);
    internal sealed record Observation(bool Passed, bool TextMatches, bool TextPlacement,
        bool BarValues, bool CategoryPlacement, Bar[] Bars, Glyph[] Glyphs);

    internal static Glyph[] ReadGlyphs(IntPtr text)
    {
        var count = FPDFText_CountChars(text);
        if (count is < 1 or > 4096) throw new InvalidDataException("Authored workbook text limit.");
        var result = new List<Glyph>();
        for (var index = 0; index < count; index++)
        {
            var character = FPDFText_GetUnicode(text, index);
            if (character > 127) throw new InvalidDataException("Unexpected workbook character.");
            if (FPDFText_GetCharBox(text, index, out var left, out var right, out var bottom, out var top) == 0)
                throw new InvalidDataException("Missing workbook character bounds.");
            result.Add(new((char)character, new(left, right, bottom, top)));
        }
        return result.ToArray();
    }

    internal static Observation Inspect(string text, Glyph[] glyphs, byte[] pixels, int width, int height, int stride,
        double pageWidth, double pageHeight)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || stride < width * 4 || stride > width * 4 + 16 || pixels.Length != (long)stride * height)
            throw new InvalidDataException("Authored chart raster dimensions.");
        var bars = FindBars(pixels, width, height, stride);
        var tokens = MatchesText(text);
        var heading = Find(glyphs, "MERGED HEADING ACROSS SEVEN COLUMNS");
        var first = Find(glyphs, "WRAPPED FIRST LINE");
        var second = Find(glyphs, "WRAPPED SECOND LINE");
        var end = Find(glyphs, "END_MARKER");
        var placement = heading is not null && first is not null && second is not null && end is not null &&
            heading.Right - heading.Left > 200 && heading.Bottom > first.Top &&
            first.Bottom > second.Top && first.Bottom - second.Bottom is > 9 and < 20 &&
            Math.Abs(first.Left - second.Left) <= 1 && end.Top < second.Bottom &&
            glyphs.Where(g => !char.IsWhiteSpace(g.Character)).All(g =>
                g.Bounds.Left >= 30 && g.Bounds.Right <= pageWidth - 30 && g.Bounds.Bottom >= 30 && g.Bounds.Top <= pageHeight - 30);
        var values = MatchesBars(bars) && bars.All(bar => bar.Left >= 30 * width / pageWidth &&
            bar.Right <= width - 30 * width / pageWidth && bar.Top >= 30 * height / pageHeight &&
            bar.Bottom <= height - 30 * height / pageHeight);
        var categories = bars.Length == 3;
        for (var index = 0; index < Math.Min(bars.Length, 3); index++)
        {
            var label = Find(glyphs, new[] { "ALPHA", "BETA", "GAMMA" }[index], last: true);
            var center = label is null ? double.NaN : (label.Left + label.Right) / 2 * width / pageWidth;
            var labelTop = label is null ? double.NaN : (pageHeight - label.Top) * height / pageHeight;
            categories &= center >= bars[index].Left - 4 && center <= bars[index].Right + 4 &&
                labelTop > bars[index].Bottom && labelTop - bars[index].Bottom < 40;
        }
        return new(tokens && placement && values && categories, tokens, placement, values, categories, bars, glyphs);
    }

    internal static bool NegativeControls()
    {
        Bar[] correct = [new(0, 19, 70, 99, 600), new(40, 59, 40, 99, 1200), new(80, 99, 10, 99, 1800)];
        return MatchesBars(correct) && !MatchesBars(correct.Reverse().ToArray()) && !MatchesBars(correct[..2]) &&
            !MatchesBars([correct[0], correct[1] with { Top = 45 }, correct[2]]) &&
            !MatchesBars([correct[0], correct[1], correct[2] with { Pixels = 100 }]) &&
            MatchesText(ExpectedText) && !MatchesText(ExpectedText.Replace("SECOND", "")) &&
            !MatchesText(ExpectedText + " UNEXPECTED") && !MatchesText(ExpectedText.Replace("30", "31"));
    }

    private const string ExpectedText = "MERGED HEADING ACROSS SEVEN COLUMNS WRAPPED FIRST LINE WRAPPED SECOND LINE ALPHA 10 BETA 20 GAMMA 30 END_MARKER ALPHA BETA GAMMA 0 10 20 30 40";
    private static bool MatchesText(string text) => Tokens(text).SequenceEqual(Tokens(ExpectedText));
    private static string[] Tokens(string text) => Regex.Split(text.Trim(), @"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Order(StringComparer.Ordinal).ToArray();

    internal static bool MatchesBars(Bar[] bars)
    {
        if (bars.Length != 3) return false;
        var heights = bars.Select(bar => bar.Bottom - bar.Top + 1).ToArray();
        return bars.All(bar => bar.Right - bar.Left >= 10 && bar.Bottom - bar.Top >= 10 &&
                bar.Pixels >= .95 * (bar.Right - bar.Left + 1) * (bar.Bottom - bar.Top + 1)) &&
            bars[0].Right < bars[1].Left && bars[1].Right < bars[2].Left &&
            bars.Max(bar => bar.Bottom) - bars.Min(bar => bar.Bottom) <= 1 &&
            Math.Abs(heights[1] - 2 * heights[0]) <= 3 && Math.Abs(heights[2] - 3 * heights[0]) <= 3 &&
            bars.Max(bar => bar.Right - bar.Left) - bars.Min(bar => bar.Right - bar.Left) <= 2;
    }

    internal static Bar[] FindBars(byte[] pixels, int width, int height, int stride)
    {
        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = y * stride + x * 4;
            mask[y * width + x] = pixels[offset] < 32 && pixels[offset + 1] < 32 && pixels[offset + 2] > 224;
        }
        var bars = new List<Bar>();
        for (var start = 0; start < mask.Length; start++)
        {
            if (!mask[start]) continue;
            var pending = new Stack<int>(); pending.Push(start); mask[start] = false;
            var left = width; var right = 0; var top = height; var bottom = 0; var count = 0;
            while (pending.TryPop(out var current))
            {
                var x = current % width; var y = current / width; count++;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                if (x > 0) Visit(current - 1);
                if (x + 1 < width) Visit(current + 1);
                if (y > 0) Visit(current - width);
                if (y + 1 < height) Visit(current + width);
            }
            if (count >= 4) bars.Add(new(left, right, top, bottom, count));
            void Visit(int position) { if (mask[position]) { mask[position] = false; pending.Push(position); } }
        }
        return bars.OrderBy(bar => bar.Left).ToArray();
    }

    internal static Box? Find(Glyph[] glyphs, string token, bool last = false)
    {
        var text = new string(glyphs.Select(g => g.Character).ToArray());
        var index = last ? text.LastIndexOf(token, StringComparison.Ordinal) : text.IndexOf(token, StringComparison.Ordinal);
        if (index < 0) return null;
        var found = glyphs.Skip(index).Take(token.Length).Where(g => !char.IsWhiteSpace(g.Character)).Select(g => g.Bounds).ToArray();
        return new(found.Min(b => b.Left), found.Max(b => b.Right), found.Min(b => b.Bottom), found.Max(b => b.Top));
    }

    [DllImport("pdfium-evaluation")] private static extern int FPDFText_CountChars(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern uint FPDFText_GetUnicode(IntPtr page, int index);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetCharBox(IntPtr page, int index, out double left, out double right, out double bottom, out double top);
}
