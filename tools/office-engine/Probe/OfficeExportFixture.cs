using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

// Passive authored pages with distinct uncompressed BMPs, so PDF export lasts
// long enough to observe real output writing. No fields, scripts or references.
internal static partial class OfficeExportFixture
{
    internal const int Pages = 96;
    internal static string Create(string directory, string family, int pages = Pages) => family switch
    {
        "Word" => Create(directory, pages),
        "Excel" or "PowerPoint" => CreateVisual(directory, family, pages),
        _ => throw new ArgumentException("Choose Word, Excel or PowerPoint.", nameof(family))
    };

    internal static string Create(string directory, int pages = Pages)
    {
        if (pages is < 1 or > Pages) throw new ArgumentOutOfRangeException(nameof(pages));
        var path = Path.Combine(directory, "Export interruption.docx");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        Text("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="bmp" ContentType="image/bmp"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
            """);
        Text("_rels/.rels", $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"document\" Type=\"{rel}/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        var links = new StringBuilder($"<Relationships xmlns=\"{relationships}\">");
        var body = new StringBuilder($"<w:document xmlns:w=\"{w}\" xmlns:r=\"{rel}\"><w:body>");
        for (var page = 1; page <= pages; page++)
        {
            var id = "image" + page;
            links.Append($"<Relationship Id=\"{id}\" Type=\"{rel}/image\" Target=\"media/{id}.bmp\"/>");
            body.Append($"<w:p><w:pPr>{(page > 1 ? "<w:pageBreakBefore/>" : "")}</w:pPr><w:r><w:t>Export interruption page {page}</w:t></w:r></w:p>");
            body.Append($"""
                <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="4572000" cy="4572000"/><wp:docPr id="{page}" name="Authored noise {page}"/><a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="{page}" name="Authored noise {page}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{id}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="4572000" cy="4572000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
                """);
            using var image = zip.CreateEntry("word/media/" + id + ".bmp", CompressionLevel.NoCompression).Open(); image.Write(Bitmap(page));
        }
        body.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\"/></w:sectPr></w:body></w:document>");
        links.Append("</Relationships>"); Text("word/document.xml", body.ToString()); Text("word/_rels/document.xml.rels", links.ToString());
        return path;
        void Text(string name, string value)
        { using var entry = zip.CreateEntry(name).Open(); using var writer = new StreamWriter(entry, new UTF8Encoding(false)); writer.Write(value); }
    }

    private static byte[] Bitmap(int seed)
    {
        const int size = 384; var bytes = new byte[54 + size * size * 3];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        Number(2, bytes.Length); Number(10, 54); Number(14, 40); Number(18, size); Number(22, -size);
        bytes[26] = 1; bytes[28] = 24; Number(34, bytes.Length - 54);
        uint state = (uint)seed;
        for (var index = 54; index < bytes.Length; index++) { state = unchecked(state * 1664525 + 1013904223); bytes[index] = (byte)(state >> 24); }
        return bytes;
        void Number(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
