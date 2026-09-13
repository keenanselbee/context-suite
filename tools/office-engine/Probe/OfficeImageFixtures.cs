using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

// Passive package copies with one authored embedded PNG. No links or active content.
internal static class OfficeImageFixtures
{
    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var image = Png(); File.WriteAllBytes(Path.Combine(directory, "authored-rgba.png"), image);
        var cases = new List<(string, string, int)>();
        foreach (var (family, extension, filter, pages) in new[] {
            ("Word", "docx", "writer_pdf_Export", 2), ("Excel", "xlsx", "calc_pdf_Export", 1),
            ("PowerPoint", "pptx", "impress_pdf_Export", 2) })
        foreach (var variant in new[] { "retained", "reduced" })
        {
            var name = family + " image " + variant + "." + extension;
            File.Copy(Path.Combine(directory, family + " \u00fc." + extension), Path.Combine(directory, name));
            using var zip = ZipFile.Open(Path.Combine(directory, name), ZipArchiveMode.Update);
            var familyRoot = family == "Word" ? "word" : family == "Excel" ? "xl" : "ppt";
            using (var stream = zip.CreateEntry(familyRoot + "/media/authored.png").Open()) stream.Write(image);
            var types = Read("[Content_Types].xml");
            types.Root!.Add(new XElement(types.Root.Name.Namespace + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")));
            if (family == "Word")
            {
                AddRelationship("word/_rels/document.xml.rels", "image", "media/authored.png");
                var document = Read("word/document.xml");
                var picture = $"""
                    <w:p xmlns:w="{w}"><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" distT="0" distB="0" distL="0" distR="0"><wp:extent cx="1828800" cy="1828800"/><wp:docPr id="81" name="Authored RGBA"/><a:graphic xmlns:a="{a}"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="81" name="Authored RGBA"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip xmlns:r="{rel}" r:embed="image"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="1828800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
                    """;
                document.Root!.Element(XName.Get("body", w))!.Element(XName.Get("sectPr", w))!.AddBeforeSelf(XElement.Parse(picture));
                Write("word/document.xml", document);
            }
            else if (family == "Excel")
            {
                const string xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
                var book = Read("xl/workbook.xml");
                book.Descendants(XName.Get("definedName", s)).Single().Value = "'Print area'!$A$1:$D$18";
                Write("xl/workbook.xml", book);
                var sheet = Read("xl/worksheets/sheet1.xml");
                sheet.Root!.Add(new XElement(XName.Get("drawing", s), new XAttribute(XName.Get("id", rel), "imageDrawing")));
                Write("xl/worksheets/sheet1.xml", sheet);
                AddRelationship("xl/worksheets/_rels/sheet1.xml.rels", "imageDrawing", "../drawings/drawing1.xml", "drawing");
                AddRelationship("xl/drawings/_rels/drawing1.xml.rels", "image", "../media/authored.png");
                Write("xl/drawings/drawing1.xml", XDocument.Parse($"""
                    <xdr:wsDr xmlns:xdr="{xdr}" xmlns:a="{a}" xmlns:r="{rel}"><xdr:absoluteAnchor><xdr:pos x="0" y="914400"/><xdr:ext cx="1828800" cy="1828800"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id="81" name="Authored RGBA"/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x="0" y="914400"/><a:ext cx="1828800" cy="1828800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:absoluteAnchor></xdr:wsDr>
                    """));
                types.Root.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            }
            else
            {
                AddRelationship("ppt/slides/_rels/slide1.xml.rels", "image", "../media/authored.png");
                var slide = Read("ppt/slides/slide1.xml");
                slide.Descendants(XName.Get("spTree", p)).Single().Add(XElement.Parse($"""
                    <p:pic xmlns:p="{p}" xmlns:a="{a}" xmlns:r="{rel}"><p:nvPicPr><p:cNvPr id="81" name="Authored RGBA"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x="914400" y="2286000"/><a:ext cx="1828800" cy="1828800"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
                    """));
                Write("ppt/slides/slide1.xml", slide);
            }
            Write("[Content_Types].xml", types); cases.Add((name, filter, pages));
            XDocument Read(string part) { using var stream = zip.GetEntry(part)!.Open(); return XDocument.Load(stream); }
            void Write(string part, XDocument document)
            { zip.GetEntry(part)?.Delete(); using var stream = zip.CreateEntry(part).Open(); document.Save(stream); }
            void AddRelationship(string part, string id, string target, string kind = "image")
            {
                XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
                var document = zip.GetEntry(part) is null ? new XDocument(new XElement(ns + "Relationships")) : Read(part);
                document.Root!.Add(new XElement(ns + "Relationship", new XAttribute("Id", id), new XAttribute("Type", rel + "/" + kind), new XAttribute("Target", target)));
                Write(part, document);
            }
        }
        return cases.ToArray();
    }

    private static byte[] Png()
    {
        const int size = 1024;
        using var png = new MemoryStream(); png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, size); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        {
            var row = new byte[size * 4 + 1];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var top = y < size / 2; var left = x < size / 2; var offset = 1 + x * 4;
                    row[offset] = (byte)(left ? 255 : 0); row[offset + 1] = (byte)(top && !left ? 255 : 0);
                    row[offset + 2] = (byte)(top ? 0 : 255); row[offset + 3] = top ? left ? (byte)255 : (byte)128 : left ? (byte)0 : (byte)64;
                }
                zlib.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return png.ToArray();
        void Chunk(string name, byte[] bytes)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); png.Write(number);
            var type = Encoding.ASCII.GetBytes(name); png.Write(type); png.Write(bytes); uint crc = uint.MaxValue;
            foreach (var value in type.Concat(bytes)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); png.Write(number);
        }
    }
}
