using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Private.Pdf;

internal static class PdfDocxWriter
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace W10 = "urn:schemas-microsoft-com:office:word";

    public static byte[] Create(IReadOnlyList<DocxPage> pages, CancellationToken token)
    {
        var body = new XElement(W + "body");
        var relationships = new XElement(Rel + "Relationships");
        var media = new List<(string Name, byte[] Bytes)>();
        var footers = new List<XElement>();
        var hasFooters = pages.Any(page => page.Blocks.Any(block => block.Paragraph?.IsFooter == true));
        var ruleId = 0;
        relationships.Add(new XElement(Rel + "Relationship", new XAttribute("Id", "styles"),
            new XAttribute("Type", R.NamespaceName + "/styles"), new XAttribute("Target", "styles.xml")));
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var leftMargin = page.Blocks.Count == 0 ? 36 : Math.Clamp(page.Blocks.Min(block => block.Bounds.Left), 18, 72);
            var topMargin = page.Blocks.Count == 0 ? 36 : Math.Clamp(page.Height - page.Blocks.Max(block => block.Bounds.Top), 18, 72);
            var footer = new XElement(W + "ftr");
            foreach (var block in page.Blocks)
            {
                token.ThrowIfCancellationRequested();
                if (block.Paragraph is { } paragraph)
                {
                    var element = Paragraph(paragraph);
                    var size = paragraph.Runs.Count == 0 ? 10 : paragraph.Runs.Max(run => run.Size);
                    element.Element(W + "pPr")!.Element(W + "pStyle")!.AddAfterSelf(new XElement(W + "framePr",
                        new XAttribute(W + "w", Twips(Math.Min(page.Width - block.Bounds.Left, block.Bounds.Width + 8))),
                        new XAttribute(W + "hAnchor", "page"), new XAttribute(W + "vAnchor", "page"),
                        new XAttribute(W + "x", Twips(block.Bounds.Left)),
                        new XAttribute(W + "y", Twips(Math.Max(0, page.Height - paragraph.Baseline - size * 0.9 -
                            (paragraph.Runs.Any(run => run.Font.StartsWith("MinionPro", StringComparison.OrdinalIgnoreCase)) ? 0.5 : 0)))),
                        new XAttribute(W + "wrap", "notBeside")));
                    if (paragraph.IsFooter) footer.Add(element);
                    else body.Add(element);
                }
                else if (block.Table is { } table) body.Add(Table(table, block.Bounds.Left, page.Height - block.Bounds.Top));
                else if (block.Rule is { } rule) body.Add(Rule(rule, ++ruleId, block.Bounds.Left, page.Height - block.Bounds.Top, block.Bounds.Width));
                else if (block.Picture is { } picture)
                {
                    var id = media.Count + 1;
                    var name = $"image{id}.{picture.Extension}";
                    media.Add((name, picture.Bytes));
                    if (media.Sum(item => (long)item.Bytes.Length) > 48 * 1024 * 1024)
                        throw new ImageFailureException(ImageFailure.ResourceLimit);
                    relationships.Add(new XElement(Rel + "Relationship", new XAttribute("Id", "image" + id),
                        new XAttribute("Type", R.NamespaceName + "/image"), new XAttribute("Target", "media/" + name)));
                    body.Add(Picture(picture, id, block.Bounds.Left, page.Height - block.Bounds.Top));
                }
            }
            var section = new XElement(W + "sectPr", new XElement(W + "type", new XAttribute(W + "val", "nextPage")),
                new XElement(W + "pgSz", new XAttribute(W + "w", Twips(page.Width)), new XAttribute(W + "h", Twips(page.Height))),
                new XElement(W + "pgMar", new XAttribute(W + "top", Twips(topMargin)), new XAttribute(W + "bottom", 360),
                    new XAttribute(W + "left", Twips(leftMargin)), new XAttribute(W + "right", 360), new XAttribute(W + "header", 0),
                    new XAttribute(W + "footer", 0), new XAttribute(W + "gutter", 0)));
            if (hasFooters)
            {
                if (!footer.HasElements) footer.Add(new XElement(W + "p", new XElement(W + "pPr", CompactSpacing())));
                footers.Add(footer);
                var id = "footer" + (index + 1);
                relationships.Add(new XElement(Rel + "Relationship", new XAttribute("Id", id),
                    new XAttribute("Type", R.NamespaceName + "/footer"), new XAttribute("Target", id + ".xml")));
                section.AddFirst(new XElement(W + "footerReference", new XAttribute(W + "type", "default"), new XAttribute(R + "id", id)));
            }
            if (index == pages.Count - 1) body.Add(section);
            else body.Add(new XElement(W + "p", new XElement(W + "pPr", CompactSpacing(), section)));
        }
        var document = new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W),
            new XAttribute(XNamespace.Xmlns + "r", R), body);
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", new XElement(ct + "Types",
                new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                new XElement(ct + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
                footers.Select((_, index) => new XElement(ct + "Override", new XAttribute("PartName", $"/word/footer{index + 1}.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml")))));
            Write(zip, "_rels/.rels", new XElement(Rel + "Relationships", new XElement(Rel + "Relationship",
                new XAttribute("Id", "document"), new XAttribute("Type", R.NamespaceName + "/officeDocument"), new XAttribute("Target", "word/document.xml"))));
            Write(zip, "word/document.xml", document);
            Write(zip, "word/_rels/document.xml.rels", relationships);
            Write(zip, "word/styles.xml", Styles());
            for (var index = 0; index < footers.Count; index++) Write(zip, $"word/footer{index + 1}.xml", footers[index]);
            foreach (var (name, bytes) in media)
            {
                using var stream = zip.CreateEntry("word/media/" + name, CompressionLevel.Fastest).Open();
                stream.Write(bytes);
            }
        }
        if (memory.Length > PdfDocxWork.MaximumOutputBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var result = memory.ToArray();
        Validate(result, string.Concat(document.Descendants(W + "t").Concat(footers.SelectMany(footer => footer.Descendants(W + "t"))).Select(node => node.Value)),
            media.Count, footers.Count, ruleId);
        return result;
    }

    internal static void Validate(byte[] bytes, string expectedText, int expectedImages, int expectedFooters = 0, int expectedRules = 0)
    {
        using var input = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != expectedImages + expectedFooters + 5 || zip.Entries.Sum(entry => entry.Length) > 96 * 1024 * 1024 ||
            zip.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        var part = zip.GetEntry("word/document.xml") ?? throw new ImageFailureException(ImageFailure.ValidationFailed);
        using var reader = System.Xml.XmlReader.Create(part.Open(), new System.Xml.XmlReaderSettings
        { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        var texts = document.Descendants(W + "t").Select(node => node.Value).ToList();
        for (var index = 0; index < expectedFooters; index++)
        {
            var footerPart = zip.GetEntry($"word/footer{index + 1}.xml") ?? throw new ImageFailureException(ImageFailure.ValidationFailed);
            using var footerReader = System.Xml.XmlReader.Create(footerPart.Open(), new System.Xml.XmlReaderSettings
            { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 });
            var footer = XDocument.Load(footerReader);
            if (footer.Root?.Name != W + "ftr") throw new ImageFailureException(ImageFailure.ValidationFailed);
            texts.AddRange(footer.Descendants(W + "t").Select(node => node.Value));
        }
        if (document.Root?.Name != W + "document" || string.Concat(texts) != expectedText ||
            document.Descendants(W + "drawing").Count() != expectedImages || document.Descendants(V + "line").Count() != expectedRules ||
            document.Descendants(W + "footerReference").Count() != expectedFooters || document.Descendants(W + "sectPr").Count() == 0)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        foreach (var entry in zip.Entries)
            if (entry.FullName.Contains("..", StringComparison.Ordinal) || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/'))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
    }

    private static XElement Paragraph(DocxParagraph paragraph, bool tableCell = false) => new(W + "p",
        new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.Style)),
            new XElement(W + "widowControl", new XAttribute(W + "val", 0)),
            new XElement(W + "spacing", new XAttribute(W + "before", Twips(paragraph.TopInset)), new XAttribute(W + "after", 0),
                new XAttribute(W + "line", Twips(paragraph.Leading > 0 ? paragraph.Leading : paragraph.Runs.Select(run => run.Size).DefaultIfEmpty(10).Max() * 1.15)),
                new XAttribute(W + "lineRule", "exact")),
            new XElement(W + "ind", new XAttribute(W + "left", Twips(paragraph.LeftInset))), new XElement(W + "jc", new XAttribute(W + "val", "left"))),
        paragraph.Runs.Select(run => new XElement(W + "r", new XElement(W + "rPr",
            new XElement(W + "rFonts", new XAttribute(W + "ascii", Font(run.Font)), new XAttribute(W + "hAnsi", Font(run.Font))),
            run.Bold ? new XElement(W + "b") : null, run.Italic ? new XElement(W + "i") : null,
            new XElement(W + "color", new XAttribute(W + "val", run.Color)),
            // Minion's embedded subset is not a reusable system font. Natural
            // Times width matches positioned answers; fixed table cells retain
            // their narrower fallback to avoid an extra line and row drift.
            tableCell && run.Font.StartsWith("MinionPro", StringComparison.OrdinalIgnoreCase)
                ? new XElement(W + "w", new XAttribute(W + "val", 97)) : null,
            new XElement(W + "sz", new XAttribute(W + "val", (int)Math.Round(run.Size * 2)))),
            run.Text == "\n" ? new XElement(W + "br") : new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), run.Text))));

    private static string Font(string font) => font.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase) ? "Arial" :
        font.StartsWith("Times", StringComparison.OrdinalIgnoreCase) ? "Times New Roman" :
        font.StartsWith("Courier", StringComparison.OrdinalIgnoreCase) ? "Courier New" :
        font.StartsWith("DejaVuSansMono", StringComparison.OrdinalIgnoreCase) ? "DejaVu Sans Mono" :
        font.StartsWith("DejaVuSansCondensed", StringComparison.OrdinalIgnoreCase) ? "DejaVu Sans Condensed" :
        font.StartsWith("DejaVuSans", StringComparison.OrdinalIgnoreCase) ? "DejaVu Sans" :
        font.StartsWith("DejaVuSerifCondensed", StringComparison.OrdinalIgnoreCase) ? "DejaVu Serif Condensed" :
        font.StartsWith("DejaVuSerif", StringComparison.OrdinalIgnoreCase) ? "DejaVu Serif" :
        font.StartsWith("MinionPro", StringComparison.OrdinalIgnoreCase) ? "Times New Roman" : font;

    private static XElement Table(DocxTable table, double left, double top)
    {
        var widths = table.Widths.Select(Twips).ToArray();
        var borders = new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }
            .Select(side => new XElement(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", 4), new XAttribute(W + "color", "auto"))));
        return new(W + "tbl", new XElement(W + "tblPr",
            new XElement(W + "tblpPr", new XAttribute(W + "vertAnchor", "page"), new XAttribute(W + "horzAnchor", "page"),
                new XAttribute(W + "tblpX", Twips(left)), new XAttribute(W + "tblpY", Twips(top))),
            new XElement(W + "tblOverlap", new XAttribute(W + "val", "overlap")),
            new XElement(W + "tblW", new XAttribute(W + "w", widths.Sum()), new XAttribute(W + "type", "dxa")), borders,
            new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")),
            new XElement(W + "tblCellMar", new[] { "top", "left", "bottom", "right" }.Select(side =>
                new XElement(W + side, new XAttribute(W + "w", 0), new XAttribute(W + "type", "dxa"))))),
            new XElement(W + "tblGrid", widths.Select(width => new XElement(W + "gridCol", new XAttribute(W + "w", width)))),
            table.Rows.Select((row, rowIndex) => new XElement(W + "tr",
                new XElement(W + "trPr", new XElement(W + "cantSplit"),
                    // Word adds the shared half-point rule to the row's content
                    // height. Account for it so lower rows do not drift.
                    new XElement(W + "trHeight", new XAttribute(W + "val", Twips(Math.Max(1, table.Heights[rowIndex] - 0.5))), new XAttribute(W + "hRule", "atLeast"))),
                row.Select((cell, index) => new XElement(W + "tc",
                    new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", widths[index]), new XAttribute(W + "type", "dxa")),
                        new XElement(W + "tcMar", new XElement(W + "top", new XAttribute(W + "w", 0), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "left", new XAttribute(W + "w", 0), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "bottom", new XAttribute(W + "w", 0), new XAttribute(W + "type", "dxa")),
                            new XElement(W + "right", new XAttribute(W + "w", 0), new XAttribute(W + "type", "dxa")))), Paragraph(cell, tableCell: true))))));
    }

    private static XElement Picture(DocxPicture picture, int id, double left, double top)
    {
        var cx = (long)Math.Round(picture.Width * 12700); var cy = (long)Math.Round(picture.Height * 12700);
        var pictureElement = new XElement(Pic + "pic", new XElement(Pic + "nvPicPr",
                    new XElement(Pic + "cNvPr", new XAttribute("id", id), new XAttribute("name", "PDF image " + id)), new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", "image" + id)),
                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))));
        var inline = new XElement(Wp + "anchor", new XAttribute("simplePos", 0), new XAttribute("relativeHeight", id),
            new XAttribute("behindDoc", 0), new XAttribute("locked", 0), new XAttribute("layoutInCell", 1), new XAttribute("allowOverlap", 1),
            new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement(Wp + "positionH", new XAttribute("relativeFrom", "page"), new XElement(Wp + "posOffset", (long)Math.Round(left * 12700))),
            new XElement(Wp + "positionV", new XAttribute("relativeFrom", "page"), new XElement(Wp + "posOffset", (long)Math.Round(top * 12700))),
            new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(Wp + "wrapNone"),
            new XElement(Wp + "docPr", new XAttribute("id", id), new XAttribute("name", "PDF image " + id)),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName), pictureElement)));
        return new(W + "p", new XElement(W + "pPr", CompactSpacing()), new XElement(W + "r", new XElement(W + "drawing", inline)));
    }

    private static XElement Rule(DocxRule rule, int id, double left, double top, double width)
    {
        // Native Office line geometry has no paper-colored image backing and
        // remains selectable/resizable in Word. Keep exact page coordinates.
        var style = FormattableString.Invariant($"position:absolute;margin-left:{left:0.###}pt;margin-top:{top:0.###}pt;mso-position-horizontal-relative:page;mso-position-vertical-relative:page");
        return new(W + "p", new XElement(W + "pPr", CompactSpacing()), new XElement(W + "r",
            new XElement(W + "pict", new XElement(V + "line", new XAttribute("id", "rule" + id),
                new XAttribute("style", style), new XAttribute("from", "0,0"),
                new XAttribute("to", FormattableString.Invariant($"{width:0.###}pt,0")),
                new XAttribute("strokecolor", "#" + rule.Color),
                new XAttribute("strokeweight", rule.Weight.ToString("0.###", CultureInfo.InvariantCulture) + "pt"),
                new XElement(W10 + "wrap", new XAttribute("type", "none"))))));
    }

    private static XElement Styles()
    {
        return new(W + "styles", new[] { "Normal", "Title", "Heading1", "Heading2", "Heading3", "Footer" }.Select(id =>
            new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id),
                id == "Normal" ? new XAttribute(W + "default", 1) : null,
                new XElement(W + "name", new XAttribute(W + "val", id.StartsWith("Heading", StringComparison.Ordinal) ? "heading " + id[^1] : id)),
                id == "Normal" ? null : new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
                new XElement(W + "qFormat"),
                id.StartsWith("Heading", StringComparison.Ordinal) ? new XElement(W + "pPr",
                    new XElement(W + "outlineLvl", new XAttribute(W + "val", id[^1] - '1'))) : null)));
    }

    private static XElement CompactSpacing() => new(W + "spacing", new XAttribute(W + "before", 0), new XAttribute(W + "after", 0),
        new XAttribute(W + "line", 20), new XAttribute(W + "lineRule", "exact"));

    private static int Twips(double points) => checked((int)Math.Round(points * 20));
    private static void Write(ZipArchive zip, string name, XElement content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), content).Save(stream);
    }
}
