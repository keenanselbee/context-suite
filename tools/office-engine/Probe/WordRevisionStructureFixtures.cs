using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// Authored literal-content controls; no fields, scripts, external targets or fonts embedded.
internal static class WordRevisionStructureFixtures
{
    private static readonly string[] Kinds = ["format", "table", "move"];
    private static readonly string[] Variants = ["clean", "tracked", "before"];
    private const string Author = "w:author=\"Fixture Author\" w:date=\"2026-09-14T00:00:00Z\"";
    private const string Font = "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/><w:sz w:val=\"24\"/>";

    internal static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        var cases = new List<(string, string, int)>();
        foreach (var kind in Kinds)
        foreach (var variant in Variants)
        {
            var name = $"Word structures {kind} {variant}.docx";
            var content = kind switch
            {
                "format" => Format(variant),
                "table" => Table(variant),
                _ => Move(variant)
            };
            var parts = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/></Types>
                    """,
                ["_rels/.rels"] = """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="main" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                    """,
                ["word/_rels/document.xml.rels"] = """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="settings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/></Relationships>
                    """,
                ["word/settings.xml"] = """
                    <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:revisionView w:markup="true" w:insDel="true" w:formatting="true"/></w:settings>
                    """,
                ["word/document.xml"] = $"""
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                    <w:p>{Run("CONTROL_MARKER")}</w:p>{content}<w:p>{Run("END_MARKER")}</w:p>
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                    </w:body></w:document>
                    """
            };
            using var file = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var (path, xml) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            cases.Add((name, "writer_pdf_Export", 1));
        }
        return cases.ToArray();
    }

    internal static Observation Observe(string name, string profile, string[] pages)
    {
        var parts = Path.GetFileNameWithoutExtension(name).Split(' ');
        if (parts.Length != 4 || parts[0] != "Word" || parts[1] != "structures" ||
            !Kinds.Contains(parts[2]) || !Variants.Contains(parts[3]) ||
            profile is not ("final-text" or "show-changes-control") ||
            profile == "show-changes-control" && parts[3] != "tracked")
            throw new ArgumentException("Unknown structural revision fixture/profile.");
        var kind = parts[2]; var variant = parts[3];
        var shown = profile == "show-changes-control";
        var body = kind switch
        {
            "format" => "FORMAT_MARKER",
            "table" when variant == "before" => "ROW_KEEP ROW_OLD",
            "table" when shown => "ROW_KEEP ROW_OLD ROW_NEW",
            "table" => "ROW_KEEP ROW_NEW",
            "move" when variant == "before" => "MOVED_MARKER MOVE_GAP",
            "move" when shown => "MOVED_MARKER MOVE_GAP MOVED_MARKER",
            _ => "MOVE_GAP MOVED_MARKER"
        };
        var expected = "CONTROL_MARKER " + body + " END_MARKER";
        var observed = Regex.Replace(string.Join(' ', pages), @"\s+", " ").Trim();
        return new(kind, variant, expected, observed, pages.Length == 1 && observed == expected);
    }

    private static string Format(string variant)
    {
        var properties = variant == "before" ? "<w:i/>" : "<w:b/>";
        if (variant == "tracked")
            properties += $"<w:rPrChange w:id=\"1\" {Author}><w:rPr>{Font}<w:i/></w:rPr></w:rPrChange>";
        return "<w:p>" + Run("FORMAT_MARKER", properties) + "</w:p>";
    }

    private static string Table(string variant)
    {
        var rows = Row("ROW_KEEP");
        if (variant != "clean") rows += Row("ROW_OLD", variant == "tracked" ? "del" : null, 10);
        if (variant != "before") rows += Row("ROW_NEW", variant == "tracked" ? "ins" : null, 20);
        return "<w:tbl><w:tblPr><w:tblW w:w=\"4320\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"4320\"/></w:tblGrid>" + rows + "</w:tbl>";
    }

    private static string Row(string text, string? change = null, int id = 0)
    {
        var properties = change is null ? "" : $"<w:trPr><w:{change} w:id=\"{id}\" {Author}/></w:trPr>";
        var run = Run(text, deleted: change == "del");
        if (change is not null) run = $"<w:{change} w:id=\"{id + 1}\" {Author}>{run}</w:{change}>";
        return $"<w:tr>{properties}<w:tc><w:tcPr><w:tcW w:w=\"4320\" w:type=\"dxa\"/></w:tcPr><w:p>{run}</w:p></w:tc></w:tr>";
    }

    private static string Move(string variant)
    {
        var from = variant == "clean" ? "" : Run("MOVED_MARKER ");
        var to = variant == "before" ? "" : Run("MOVED_MARKER ");
        if (variant == "tracked")
        {
            from = $"<w:moveFromRangeStart w:id=\"30\" w:name=\"FixtureMove\" {Author}/><w:moveFrom w:id=\"31\" {Author}>{from}</w:moveFrom><w:moveFromRangeEnd w:id=\"30\"/>";
            to = $"<w:moveToRangeStart w:id=\"32\" w:name=\"FixtureMove\" {Author}/><w:moveTo w:id=\"33\" {Author}>{to}</w:moveTo><w:moveToRangeEnd w:id=\"32\"/>";
        }
        return "<w:p>" + from + Run("MOVE_GAP ") + to + "</w:p>";
    }

    private static string Run(string text, string properties = "", bool deleted = false)
    {
        var element = deleted ? "delText" : "t";
        return $"<w:r><w:rPr>{Font}{properties}</w:rPr><w:{element} xml:space=\"preserve\">{text}</w:{element}></w:r>";
    }

    internal sealed record Observation(string Kind, string Variant, string ExpectedText, string ObservedText, bool Matches);
}
