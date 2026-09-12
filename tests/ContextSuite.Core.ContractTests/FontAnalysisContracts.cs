using System.Buffers.Binary;
using ContextSuite.Core.Analysis;
using ContextSuite.Application.Infrastructure;
using System.Security.Cryptography;

internal static class FontAnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        foreach (var id in new[] { "truetype", "opentype", "font-collection", "woff", "woff2" })
        {
            var bytes = Fixture(id);
            var report = HeaderAnalyzer.Analyze("misleading.pdf", bytes, bytes.Length);
            check(report.Identity is { Confidence: IdentificationConfidence.Likely, Basis: IdentificationBasis.Content } &&
                report.Identity.FormatId == id && report.Warnings.Any(warning => warning.Contains("different type")),
                "font analysis: header evidence overrides misleading filename for " + id);
            check(report.Facts.Count(fact => fact.Group == "Font" && fact.Availability == FactAvailability.Unavailable) == 3 &&
                report.Facts.Any(fact => fact.Id == (id == "font-collection" ? "font.count" : "font.tables") && fact.Integer == 1),
                "font analysis: declared count and explicitly unavailable name/glyph/rights for " + id);
            for (var length = 0; length < bytes.Length; length++)
            {
                var shortened = HeaderAnalyzer.Analyze("unknown", bytes.AsSpan(0, length), length);
                check(shortened.InspectedBytes == length && shortened.Identity.Confidence != IdentificationConfidence.Confirmed,
                    "font analysis: every truncated header remains bounded and unconfirmed for " + id);
            }
            var unknown = HeaderAnalyzer.Analyze("unknown", bytes, long.MaxValue);
            check(unknown.InspectedBytes == bytes.Length, "font analysis: declared huge file size causes no additional reads for " + id);
            var path = Path.Combine(scratch, id + ".pdf");
            await File.WriteAllBytesAsync(path, bytes);
            var modified = File.GetLastWriteTimeUtc(path);
            var fileReport = await FileAnalysisReader.ReadAsync(path, default);
            check(fileReport.Identity.FormatId == id && fileReport.InspectedBytes == bytes.Length &&
                File.GetLastWriteTimeUtc(path) == modified &&
                SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(bytes)),
                "font analysis: actual reader preserves original under misleading filename for " + id);
        }
        var numeric = new byte[12]; U32(numeric, 0, 0x00010000);
        check(HeaderAnalyzer.Analyze("unknown", numeric, 12).Identity.Basis != IdentificationBasis.Content,
            "font analysis: weak numeric marker with no table directory is not identified as TrueType");
        U16(numeric, 4, 65535);
        check(HeaderAnalyzer.Analyze("unknown", numeric, 12).Identity.FormatId != "truetype",
            "font analysis: impossible TrueType directory does not promote a numeric marker");
        var largeDirectory = HeaderAnalyzer.Analyze("unknown", numeric, 2_000_000);
        check(largeDirectory.Identity.FormatId == "truetype" && largeDirectory.Warnings.Any(w => w.Contains("prefix")),
            "font analysis: plausible large directory remains a qualified prefix result");
        var collection = Fixture("font-collection"); U32(collection, 8, uint.MaxValue);
        var hugeCollection = HeaderAnalyzer.Analyze("unknown", collection, collection.Length);
        check(hugeCollection.Facts.Single(f => f.Id == "font.count").Integer == uint.MaxValue &&
            hugeCollection.Warnings.Any(w => w.Contains("out-of-file")), "font analysis: oversized collection count is raw evidence without allocation");
        U32(collection, 4, 0x00030000);
        var version = HeaderAnalyzer.Analyze("unknown", collection, collection.Length);
        check(version.Facts.All(f => f.Id != "font.count") && version.Warnings.Any(w => w.Contains("unsupported")),
            "font analysis: future collection version does not interpret remaining fields");
        collection = Fixture("font-collection"); U32(collection, 4, 0x00020000);
        check(HeaderAnalyzer.Analyze("unknown", collection, collection.Length).Warnings.Any(w => w.Contains("out-of-file")),
            "font analysis: collection version two accounts for additional signature header fields");
        foreach (var id in new[] { "woff", "woff2" })
        {
            var bytes = Fixture(id); U32(bytes, 4, 0x12345678); U16(bytes, 14, 1); U32(bytes, 8, uint.MaxValue);
            var report = HeaderAnalyzer.Analyze("unknown", bytes, bytes.Length);
            check(report.Facts.Single(f => f.Id == "font.flavor").Text == "Uninterpreted 0x12345678" &&
                report.Warnings.Any(w => w.Contains("reserved")) && report.Warnings.Any(w => w.Contains("file size")),
                "font analysis: unknown flavors and contradictory packaged declarations preserved for " + id);
        }
        var woff2 = Fixture("woff2"); U32(woff2, 4, 0x74746366); U32(woff2, 20, uint.MaxValue); U32(woff2, 16, uint.MaxValue);
        var packedCollection = HeaderAnalyzer.Analyze("unknown", woff2, woff2.Length);
        check(packedCollection.Facts.Single(f => f.Id == "font.flavor").Text!.StartsWith("Font collection") &&
            packedCollection.Facts.Single(f => f.Id == "font.sfnt-bytes").Integer == uint.MaxValue &&
            packedCollection.Warnings.Any(w => w.Contains("cannot fit")),
            "font analysis: WOFF2 collection flavor and size references never trigger decompression");
        var random = new Random(9361);
        foreach (var id in new[] { "truetype", "opentype", "font-collection", "woff", "woff2" })
        {
            for (var index = 0; index < 100; index++)
            {
                var bytes = Fixture(id);
                bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                var report = HeaderAnalyzer.Analyze("unknown", bytes, bytes.Length);
                if (report.Facts.Select(f => f.Id).Distinct().Count() != report.Facts.Length)
                    throw new InvalidDataException("Mutated font produced duplicate facts.");
            }
        }
        check(true, "font analysis: 500 deterministic header mutations complete without exceptions or duplicate facts");
    }

    private static byte[] Fixture(string id)
    {
        var bytes = new byte[id == "woff" ? 64 : id == "woff2" ? 52 : id == "font-collection" ? 16 : 28];
        if (id == "truetype") U32(bytes, 0, 0x00010000);
        else System.Text.Encoding.ASCII.GetBytes(id switch { "opentype" => "OTTO", "font-collection" => "ttcf", "woff" => "wOFF", _ => "wOF2" }).CopyTo(bytes, 0);
        if (id == "font-collection") { U32(bytes, 4, 0x00010000); U32(bytes, 8, 1); U32(bytes, 12, 16); }
        else if (id is "woff" or "woff2")
        { U32(bytes, 4, 0x00010000); U32(bytes, 8, (uint)bytes.Length); U16(bytes, 12, 1); U32(bytes, 16, 28); }
        else { U16(bytes, 4, 1); U16(bytes, 6, 16); }
        return bytes;
    }

    private static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
