using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

// Authored passive chart controls. Literal chart data has no workbook or external target.
internal static class OfficeChartFixtures
{
    private static readonly XNamespace Chart = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Content = "http://schemas.openxmlformats.org/package/2006/content-types";

    internal static string[] Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var support = Path.Combine(directory, "authored-support");
        Directory.CreateDirectory(support);
        OfficeFixtures.Create(support);
        var excel = ExcelLayoutFixtures.Create(Path.Combine(support, "excel"));
        using var workbook = ZipFile.OpenRead(Path.Combine(support, "excel", excel[0]));
        using var chartStream = workbook.GetEntry("xl/charts/chart1.xml")!.Open();
        var chart = XDocument.Load(chartStream);
        foreach (var kind in new[] { "str", "num" })
        {
            var reference = chart.Descendants(Chart + (kind + "Ref")).Single();
            var cache = reference.Element(Chart + (kind + "Cache"))!;
            reference.ReplaceWith(new XElement(Chart + (kind + "Lit"), cache.Elements()));
        }
        if (chart.Descendants(Chart + "f").Any()) throw new InvalidDataException("Unexpected chart reference.");
        var chartXml = chart.ToString(SaveOptions.DisableFormatting);
        var graphic = $"""
            <a:graphic xmlns:a="{Drawing}"><a:graphicData uri="{Chart}"><c:chart xmlns:c="{Chart}" xmlns:r="{Office}" r:id="chart"/></a:graphicData></a:graphic>
            """;

        using (var word = ZipFile.Open(Path.Combine(directory, "Word chart.docx"), ZipArchiveMode.Create))
        {
            Put(word, "[Content_Types].xml", $"""
                <Types xmlns="{Content}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                <Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/></Types>
                """);
            Put(word, "_rels/.rels", Relationship("root", "officeDocument", "word/document.xml"));
            Put(word, "word/_rels/document.xml.rels", Relationship("chart", "chart", "charts/chart1.xml"));
            Put(word, "word/charts/chart1.xml", chartXml);
            Put(word, "word/document.xml", $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><w:body>
                <w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="28"/></w:rPr><w:t>WORD CHART CONTROL</w:t></w:r></w:p>
                <w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="5486400" cy="3200400"/><wp:docPr id="1" name="Authored values"/>{graphic}</wp:inline></w:drawing></w:r></w:p>
                <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                </w:body></w:document>
                """);
        }

        var deck = Path.Combine(directory, "PowerPoint chart.pptx");
        File.Copy(Path.Combine(support, "PowerPoint ü.pptx"), deck);
        using (var slides = ZipFile.Open(deck, ZipArchiveMode.Update))
        {
            var types = Read(slides, "[Content_Types].xml");
            types.Root!.Elements().Where(e => (string?)e.Attribute("PartName") is "/ppt/slides/slide2.xml" or "/ppt/slides/slide3.xml").Remove();
            types.Root!.Add(new XElement(XName.Get("Override", Content), new XAttribute("PartName", "/ppt/charts/chart1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml")));
            Put(slides, "[Content_Types].xml", types.ToString());
            var presentation = Read(slides, "ppt/presentation.xml");
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            presentation.Descendants(p + "sldId").Skip(1).Remove();
            Put(slides, "ppt/presentation.xml", presentation.ToString());
            var relationships = Read(slides, "ppt/_rels/presentation.xml.rels");
            relationships.Root!.Elements().Where(e => (string?)e.Attribute("Id") is "rId2" or "rId3").Remove();
            Put(slides, "ppt/_rels/presentation.xml.rels", relationships.ToString());
            foreach (var index in new[] { 2, 3 })
            {
                slides.GetEntry($"ppt/slides/slide{index}.xml")!.Delete();
                slides.GetEntry($"ppt/slides/_rels/slide{index}.xml.rels")!.Delete();
            }
            var slide = Read(slides, "ppt/slides/slide1.xml");
            slide.Descendants(Drawing + "t").Single().Value = "POWERPOINT CHART CONTROL";
            slide.Descendants(Drawing + "rPr").Single().SetAttributeValue("sz", "1800");
            var frame = XElement.Parse($"""
                <p:graphicFrame xmlns:p="{p}" xmlns:a="{Drawing}"><p:nvGraphicFramePr><p:cNvPr id="3" name="Authored values"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>
                <p:xfrm><a:off x="914400" y="1371600"/><a:ext cx="5486400" cy="3200400"/></p:xfrm>{graphic}</p:graphicFrame>
                """);
            slide.Descendants(p + "spTree").Single().Add(frame);
            Put(slides, "ppt/slides/slide1.xml", slide.ToString());
            var rels = Read(slides, "ppt/slides/_rels/slide1.xml.rels");
            rels.Root!.Add(new XElement(Relationships + "Relationship", new XAttribute("Id", "chart"),
                new XAttribute("Type", Office + "/chart"), new XAttribute("Target", "../charts/chart1.xml")));
            Put(slides, "ppt/slides/_rels/slide1.xml.rels", rels.ToString());
            Put(slides, "ppt/charts/chart1.xml", chartXml);
        }
        return ["Word chart.docx", "PowerPoint chart.pptx"];
    }

    private static XDocument Read(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private static void Put(ZipArchive zip, string name, string value)
    {
        if (zip.Mode == ZipArchiveMode.Update) zip.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }

    private static string Relationship(string id, string type, string target) =>
        new XElement(Relationships + "Relationships", new XElement(Relationships + "Relationship",
            new XAttribute("Id", id), new XAttribute("Type", Office + "/" + type), new XAttribute("Target", target))).ToString();
}
