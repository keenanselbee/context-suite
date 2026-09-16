using System.IO.Compression;
using System.Xml.Linq;

internal static partial class OfficeExportFixture
{
    // Clone only our passive authored template, with one distinct bitmap per sheet/slide.
    // Explicit print areas and slide order make the full-export page count independent
    // of the interruption observer. There are no formulas, links or hidden pages here.
    private static string CreateVisual(string directory, string family, int pages, int bitmapSize = 384)
    {
        if (pages is < 1 or > Pages) throw new ArgumentOutOfRangeException(nameof(pages));
        var excel = family == "Excel";
        var extension = excel ? "xlsx" : "pptx";
        var path = Path.Combine(directory, "Export interruption." + extension);
        File.Copy(Path.Combine(directory, family + " \u00fc." + extension), path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace package = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var prefix = excel ? "xl" : "ppt";
        var part = excel ? "xl/workbook.xml" : "ppt/presentation.xml";
        var linksPart = excel ? "xl/_rels/workbook.xml.rels" : "ppt/_rels/presentation.xml.rels";
        var pagesPrefix = excel ? "xl/worksheets/" : "ppt/slides/";
        var template = Read(pagesPrefix + (excel ? "sheet1.xml" : "slide1.xml"));
        var document = Read(part); var links = Read(linksPart); var types = Read("[Content_Types].xml");
        var list = document.Root!.Element(excel ? s + "sheets" : p + "sldIdLst")!; list.RemoveNodes();
        var names = excel ? document.Root.Element(s + "definedNames") : null; names?.RemoveNodes();
        links.Root!.Elements().Where(element => ((string?)element.Attribute("Type")) == rel.NamespaceName + (excel ? "/worksheet" : "/slide")).Remove();
        types.Root!.Elements().Where(element => ((string?)element.Attribute("PartName"))?.StartsWith("/" + pagesPrefix, StringComparison.Ordinal) == true).Remove();
        types.Root.Add(new XElement(types.Root.Name.Namespace + "Default", new XAttribute("Extension", "bmp"), new XAttribute("ContentType", "image/bmp")));
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith(pagesPrefix, StringComparison.Ordinal)).ToArray()) entry.Delete();
        for (var page = 1; page <= pages; page++)
        {
            var pageName = (excel ? "sheet" : "slide") + page + ".xml";
            var imageName = "image" + page + ".bmp";
            var id = "page" + page;
            var pageDocument = new XDocument(template);
            var pageLinks = new XDocument(new XElement(package + "Relationships"));
            if (excel)
            {
                list.Add(new XElement(s + "sheet", new XAttribute("name", "Page " + page), new XAttribute("sheetId", page), new XAttribute(rel + "id", id)));
                names!.Add(new XElement(s + "definedName", new XAttribute("name", "_xlnm.Print_Area"), new XAttribute("localSheetId", page - 1), $"'Page {page}'!$A$1:$D$32"));
                pageDocument.Root!.Element(s + "sheetData")!.ReplaceNodes(new XElement(s + "row", new XAttribute("r", 1),
                    new XElement(s + "c", new XAttribute("r", "A1"), new XAttribute("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", "Export interruption sheet " + page)))));
                pageDocument.Root.Add(new XElement(s + "drawing", new XAttribute(rel + "id", "drawing")));
                pageLinks.Root!.Add(Link("drawing", "drawing", "../drawings/drawing" + page + ".xml"));
                Write($"xl/drawings/drawing{page}.xml", XDocument.Parse($"""
                    <xdr:wsDr xmlns:xdr="{xdr}" xmlns:a="{a}" xmlns:r="{rel}"><xdr:absoluteAnchor><xdr:pos x="0" y="914400"/><xdr:ext cx="4572000" cy="4572000"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="2" name="Authored noise {page}"/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="914400"/><a:ext cx="4572000" cy="4572000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:absoluteAnchor></xdr:wsDr>
                    """));
                Write($"xl/drawings/_rels/drawing{page}.xml.rels", new XDocument(new XElement(package + "Relationships", Link("image", "image", "../media/" + imageName))));
                Type($"xl/drawings/drawing{page}.xml", "drawing");
            }
            else
            {
                list.Add(new XElement(p + "sldId", new XAttribute("id", 255 + page), new XAttribute(rel + "id", id)));
                pageDocument.Root!.SetAttributeValue("show", "1");
                pageDocument.Descendants(a + "t").Single().Value = "Export interruption slide " + page;
                pageDocument.Descendants(p + "spTree").Single().Add(XElement.Parse($"""
                    <p:pic xmlns:p="{p}" xmlns:a="{a}" xmlns:r="{rel}"><p:nvPicPr><p:cNvPr id="3" name="Authored noise {page}"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x="2743200" y="1143000"/><a:ext cx="3657600" cy="3657600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
                    """));
                pageLinks.Root!.Add(Link("layout", "slideLayout", "../slideLayouts/slideLayout1.xml"), Link("image", "image", "../media/" + imageName));
            }
            links.Root.Add(Link(id, excel ? "worksheet" : "slide", (excel ? "worksheets/" : "slides/") + pageName));
            Type(pagesPrefix + pageName, excel ? "spreadsheetml.worksheet" : "presentationml.slide");
            Write(pagesPrefix + pageName, pageDocument); Write(pagesPrefix + "_rels/" + pageName + ".rels", pageLinks);
            using var image = zip.CreateEntry(prefix + "/media/" + imageName, CompressionLevel.NoCompression).Open(); image.Write(Bitmap(page, bitmapSize));
        }
        Write(part, document); Write(linksPart, links); Write("[Content_Types].xml", types);
        return path;

        XDocument Read(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
        void Write(string name, XDocument value)
        { zip.GetEntry(name)?.Delete(); using var stream = zip.CreateEntry(name).Open(); value.Save(stream); }
        XElement Link(string id, string kind, string target) => new(package + "Relationship", new XAttribute("Id", id), new XAttribute("Type", rel.NamespaceName + "/" + kind), new XAttribute("Target", target));
        void Type(string name, string type) => types.Root.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/" + name),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument." + type + "+xml")));
    }
}
