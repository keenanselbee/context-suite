using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Bounded native glyph/font observations for authored layout fixtures.
internal static class OfficeLayoutGlyphs
{
    internal sealed record Glyph(char Character, double Left, double Right, double Top, double Bottom,
        double Size, string Font, int Flags, bool Embedded, string ProgramSha256);
    internal static Glyph[] Read(IntPtr pdfPage, IntPtr page)
    {
        var pageHeight = FPDF_GetPageHeightF(pdfPage);
        var count = FPDFText_CountChars(page);
        if (count is < 1 or > 2048) throw new InvalidDataException("Authored document text limit.");
        var fonts = new Dictionary<IntPtr, (bool Embedded, string Hash)>();
        var result = new List<Glyph>();
        for (var index = 0; index < count; index++)
        {
            var value = FPDFText_GetUnicode(page, index);
            if (value > char.MaxValue || value == 0 || char.IsSurrogate((char)value)) throw new InvalidDataException("Unexpected document character.");
            if (char.IsWhiteSpace((char)value) || IsDirectionMark((char)value)) continue;
            var font = FPDFTextObj_GetFont(FPDFText_GetTextObject(page, index));
            var name = new byte[256];
            var length = FPDFText_GetFontInfo(page, index, name, (uint)name.Length, out var flags);
            if (font == IntPtr.Zero || length is < 2 or > 256 || name[length - 1] != 0 ||
                FPDFText_GetCharBox(page, index, out var left, out var right, out var bottom, out var top) == 0)
                throw new InvalidDataException("Missing document glyph/font facts.");
            if (!fonts.TryGetValue(font, out var program))
            {
                var embedded = FPDFFont_GetIsEmbedded(font) == 1;
                if (FPDFFont_GetFontData(font, null, 0, out var bytes) == 0 || bytes is < 1 or > 4194304)
                    throw new InvalidDataException("Document font program limit.");
                var data = new byte[(int)bytes];
                if (FPDFFont_GetFontData(font, data, bytes, out var read) == 0 || read != bytes)
                    throw new InvalidDataException("Document font program read failed.");
                program = (embedded, Convert.ToHexString(SHA256.HashData(data))); fonts.Add(font, program);
            }
            result.Add(new((char)value, left, right, pageHeight - top, pageHeight - bottom,
                FPDFText_GetFontSize(page, index), Encoding.UTF8.GetString(name, 0, (int)length - 1), flags,
                program.Embedded, program.Hash));
        }
        return result.ToArray();
    }

    internal static bool IsDirectionMark(char value) => value is '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069';

    [DllImport("pdfium-evaluation")] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_CountChars(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern uint FPDFText_GetUnicode(IntPtr page, int index);
    [DllImport("pdfium-evaluation")] private static extern uint FPDFText_GetFontInfo(IntPtr page, int index, [Out] byte[] name, uint length, out int flags);
    [DllImport("pdfium-evaluation")] private static extern double FPDFText_GetFontSize(IntPtr page, int index);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetCharBox(IntPtr page, int index, out double left, out double right, out double bottom, out double top);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFText_GetTextObject(IntPtr text, int index);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFTextObj_GetFont(IntPtr item);
    [DllImport("pdfium-evaluation")] private static extern int FPDFFont_GetIsEmbedded(IntPtr font);
    [DllImport("pdfium-evaluation")] private static extern int FPDFFont_GetFontData(IntPtr font, [Out] byte[]? bytes, nuint length, out nuint written);
}
