using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

// Local generated data only. The saved value differs from a real, readable target.
internal static class OfficeExternalLinkFixtures
{
    private const string Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal static string[] Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "target");
        Directory.CreateDirectory(target);
        Write(Path.Combine(target, "Source.xlsx"), null);
        var uri = new Uri(Path.Combine(target, "Source.xlsx")).AbsoluteUri;
        string[] names = ["External cached withheld.xlsx", "External recalculate withheld.xlsx",
            "External cached granted.xlsx", "External recalculate granted.xlsx", "Direct target.xlsx"];
        foreach (var name in names[..4]) Write(Path.Combine(directory, name), uri);
        File.Copy(Path.Combine(target, "Source.xlsx"), Path.Combine(directory, names[4]));
        return names;
    }

    private static void Write(string path, string? target)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add("[Content_Types].xml", $$"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
            <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            {{(target is null ? "" : "<Override PartName=\"/xl/externalLinks/externalLink1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml\"/>")}}</Types>
            """);
        Add("_rels/.rels", Rel("root", "officeDocument", "xl/workbook.xml"));
        Add("xl/workbook.xml", $$"""
            <workbook xmlns="{{Sheet}}" xmlns:r="{{Relations}}"><sheets><sheet name="Source" sheetId="1" r:id="sheet"/></sheets>
            {{(target is null ? "" : "<externalReferences><externalReference r:id=\"external\"/></externalReferences>")}}
            <definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'Source'!$A$1:$B$2</definedName></definedNames>
            <calcPr fullCalcOnLoad="0"/></workbook>
            """);
        var relationships = XDocument.Parse(Rel("sheet", "worksheet", "worksheets/sheet1.xml"));
        relationships.Root!.Add(XDocument.Parse(Rel("styles", "styles", "styles.xml")).Root!.Elements());
        if (target is not null)
            relationships.Root.Add(XDocument.Parse(Rel("external", "externalLink", "externalLinks/externalLink1.xml")).Root!.Elements());
        Add("xl/_rels/workbook.xml.rels", relationships.ToString());
        Add("xl/styles.xml", """
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts>
            <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders>
            <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
            <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
            """);
        Add("xl/worksheets/sheet1.xml", $$"""
            <worksheet xmlns="{{Sheet}}"><sheetPr><pageSetUpPr fitToPage="1"/></sheetPr><cols><col min="1" max="2" width="32" customWidth="1"/></cols>
            <sheetData><row r="1"><c r="A1" s="0">{{(target is null ? "" : "<f>'[1]Source'!$A$1</f>")}}<v>{{(target is null ? 987654 : 123456)}}</v></c></row>
            <row r="2"><c r="A2" s="0" t="inlineStr"><is><t>{{(target is null ? "DIRECT TARGET CONTROL" : "EXTERNAL SAVED VALUE")}}</t></is></c></row></sheetData>
            <pageMargins left="0.5" right="0.5" top="0.5" bottom="0.5" header="0.2" footer="0.2"/><pageSetup paperSize="1" orientation="portrait" fitToWidth="1" fitToHeight="1"/></worksheet>
            """);
        if (target is not null)
        {
            Add("xl/externalLinks/externalLink1.xml", $$"""
                <externalLink xmlns="{{Sheet}}" xmlns:r="{{Relations}}"><externalBook r:id="target"><sheetNames><sheetName val="Source"/></sheetNames>
                <sheetDataSet><sheetData sheetId="0"><row r="1"><cell r="A1" t="n"><v>123456</v></cell></row></sheetData></sheetDataSet></externalBook></externalLink>
                """);
            Add("xl/externalLinks/_rels/externalLink1.xml.rels", Rel("target", "externalLinkPath", target, true));
        }

        void Add(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
    }

    private static string Rel(string id, string type, string target, bool external = false)
    {
        XNamespace ns = Package;
        var relation = new XElement(ns + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Relations + "/" + type), new XAttribute("Target", target));
        if (external) relation.Add(new XAttribute("TargetMode", "External"));
        return new XElement(ns + "Relationships", relation).ToString();
    }
}
