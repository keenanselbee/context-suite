using System.Runtime.InteropServices;
using System.Security.Cryptography;

// Authored single-page move fixture only. This is not a customer PDF validator.
internal static class WordMoveSpacing
{
    // Owner accepts the measured 0.070007-point shift; round upward by 0.001 point.
    internal const double MaximumHorizontalPoints = 0.071;
    internal sealed record Glyph(uint Unicode, double X, double Y, double Left, double Right,
        double Bottom, double Top, double Size, string FontSha256);
    internal sealed record Observation(string[] Text, Glyph[] Glyphs);
    internal sealed record Comparison(bool Passed, double MaximumHorizontalPoints,
        double MaximumVerticalPoints, int Characters, bool ContentAndFormattingMatch);

    internal static Observation Read(string path)
    {
        var glyphs = new List<Glyph>();
        var fonts = new Dictionary<IntPtr, string>();
        var text = PdfTextReader.Read(path, (page, textPage) =>
        {
            var objects = FPDFPage_CountObjects(page);
            if (objects is < 1 or > 64 || FPDFPage_GetAnnotCount(page) != 0)
                throw new InvalidDataException("Move fixture must contain only bounded plain text.");
            for (var index = 0; index < objects; index++)
            {
                var item = FPDFPage_GetObject(page, index);
                if (FPDFPageObj_GetType(item) != 1 || FPDFTextObj_GetTextRenderMode(item) != 0)
                    throw new InvalidDataException("Unexpected move fixture page object or rendering mode.");
            }
            var count = FPDFText_CountChars(textPage);
            if (count is < 1 or > 256) throw new InvalidDataException("Move character limit.");
            for (var index = 0; index < count; index++)
            {
                var unicode = FPDFText_GetUnicode(textPage, index);
                if (unicode is 10 or 13) continue; // Generated line separators remain in exact Text.
                var item = FPDFText_GetTextObject(textPage, index);
                var font = FPDFTextObj_GetFont(item);
                if (unicode == 0 || item == IntPtr.Zero || font == IntPtr.Zero ||
                    FPDFFont_GetIsEmbedded(font) != 1 ||
                    FPDFText_GetCharOrigin(textPage, index, out var x, out var y) == 0 ||
                    FPDFText_GetCharBox(textPage, index, out var left, out var right, out var bottom, out var top) == 0 ||
                    FPDFText_GetFillColor(textPage, index, out var r, out var g, out var b, out var a) == 0 ||
                    r != 0 || g != 0 || b != 0 || a != 255 || FPDFText_GetCharAngle(textPage, index) != 0)
                    throw new InvalidDataException("Unexpected move character formatting.");
                if (!fonts.TryGetValue(font, out var hash))
                {
                    if (FPDFFont_GetFontData(font, null, 0, out var length) == 0 || length is < 1 or > 1048576)
                        throw new InvalidDataException("Move embedded font limit.");
                    var bytes = new byte[(int)length];
                    if (FPDFFont_GetFontData(font, bytes, length, out var written) == 0 || written != length)
                        throw new InvalidDataException("Move embedded font read failed.");
                    fonts.Add(font, hash = Convert.ToHexString(SHA256.HashData(bytes)));
                }
                var size = FPDFText_GetFontSize(textPage, index);
                if (size != 12 || new[] { x, y, left, right, bottom, top }.Any(value => !double.IsFinite(value)))
                    throw new InvalidDataException("Unexpected move character dimensions.");
                glyphs.Add(new(unicode, x, y, left, right, bottom, top, size, hash));
            }
        });
        if (text.Length != 1) throw new InvalidDataException("Move fixture must have one page.");
        return new(text, glyphs.ToArray());
    }

    internal static Comparison Compare(Observation before, Observation after)
    {
        var content = before.Text.SequenceEqual(after.Text, StringComparer.Ordinal) &&
            before.Glyphs.Length == after.Glyphs.Length && before.Glyphs.Length > 0;
        double horizontal = 0, vertical = 0;
        foreach (var (left, right) in before.Glyphs.Zip(after.Glyphs))
        {
            content &= left.Unicode == right.Unicode && left.Size == right.Size && left.FontSha256 == right.FontSha256;
            content &= new[] { left.X, left.Y, left.Left, left.Right, left.Bottom, left.Top,
                right.X, right.Y, right.Left, right.Right, right.Bottom, right.Top }.All(double.IsFinite);
            horizontal = Math.Max(horizontal, new[] { Math.Abs(left.X - right.X), Math.Abs(left.Left - right.Left), Math.Abs(left.Right - right.Right) }.Max());
            vertical = Math.Max(vertical, new[] { Math.Abs(left.Y - right.Y), Math.Abs(left.Bottom - right.Bottom), Math.Abs(left.Top - right.Top) }.Max());
        }
        return new(content && horizontal <= MaximumHorizontalPoints && vertical == 0,
            horizontal, vertical, before.Glyphs.Length, content);
    }

    internal static int VerifyGuards(Observation baseline)
    {
        var first = baseline.Glyphs[0];
        Observation Changed(Glyph glyph) => baseline with { Glyphs = new[] { glyph }.Concat(baseline.Glyphs.Skip(1)).ToArray() };
        Observation[] rejected = [
            baseline with { Text = [baseline.Text[0] + "EXTRA"] },
            baseline with { Glyphs = baseline.Glyphs.Skip(1).ToArray() },
            Changed(first with { Unicode = first.Unicode + 1 }),
            Changed(first with { X = first.X + 0.072 }),
            Changed(first with { Right = first.Right + 0.072 }),
            Changed(first with { Y = first.Y + 0.001 }),
            Changed(first with { Size = 13 }),
            Changed(first with { FontSha256 = "changed" }),
            Changed(first with { X = double.NaN })];
        if (!Compare(baseline, baseline).Passed || rejected.Any(item => Compare(baseline, item).Passed))
            throw new InvalidDataException("Move spacing acceptance guard failed.");
        return rejected.Length + 1;
    }

    [DllImport("pdfium-evaluation")] private static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern int FPDFPage_GetAnnotCount(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
    [DllImport("pdfium-evaluation")] private static extern int FPDFPageObj_GetType(IntPtr item);
    [DllImport("pdfium-evaluation")] private static extern int FPDFTextObj_GetTextRenderMode(IntPtr item);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_CountChars(IntPtr text);
    [DllImport("pdfium-evaluation")] private static extern uint FPDFText_GetUnicode(IntPtr text, int index);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFText_GetTextObject(IntPtr text, int index);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFTextObj_GetFont(IntPtr item);
    [DllImport("pdfium-evaluation")] private static extern int FPDFFont_GetIsEmbedded(IntPtr font);
    [DllImport("pdfium-evaluation")] private static extern int FPDFFont_GetFontData(IntPtr font, [Out] byte[]? bytes, nuint length, out nuint written);
    [DllImport("pdfium-evaluation")] private static extern double FPDFText_GetFontSize(IntPtr text, int index);
    [DllImport("pdfium-evaluation")] private static extern float FPDFText_GetCharAngle(IntPtr text, int index);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetFillColor(IntPtr text, int index, out uint r, out uint g, out uint b, out uint a);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetCharOrigin(IntPtr text, int index, out double x, out double y);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetCharBox(IntPtr text, int index, out double left, out double right, out double bottom, out double top);
}
