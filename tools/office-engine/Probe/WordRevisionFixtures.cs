using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// Literal text and revision declarations only; no fields, macros or external links.
internal static class WordRevisionFixtures
{
    private static readonly string[] Cases = ["clean", "shown", "hidden", "unspecified"];

    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var name in Cases)
        {
            var revisions = name == "clean" ? "<w:r><w:t>INSERTED_MARKER</w:t></w:r>" : """
                <w:del w:id="1" w:author="Fixture Author" w:date="2026-09-11T00:00:00Z"><w:r><w:delText>DELETED_MARKER</w:delText></w:r></w:del>
                <w:ins w:id="2" w:author="Fixture Author" w:date="2026-09-11T00:00:00Z"><w:r><w:t>INSERTED_MARKER</w:t></w:r></w:ins>
                """;
            var view = name is "shown" or "hidden" ? $"<w:revisionView w:markup=\"{(name == "shown" ? "true" : "false")}\" w:insDel=\"{(name == "shown" ? "true" : "false")}\"/>" : "";
            var parts = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/></Types>
                    """,
                ["_rels/.rels"] = $"<Relationships xmlns=\"{rel}\"><Relationship Id=\"main\" Type=\"{office}/officeDocument\" Target=\"word/document.xml\"/></Relationships>",
                ["word/_rels/document.xml.rels"] = $"<Relationships xmlns=\"{rel}\"><Relationship Id=\"settings\" Type=\"{office}/settings\" Target=\"settings.xml\"/></Relationships>",
                ["word/settings.xml"] = $"<w:settings xmlns:w=\"{w}\">{view}</w:settings>",
                ["word/document.xml"] = $"""
                    <w:document xmlns:w="{w}"><w:body>
                    <w:p><w:r><w:t>CONTROL_MARKER</w:t></w:r></w:p>
                    <w:p>{revisions}</w:p>
                    <w:p><w:r><w:t>END_MARKER</w:t></w:r></w:p>
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                    </w:body></w:document>
                    """
            };
            using var file = new FileStream(Path.Combine(directory, "Word revisions " + name + ".docx"), FileMode.CreateNew);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var part in parts)
            {
                using var output = new StreamWriter(archive.CreateEntry(part.Key).Open(), new UTF8Encoding(false));
                output.Write(part.Value);
            }
        }
        return Cases.Select(name => ("Word revisions " + name + ".docx", "writer_pdf_Export", 1)).ToArray();
    }

    public static RevisionObservation Observe(string name, string[] text)
    {
        var key = Path.GetFileNameWithoutExtension(name)["Word revisions ".Length..];
        if (!Cases.Contains(key)) throw new ArgumentException("Unknown authored revision fixture.");
        var pages = text.Select(page => Regex.Replace(page, @"\s+", " ").Trim()).ToArray();
        var combined = string.Join(" ", pages);
        if (!combined.Contains("CONTROL_MARKER", StringComparison.Ordinal) || !combined.Contains("END_MARKER", StringComparison.Ordinal))
            throw new InvalidDataException("Revision fixture control text is missing.");
        return new(key, pages, combined.Contains("INSERTED_MARKER", StringComparison.Ordinal), combined.Contains("DELETED_MARKER", StringComparison.Ordinal));
    }

    internal sealed record RevisionObservation(string Case, string[] ObservedPages, bool InsertedTextPresent, bool DeletedTextPresent);
}
