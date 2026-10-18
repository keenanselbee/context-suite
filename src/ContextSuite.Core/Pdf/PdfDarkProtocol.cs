using System.Buffers.Binary;
using System.Text.Json;

namespace ContextSuite.Core.Pdf;

public sealed record PdfDarkFrame(PdfDarkFacts Facts, long SourceBytes, ReadOnlyMemory<byte> Pdf);

// Native output is a bounded CPD1 frame, never a filename or an engine command.
public static class PdfDarkProtocol
{
    public const int MaximumInspectionBytes = 16 + 4096;
    public const int MaximumConversionBytes = MaximumInspectionBytes + PdfDarkWork.MaximumOutputBytes;

    public static PdfDarkFrame Read(ReadOnlyMemory<byte> bytes, bool conversion)
    {
        var header = bytes.Span;
        if (bytes.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x31445043 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 1)
            throw new InvalidDataException("Invalid PDF dark-mode frame header.");
        var jsonBytes = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var pdfBytes = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (jsonBytes is < 2 or > 4096 || pdfBytes > PdfDarkWork.MaximumOutputBytes ||
            !conversion && pdfBytes != 0 || bytes.Length != 16L + jsonBytes + pdfBytes)
            throw new InvalidDataException("Invalid PDF dark-mode frame length.");
        using var json = JsonDocument.Parse(bytes.Slice(16, (int)jsonBytes), new JsonDocumentOptions { MaxDepth = 2 });
        var root = json.RootElement;
        string[] names = ["policy", "pageCount", "changedPages", "alreadyDarkPages", "recoloredImages", "preservedImages", "sourceBytes"];
        string[] optionalNames = ["recoveredImages", "recoveredCoordinates"];
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count() != names.Length + optionalNames.Count(name => root.TryGetProperty(name, out _)) ||
            root.EnumerateObject().Select(property => property.Name).Distinct().Count() != root.EnumerateObject().Count() ||
            root.EnumerateObject().Any(property => !names.Contains(property.Name) && !optionalNames.Contains(property.Name)) ||
            names.Any(name => !root.TryGetProperty(name, out _)))
            throw new InvalidDataException("Invalid PDF dark-mode inspection fields.");
        int Number(string name) => root.GetProperty(name).ValueKind == JsonValueKind.Number &&
            root.GetProperty(name).TryGetInt32(out var value) ? value :
            throw new InvalidDataException("Invalid PDF dark-mode count.");
        var policy = root.GetProperty("policy");
        if (policy.ValueKind != JsonValueKind.String ||
            root.GetProperty("sourceBytes").ValueKind != JsonValueKind.Number ||
            !root.GetProperty("sourceBytes").TryGetInt64(out var sourceBytes) || sourceBytes is <= 0 or > PdfDarkSource.MaximumSourceBytes)
            throw new InvalidDataException("Invalid PDF dark-mode source length or policy.");
        var facts = new PdfDarkFacts(Number("pageCount"), Number("changedPages"), Number("alreadyDarkPages"),
            Number("recoloredImages"), Number("preservedImages"), policy.GetString()!,
            root.TryGetProperty("recoveredImages", out _) ? Number("recoveredImages") : 0,
            root.TryGetProperty("recoveredCoordinates", out _) ? Number("recoveredCoordinates") : 0);
        facts.Validate();
        if (conversion && (facts.ChangedPages > 0) != (pdfBytes > 0))
            throw new InvalidDataException("PDF dark-mode output contradicts its inspection.");
        var pdf = bytes[(16 + (int)jsonBytes)..];
        if (!pdf.IsEmpty && (pdf.Length < 8 || !pdf.Span[..5].SequenceEqual("%PDF-"u8)))
            throw new InvalidDataException("PDF dark-mode output is not a PDF.");
        return new(facts, sourceBytes, pdf);
    }
}
