using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

// Only modifies copies of OfficeFixtures' passive generated documents.
internal static class OfficeFontFixtures
{
    public const string MissingFont = "ContextSuiteAbsentFont9361";

    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        var result = new List<(string, string, int)>();
        foreach (var (family, extension, filter, pages) in new[] {
            ("Word", "docx", "writer_pdf_Export", 2), ("Excel", "xlsx", "calc_pdf_Export", 1),
            ("PowerPoint", "pptx", "impress_pdf_Export", 2) })
        foreach (var (variant, font) in new[] { ("control", "Arial"), ("missing", MissingFont) })
        {
            var name = family + " font " + variant + "." + extension;
            var path = Path.Combine(directory, name);
            File.Copy(Path.Combine(directory, family + " \u00fc." + extension), path, false);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            foreach (var part in archive.Entries.Select(entry => entry.FullName).ToArray())
            {
                if (!part.EndsWith(".xml", StringComparison.Ordinal)) continue;
                var entry = archive.GetEntry(part)!;
                string text;
                using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                if (!text.Contains("Arial", StringComparison.Ordinal)) continue;
                entry.Delete();
                using var writer = new StreamWriter(archive.CreateEntry(part).Open());
                writer.Write(text.Replace("Arial", font, StringComparison.Ordinal));
            }
            if (family == "Excel")
            {
                // Give the authored workbook an explicit default font rather than
                // relying on the engine's unstated default for the control.
                Write("xl/styles.xml", $"""
                    <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="{font}"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                    """);
                Add("[Content_Types].xml", "Override", "PartName", "/xl/styles.xml", "ContentType",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                var rel = Read("xl/_rels/workbook.xml.rels");
                rel.Root!.Add(new XElement(rel.Root.Name.Namespace + "Relationship", new XAttribute("Id", "fontStyle"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));
                Write("xl/_rels/workbook.xml.rels", rel.ToString());
            }
            result.Add((name, filter, pages));
            XDocument Read(string part) { using var stream = archive.GetEntry(part)!.Open(); return XDocument.Load(stream); }
            void Write(string part, string text)
            { archive.GetEntry(part)?.Delete(); using var writer = new StreamWriter(archive.CreateEntry(part).Open()); writer.Write(text); }
            void Add(string part, string element, string key1, string value1, string key2, string value2)
            {
                var document = Read(part);
                document.Root!.Add(new XElement(document.Root.Name.Namespace + element, new XAttribute(key1, value1), new XAttribute(key2, value2)));
                Write(part, document.ToString());
            }
        }
        return result.ToArray();
    }

    public static string[] Observe(string json)
    {
        using var document = JsonDocument.Parse(json);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(document.RootElement);
        if (names.Count == 0) throw new InvalidDataException("No PDF BaseFont declarations observed.");
        return names.Order(StringComparer.Ordinal).ToArray();
        void Walk(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Name == "/BaseFont" && property.Value.ValueKind == JsonValueKind.String) names.Add(property.Value.GetString()!);
                    else Walk(property.Value);
                }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Walk(item);
        }
    }
}
