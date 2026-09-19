using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml.Linq;

// Passive authored D09 deck. All positions below are PDF points converted to EMU.
internal static class PowerPointLayoutFixtures
{
    internal const string Name = "PowerPoint layout.pptx";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static string[] Create(string directory)
    {
        File.Copy(Path.Combine(directory, "PowerPoint \u00fc.pptx"), Path.Combine(directory, Name), false);
        using var zip = ZipFile.Open(Path.Combine(directory, Name), ZipArchiveMode.Update);
        for (var index = 1; index <= 3; index++)
        {
            var slide = Read($"ppt/slides/slide{index}.xml");
            slide.Root!.SetAttributeValue("show", "1");
            var tree = slide.Descendants(P + "spTree").Single();
            tree.Elements(P + "sp").Remove();
            tree.Add(Shape(2, new[] { "IMAGE_LAYOUT", "DIAGRAM_TABLE", "MIXED_TYPOGRAPHY" }[index - 1], 36, 24, 648, 40, 24, bold: true));
            if (index == 1)
            {
                tree.Add(XElement.Parse($"""
                    <p:pic xmlns:p="{P}" xmlns:a="{A}" xmlns:r="{R}"><p:nvPicPr><p:cNvPr id="3" name="Four color quadrants"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>{Transform(72, 108, 216, 144)}<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
                    """));
                tree.Add(Shape(4, "IMAGE_CAPTION", 72, 276, 400, 32, 18));
                var relationships = Read("ppt/slides/_rels/slide1.xml.rels");
                var ns = relationships.Root!.Name.Namespace;
                relationships.Root.Add(new XElement(ns + "Relationship", new XAttribute("Id", "image"),
                    new XAttribute("Type", R + "/image"), new XAttribute("Target", "../media/quadrants.bmp")));
                Write("ppt/slides/_rels/slide1.xml.rels", relationships);
            }
            else if (index == 2)
            {
                tree.Add(Shape(3, "START", 72, 90, 108, 36, 18, fill: "DDEEFF"),
                    Shape(4, "END", 324, 90, 108, 36, 18, fill: "DDFFDD"));
                tree.Add(XElement.Parse($"""
                    <p:cxnSp xmlns:p="{P}" xmlns:a="{A}"><p:nvCxnSpPr><p:cNvPr id="5" name="Direction"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr><p:spPr>{Transform(180, 108, 144, 0)}<a:prstGeom prst="line"><a:avLst/></a:prstGeom><a:ln w="25400"><a:solidFill><a:srgbClr val="000000"/></a:solidFill><a:tailEnd type="triangle"/></a:ln></p:spPr></p:cxnSp>
                    """));
                var rows = new[] { new[] { "ITEM", "VALUE" }, new[] { "ALPHA", "10" }, new[] { "BETA", "20" } };
                var table = new XElement(A + "tbl", new XElement(A + "tblPr", new XAttribute("firstRow", "1")),
                    new XElement(A + "tblGrid", new XElement(A + "gridCol", new XAttribute("w", 216 * 12700)),
                        new XElement(A + "gridCol", new XAttribute("w", 216 * 12700))));
                foreach (var row in rows)
                    table.Add(new XElement(A + "tr", new XAttribute("h", 36 * 12700), row.Select(text =>
                        new XElement(A + "tc", TextBody(A, text, 18, "Arial", false, false),
                            new XElement(A + "tcPr", new XAttribute("marL", 6 * 12700), new XAttribute("marT", 3 * 12700),
                                new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", "EEEEEE"))))))));
                tree.Add(new XElement(P + "graphicFrame",
                    new XElement(P + "nvGraphicFramePr", new XElement(P + "cNvPr", new XAttribute("id", "6"), new XAttribute("name", "Three row table")),
                        new XElement(P + "cNvGraphicFramePr"), new XElement(P + "nvPr")),
                    new XElement(P + "xfrm", new XElement(A + "off", new XAttribute("x", 72 * 12700), new XAttribute("y", 180 * 12700)),
                        new XElement(A + "ext", new XAttribute("cx", 432 * 12700), new XAttribute("cy", 108 * 12700))),
                    new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/table"), table))));
            }
            else
            {
                tree.Add(Shape(3, "ARIAL_REGULAR", 72, 90, 576, 40, 20),
                    Shape(4, "ARIAL_BOLD", 72, 144, 576, 40, 24, bold: true),
                    Shape(5, "TIMES_ITALIC", 72, 198, 576, 40, 22, "Times New Roman", italic: true),
                    Shape(6, "COURIER_FIXED", 72, 252, 576, 40, 18, "Courier New"));
            }
            Write($"ppt/slides/slide{index}.xml", slide);
        }
        var types = Read("[Content_Types].xml");
        types.Root!.Add(new XElement(types.Root.Name.Namespace + "Default", new XAttribute("Extension", "bmp"), new XAttribute("ContentType", "image/bmp")));
        Write("[Content_Types].xml", types);
        // Small uncompressed 64x64 BMP: top-left red, top-right green,
        // bottom-left blue, bottom-right yellow. Rows stored bottom-up.
        var bitmap = new byte[54 + 64 * 64 * 3];
        bitmap[0] = (byte)'B'; bitmap[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2), bitmap.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), 64);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28), 24);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var offset = 54 + ((63 - y) * 64 + x) * 3;
            bitmap[offset] = (byte)(y >= 32 && x < 32 ? 255 : 0);
            bitmap[offset + 1] = (byte)(x >= 32 ? 255 : 0);
            bitmap[offset + 2] = (byte)(y < 32 && x < 32 || y >= 32 && x >= 32 ? 255 : 0);
        }
        using (var stream = zip.CreateEntry("ppt/media/quadrants.bmp").Open()) stream.Write(bitmap);
        return [Name];

        XDocument Read(string part) { using var stream = zip.GetEntry(part)!.Open(); return XDocument.Load(stream); }
        void Write(string part, XDocument document)
        { zip.GetEntry(part)?.Delete(); using var stream = zip.CreateEntry(part).Open(); document.Save(stream); }
    }

    private static XElement Transform(int x, int y, int width, int height) => new(A + "xfrm",
        new XElement(A + "off", new XAttribute("x", x * 12700), new XAttribute("y", y * 12700)),
        new XElement(A + "ext", new XAttribute("cx", width * 12700), new XAttribute("cy", height * 12700)));

    private static XElement Shape(int id, string text, int x, int y, int width, int height, int size,
        string family = "Arial", bool bold = false, bool italic = false, string? fill = null) =>
        new(P + "sp", new XElement(P + "nvSpPr", new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", text)),
            new XElement(P + "cNvSpPr", new XAttribute("txBox", "1")), new XElement(P + "nvPr")),
            new XElement(P + "spPr", Transform(x, y, width, height),
                new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")),
                fill is null ? new XElement(A + "noFill") : new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", fill))),
                new XElement(A + "ln", new XElement(A + "noFill"))), TextBody(P, text, size, family, bold, italic));

    private static XElement TextBody(XNamespace ns, string text, int size, string family, bool bold, bool italic) =>
        new(ns + "txBody", new XElement(A + "bodyPr", new XAttribute("lIns", 0), new XAttribute("rIns", 0),
            new XAttribute("tIns", 0), new XAttribute("bIns", 0), new XElement(A + "noAutofit")), new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "r", new XElement(A + "rPr", new XAttribute("lang", "en-US"),
                new XAttribute("sz", size * 100), new XAttribute("b", bold ? "1" : "0"), new XAttribute("i", italic ? "1" : "0"),
                new XElement(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", "000000"))),
                new XElement(A + "latin", new XAttribute("typeface", family))), new XElement(A + "t", text))));
}
