using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

internal static class WordLayoutFixtures
{
    internal const string Name = "Word multilingual anchor.docx";
    internal const string Latin = "COMBINING cafe\u0301 A\u030a";
    internal const string Cjk = "\u4e2d\u6587\u6d4b\u8bd5";
    internal const string Hebrew = "\u05e9\u05dc\u05d5\u05dd";
    internal const string Marker = "ANCHOR_MARKER";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static string[] Create(string directory)
    {
        using var zip = ZipFile.Open(Path.Combine(directory, Name), ZipArchiveMode.Create);
        var first = Paragraph(Latin, "Arial", "en-US");
        first.Add(XElement.Parse($"""
            <w:r xmlns:w="{W}"><w:drawing><wp:anchor xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="1" behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1"><wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="page"><wp:posOffset>4572000</wp:posOffset></wp:positionH><wp:positionV relativeFrom="page"><wp:posOffset>3048000</wp:posOffset></wp:positionV><wp:extent cx="1828800" cy="1219200"/><wp:wrapNone/><wp:docPr id="1" name="Anchored red and blue"/><a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="1" name="Anchored red and blue"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip xmlns:r="{Office}" r:embed="image"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="1219200"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>
            """));
        var body = new XElement(W + "body", first, Paragraph(Cjk, "Microsoft YaHei", "zh-CN"),
            Paragraph(Hebrew, "Arial", "he-IL", rtl: true), Paragraph(Marker, "Arial", "en-US"),
            new XElement(W + "sectPr", new XElement(W + "pgSz", new XAttribute(W + "w", 12240), new XAttribute(W + "h", 15840)),
                new XElement(W + "pgMar", new XAttribute(W + "top", 1440), new XAttribute(W + "right", 1440),
                    new XAttribute(W + "bottom", 1440), new XAttribute(W + "left", 1440),
                    new XAttribute(W + "header", 0), new XAttribute(W + "footer", 0), new XAttribute(W + "gutter", 0))));
        Write("word/document.xml", new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body)).ToString());
        Write("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="bmp" ContentType="image/bmp"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        Write("_rels/.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='main' Type='{Office}/officeDocument' Target='word/document.xml'/></Relationships>");
        Write("word/_rels/document.xml.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='image' Type='{Office}/image' Target='media/anchor.bmp'/></Relationships>");
        var bitmap = new byte[54 + 64 * 32 * 3];
        bitmap[0] = (byte)'B'; bitmap[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2), bitmap.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), 32);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28), 24);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 64; x++) bitmap[54 + (y * 64 + x) * 3 + (x < 32 ? 2 : 0)] = 255;
        using (var stream = zip.CreateEntry("word/media/anchor.bmp").Open()) stream.Write(bitmap);
        return [Name];

        void Write(string part, string text)
        { using var writer = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false)); writer.Write(text); }
    }

    // Logical start keeps alignment explicit for both reading directions; legacy
    // left/right values have direction-dependent compatibility semantics in Word.
    private static XElement Paragraph(string text, string family, string language, bool rtl = false) => new(W + "p",
        new XElement(W + "pPr", rtl ? new XElement(W + "bidi") : null,
            new XElement(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 240),
            new XAttribute(W + "line", 480), new XAttribute(W + "lineRule", "exact")),
            new XElement(W + "jc", new XAttribute(W + "val", "start"))),
        new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", family),
            new XAttribute(W + "hAnsi", family), new XAttribute(W + "eastAsia", family), new XAttribute(W + "cs", family)),
            new XElement(W + "sz", new XAttribute(W + "val", 36)), new XElement(W + "szCs", new XAttribute(W + "val", 36)),
            rtl ? new XElement(W + "rtl") : null, new XElement(W + "lang", new XAttribute(W + "val", language),
                new XAttribute(W + "eastAsia", language), new XAttribute(W + "bidi", language))),
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
}
