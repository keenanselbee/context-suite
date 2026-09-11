using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Pdf;

public sealed record PdfRasterPage(int Index, int Rotation, int Width, int Height, double WidthPoints, double HeightPoints)
{
    public void Validate()
    {
        if (Index is < 0 or >= PdfRasterProtocol.MaximumPages || Rotation is < 0 or > 3 ||
            !double.IsFinite(WidthPoints) || !double.IsFinite(HeightPoints) || WidthPoints is <= 0 or > 14400 || HeightPoints is <= 0 or > 14400 ||
            Width != Math.Ceiling(WidthPoints * PdfRasterProtocol.Dpi / 72) || Height != Math.Ceiling(HeightPoints * PdfRasterProtocol.Dpi / 72) ||
            Width is <= 0 or > 16384 || Height is <= 0 or > 16384 || (long)Width * Height > PdfRasterProtocol.MaximumPagePixels)
            throw new InvalidDataException("PDF page geometry exceeds the fixed rendering policy.");
    }
}

public sealed record PdfRasterSource(Guid ItemId, string Path, string Sha256, long FileBytes, PdfRasterDocument Document)
{
    public void Validate()
    {
        new PdfFileProbe(ItemId, Path).Validate();
        if (Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) || FileBytes is <= 0 or > PdfRasterProtocol.MaximumSourceBytes || Document is null)
            throw new InvalidDataException("Invalid PDF rendering source facts.");
        Document.Validate();
    }
}

public sealed record PdfRasterDocument(ImmutableArray<PdfRasterPage> Pages)
{
    public void Validate()
    {
        if (Pages.IsDefaultOrEmpty || Pages.Length > PdfRasterProtocol.MaximumPages) throw new InvalidDataException("Invalid PDF page inventory.");
        for (var index = 0; index < Pages.Length; index++)
        {
            if (Pages[index] is not { } page || page.Index != index) throw new InvalidDataException("PDF page inventory is incomplete or out of order.");
            page.Validate();
        }
    }
}

// Fixed little-endian renderer ABI. Page pixels are top-down straight BGRA8;
// transparent backing retains alpha, and stored form/annotation appearances render.
public static class PdfRasterProtocol
{
    public const int Dpi = 150;
    public const int MaximumSourceBytes = 128 * 1024 * 1024;
    public const int MaximumPages = 4096;
    public const int MaximumPagePixels = 16_000_000;
    public const int MaximumInspectionBytes = 32 + MaximumPages * 32;
    public const int MaximumRenderedBytes = 32 + MaximumPagePixels * 4;
    public const string Policy = "pdf-pages-png-150-1";

    public static PdfRasterDocument ReadInspection(ReadOnlySpan<byte> bytes)
    {
        Header(bytes, 1);
        var count = Integer(bytes, 12);
        if (count is <= 0 or > MaximumPages || Integer(bytes, 16) != 0 || Integer(bytes, 20) != 0 || Integer(bytes, 24) != 0 ||
            bytes.Length != 32 + count * 32) throw new InvalidDataException("Invalid PDF renderer page inventory.");
        var pages = ImmutableArray.CreateBuilder<PdfRasterPage>(count);
        for (var index = 0; index < count; index++)
        {
            var row = bytes.Slice(32 + index * 32, 32);
            pages.Add(new(Integer(row, 0), Integer(row, 4), Integer(row, 8), Integer(row, 12),
                BinaryPrimitives.ReadDoubleLittleEndian(row[16..]), BinaryPrimitives.ReadDoubleLittleEndian(row[24..])));
        }
        var document = new PdfRasterDocument(pages.MoveToImmutable());
        document.Validate();
        return document;
    }

    public static byte[] ReadPage(ReadOnlySpan<byte> bytes, PdfRasterPage expected)
    {
        expected.Validate();
        Header(bytes, 2);
        if (Integer(bytes, 12) != expected.Index || Integer(bytes, 16) != expected.Width || Integer(bytes, 20) != expected.Height ||
            Integer(bytes, 24) != expected.Width * 4 || bytes.Length != 32 + expected.Width * expected.Height * 4)
            throw new InvalidDataException("Rendered PDF page does not match the inspected geometry.");
        return bytes[32..].ToArray();
    }

    private static void Header(ReadOnlySpan<byte> bytes, int kind)
    {
        if (bytes.Length < 32 || Integer(bytes, 0) != 0x31525043 || Integer(bytes, 4) != 1 || Integer(bytes, 8) != kind ||
            Integer(bytes, 28) != bytes.Length - 32) throw new InvalidDataException("Invalid PDF renderer reply frame.");
    }

    private static int Integer(ReadOnlySpan<byte> bytes, int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        if (value > int.MaxValue) throw new InvalidDataException("PDF renderer integer exceeds its bound.");
        return (int)value;
    }
}
