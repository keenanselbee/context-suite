using System.IO.Compression;
using ContextSuite.Core.Analysis;
using ContextSuite.Application.Infrastructure;

internal static partial class DocumentAnalysisContracts
{
    private static async Task EmbeddedRelationshipContractsAsync(string scratch, Action<bool, string> check)
    {
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string vba = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
        var factIds = new[] { "document.image-relationships", "document.embedded-relationships", "document.vba-relationships" };
        string Rel(string id, string type, string mode = "") => $"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"../missing.bin\" {mode}/>";
        string Rels(string text) => $"<Relationships xmlns=\"{relationships}\">{text}</Relationships>";
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
        foreach (var strict in new[] { false, true })
        {
            var prefix = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
            var declarations = Rel("image1", prefix + "image") + Rel("image2", prefix + "image", "TargetMode=\"Internal\"") +
                Rel("ole", prefix + "oleObject") + Rel("package", prefix + "package") + Rel("vba", vba) +
                Rel("externalImage", prefix + "image", "TargetMode=\"External\"") +
                Rel("externalVba", vba, "TargetMode=\"External\"") + Rel("externalObject", prefix + "oleObject", "TargetMode=\"External\"");
            var bytes = Zip([.. OpenXmlParts(family, strict), ("content/_rels/main.xml.rels", Rels(declarations))]);
            var result = await AnalyzeAsync(bytes, "unrelated.bin");
            check(result.Identity.FormatId == family && result.Facts.Single(fact => fact.Id == factIds[0]).Integer == 2 &&
                result.Facts.Single(fact => fact.Id == factIds[1]).Integer == 2 && result.Facts.Single(fact => fact.Id == factIds[2]).Integer == 1 &&
                result.Facts.Single(fact => fact.Id == "document.external-relationships").Integer == 3,
                $"documents: internal image/object/VBA references are separate from external declarations: {family}/{strict}");
            check(result.Facts.Single(fact => fact.Id == "document.relationship-scope").Text is { } scope &&
                scope.Contains("not unique or verified files") && scope.Contains("Zero does not establish") &&
                result.Facts.Where(fact => fact.Text is not null).All(fact => !fact.Text!.Contains("missing.bin")),
                $"documents: duplicate targets and absent payloads remain declaration counts without leaking paths: {family}/{strict}");
            var file = Path.Combine(scratch, $"embedded-declarations-{family}-{strict}.bin");
            await File.WriteAllBytesAsync(file, bytes); var modified = File.GetLastWriteTimeUtc(file);
            var read = await FileAnalysisReader.ReadAsync(file, default);
            check(read.Identity.FormatId == family && read.Facts.Single(fact => fact.Id == factIds[2]).Integer == 1 &&
                (await File.ReadAllBytesAsync(file)).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(file) == modified,
                $"documents: actual file reader reports declarations and preserves originals: {family}/{strict}");
        }
        const string prefixNormal = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        var parts = OpenXmlParts("docx");
        foreach (var misleading in new[] { "urn:unknown:image", prefixNormal + "Image", prefixNormal + "imageExtra", vba + "Signature" })
        {
            var result = await AnalyzeAsync(Zip([.. parts, ("content/_rels/main.xml.rels", Rels(Rel("unknown", misleading)))]));
            check(factIds.All(id => result.Facts.Single(fact => fact.Id == id).Integer == 0),
                "documents: exact relationship types prevent misleading suffix or case matches: " + misleading);
        }
        var orphan = await AnalyzeAsync(Zip([.. parts, ("unused/_rels/orphan.xml.rels", Rels(Rel("image", prefixNormal + "image") + Rel("vba", vba)))]));
        check(orphan.Facts.Single(fact => fact.Id == factIds[0]).Integer == 1 && orphan.Facts.Single(fact => fact.Id == factIds[2]).Integer == 1,
            "documents: unused relationship declarations are counted without inferring live document content");
        foreach (var bad in new[] { "<Relationships/>", Rels(Rel("same", vba) + Rel("same", vba)),
            "<!DOCTYPE x SYSTEM 'file:///never-opened'><x/>", Rels(new string(' ', 300 * 1024)) })
        {
            var result = await AnalyzeAsync(Zip([.. parts, ("content/_rels/main.xml.rels", Rels(Rel("image", prefixNormal + "image"))), ("later/_rels/bad.xml.rels", bad)]));
            check(result.Identity.FormatId == "docx" && factIds.All(id => result.Facts.Single(fact => fact.Id == id) is { Integer: null, Availability: FactAvailability.Unavailable }),
                "documents: a later relationship failure hides every partial embedded count");
        }
        var large = Zip([.. parts, ("content/_rels/main.xml.rels", Rels(Rel("image", prefixNormal + "image") + Rel("vba", vba))),
            ("content/opaque.bin", new string('x', 8 * 1024 * 1024))], CompressionLevel.NoCompression);
        var largeResult = await AnalyzeAsync(large);
        check(largeResult.Identity.FormatId == "docx" && largeResult.Facts.Single(fact => fact.Id == factIds[2]).Integer == 1 &&
            largeResult.Facts.Single(fact => fact.Id == "package.bytes-read").Integer < 128 * 1024,
            "documents: large opaque payload is not decoded or read in full for relationship counts");
        var recognized = Zip([.. parts, ("content/_rels/main.xml.rels", Rels(Rel("image", prefixNormal + "image")))], CompressionLevel.NoCompression);
        var unknown = Zip([.. parts, ("content/_rels/main.xml.rels", Rels(Rel("image", prefixNormal + "other")))], CompressionLevel.NoCompression);
        var recognizedResult = await AnalyzeAsync(recognized); var unknownResult = await AnalyzeAsync(unknown);
        check(recognized.Length == unknown.Length && recognizedResult.InspectedBytes == unknownResult.InspectedBytes &&
            recognizedResult.Facts.Single(fact => fact.Id == "package.bytes-read").Integer == unknownResult.Facts.Single(fact => fact.Id == "package.bytes-read").Integer,
            "documents: classifying known relationship types adds no reads beyond the existing scan");
        foreach (var family in new[] { "odt", "ods", "odp" })
        {
            var result = await AnalyzeAsync(OpenDocument(family));
            check(result.Identity.FormatId == family && result.Facts.All(fact => !factIds.Contains(fact.Id)),
                "documents: unscanned OpenDocument embedded references are not shown as zero: " + family);
        }
    }
}
