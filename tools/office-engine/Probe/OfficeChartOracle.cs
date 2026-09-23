using System.Text.RegularExpressions;

// Authored bounds precede export. Reuse the unchanged three-pixel 1:2:3 bar rule.
internal static class OfficeChartOracle
{
    internal sealed record Observation(bool Passed, bool TextMatches, bool TextPlacement,
        bool BarValues, bool CategoryPlacement, ExcelLayoutOracle.Bar[] Bars);

    internal static Observation Inspect(string text, ExcelLayoutOracle.Glyph[] glyphs, byte[] pixels,
        int width, int height, int stride, double pageWidth, double pageHeight, bool word)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || stride < width * 4 ||
            stride > width * 4 + 16 || pixels.Length != (long)stride * height)
            throw new InvalidDataException("Authored chart raster dimensions.");
        var title = word ? "WORD CHART CONTROL" : "POWERPOINT CHART CONTROL";
        var tokens = Tokens(text).SequenceEqual(Tokens(title + " ALPHA BETA GAMMA 0 10 20 30 40"));
        var bars = ExcelLayoutOracle.FindBars(pixels, width, height, stride);
        var values = ExcelLayoutOracle.MatchesBars(bars);
        var heading = ExcelLayoutOracle.Find(glyphs, title);
        var placement = heading is not null && heading.Left >= 30 && heading.Right <= pageWidth - 30 &&
            pageHeight - heading.Top is >= 30 and <= 90 &&
            glyphs.Where(g => !char.IsWhiteSpace(g.Character)).All(g => g.Bounds.Left >= 30 &&
                g.Bounds.Right <= pageWidth - 30 && g.Bounds.Bottom >= 30 && g.Bounds.Top <= pageHeight - 30) &&
            bars.Length == 3 && bars.All(bar => bar.Left * pageWidth / width >= 67 &&
                bar.Right * pageWidth / width <= 509 && bar.Top * pageHeight / height > pageHeight - heading.Bottom &&
                bar.Bottom * pageHeight / height < (word ? 400 : 365));
        var categories = bars.Length == 3;
        for (var index = 0; index < Math.Min(3, bars.Length); index++)
        {
            var label = ExcelLayoutOracle.Find(glyphs, new[] { "ALPHA", "BETA", "GAMMA" }[index]);
            var center = label is null ? double.NaN : (label.Left + label.Right) / 2 * width / pageWidth;
            var top = label is null ? double.NaN : (pageHeight - label.Top) * height / pageHeight;
            categories &= center >= bars[index].Left - 4 && center <= bars[index].Right + 4 &&
                top > bars[index].Bottom && top - bars[index].Bottom < 40;
        }
        return new(tokens && placement && values && categories, tokens, placement, values, categories, bars);
    }

    internal static bool NegativeControls()
    {
        const int width = 960, height = 540, stride = width * 4;
        var pixels = Enumerable.Repeat((byte)255, stride * height).ToArray();
        for (var index = 0; index < 3; index++)
        for (var y = 400 - (index + 1) * 60; y < 400; y++)
        for (var x = 200 + index * 180; x < 240 + index * 180; x++)
        {
            var offset = y * stride + x * 4;
            pixels[offset] = pixels[offset + 1] = 0;
        }
        var glyphs = new List<ExcelLayoutOracle.Glyph>();
        Add("POWERPOINT CHART CONTROL", 40, 40);
        Add("ALPHA", 155, 307); Add("BETA", 290, 307); Add("GAMMA", 425, 307);
        Add("0 10 20 30 40", 75, 150);
        var text = new string(glyphs.Select(g => g.Character).ToArray());
        var baseline = glyphs.ToArray();
        bool Check(string value, ExcelLayoutOracle.Glyph[] letters, byte[] raster) =>
            Inspect(value, letters, raster, width, height, stride, 720, 405, false).Passed;
        var missing = Enumerable.Repeat((byte)255, pixels.Length).ToArray();
        var badValues = (byte[])pixels.Clone();
        for (var y = 280; y < 295; y++)
        for (var x = 380; x < 420; x++)
            Array.Fill(badValues, (byte)255, y * stride + x * 4, 4);
        var swapped = baseline.Select(g => g.Bounds.Left is >= 290 and <= 310
            ? g with { Bounds = g.Bounds with { Left = g.Bounds.Left + 130, Right = g.Bounds.Right + 130 } } : g).ToArray();
        var clipped = baseline.Select(g => g with { Bounds = g.Bounds with { Left = -1 } }).ToArray();
        return Check(text, baseline, pixels) && !Check(text.Replace("CONTROL", ""), baseline, pixels) &&
            !Check(text + " EXTRA", baseline, pixels) && !Check(text, baseline, missing) &&
            !Check(text, baseline, badValues) && !Check(text, swapped, pixels) && !Check(text, clipped, pixels);

        void Add(string value, double left, double top)
        {
            foreach (var character in value)
            {
                glyphs.Add(new(character, new(left, left + 4, 405 - top - 8, 405 - top)));
                left += 4;
            }
            glyphs.Add(new(' ', new(left, left, 405 - top, 405 - top)));
        }
    }

    private static string[] Tokens(string text) => Regex.Split(text.Trim(), @"\s+",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Order(StringComparer.Ordinal).ToArray();
}
