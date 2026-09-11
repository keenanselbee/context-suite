using System.Buffers.Binary;
using ContextSuite.Core.Pdf;

internal static class PdfRasterContracts
{
    public static void Run(Action<bool, string> check)
    {
        var inspection = new byte[64];
        Header(inspection, 1, 1);
        Put(inspection, 32, 0); Put(inspection, 36, 1); Put(inspection, 40, 3); Put(inspection, 44, 5);
        BinaryPrimitives.WriteDoubleLittleEndian(inspection.AsSpan(48), 1);
        BinaryPrimitives.WriteDoubleLittleEndian(inspection.AsSpan(56), 2);
        var page = PdfRasterProtocol.ReadInspection(inspection).Pages.Single();
        check(page.Index == 0 && page.Rotation == 1 && page.Width == 3 && page.Height == 5, "PDF raster: geometry uses fixed DPI and preserves declared rotation");
        var bitmap = new byte[32 + 3 * 5 * 4]; Header(bitmap, 2, 0);
        Put(bitmap, 16, 3); Put(bitmap, 20, 5); Put(bitmap, 24, 12);
        bitmap[32] = 200; bitmap[35] = 128;
        check(PdfRasterProtocol.ReadPage(bitmap, page).AsSpan().SequenceEqual(bitmap.AsSpan(32)), "PDF raster: straight BGRA bytes retain alpha without reinterpretation");
        foreach (var offset in new[] { 0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44 })
        {
            var forged = inspection.ToArray(); Put(forged, offset, uint.MaxValue);
            Reject(() => PdfRasterProtocol.ReadInspection(forged), "invalid inventory integer at " + offset);
        }
        foreach (var number in new[] { double.NaN, double.PositiveInfinity, -1, 0, 14401 })
        {
            var forged = inspection.ToArray(); BinaryPrimitives.WriteDoubleLittleEndian(forged.AsSpan(48), number);
            Reject(() => PdfRasterProtocol.ReadInspection(forged), "nonfinite/out-of-policy physical size");
        }
        Reject(() => PdfRasterProtocol.ReadInspection(inspection[..63]), "truncated inventory");
        Reject(() => PdfRasterProtocol.ReadInspection([.. inspection, 0]), "trailing inventory bytes");
        Reject(() => PdfRasterProtocol.ReadPage(bitmap[..^1], page), "truncated page");
        Reject(() => PdfRasterProtocol.ReadPage(bitmap, page with { Index = 1 }), "wrong page index");
        var wrongStride = bitmap.ToArray(); Put(wrongStride, 24, 13);
        Reject(() => PdfRasterProtocol.ReadPage(wrongStride, page), "unreviewed row padding");
        Reject(() => new PdfRasterDocument([page with { Index = 1 }]).Validate(), "missing first page");
        Reject(() => new PdfRasterDocument([page, page]).Validate(), "duplicate page");
        Reject(() => new PdfRasterPage(0, 0, 10000, 10000, 4800, 4800).Validate(), "page exceeds pixel budget");
        Reject(() => new PdfRasterPage(0, 0, 20000, 1, 9600, 0.48).Validate(), "page exceeds encoder dimension budget");

        void Reject(Action action, string message)
        {
            try { action(); check(false, "PDF raster: " + message); }
            catch (InvalidDataException) { check(true, "PDF raster: " + message); }
        }
    }

    private static void Header(byte[] bytes, uint kind, uint countOrIndex)
    {
        Put(bytes, 0, 0x31525043); Put(bytes, 4, 1); Put(bytes, 8, kind); Put(bytes, 12, countOrIndex); Put(bytes, 28, (uint)bytes.Length - 32);
    }
    private static void Put(byte[] bytes, int offset, uint value) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value); }
}
