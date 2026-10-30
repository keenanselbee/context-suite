using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Pdf;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ImageMagick;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

internal static class PdfDocxContracts
{
    public static async Task RunAsync(string root, string? rendererDirectory = null)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a new evidence directory.");
        Directory.CreateDirectory(root);
        var checks = 0;
        using var renderer = rendererDirectory is null ? null : new PdfRasterAdapter(Path.GetFullPath(rendererDirectory), Path.Combine(root, "renderer"));
        var adapter = new PdfDocxAdapter(renderer);
        foreach (var kind in new[] { "ordinary", "prose", "fields", "simple-rule", "default-rule", "dashed-rule", "columns", "table", "table-tolerant", "overlap-rules", "overlap-rules-same-font", "image", "multipage", "footers", "accents", "artwork", "annotated", "scanned", "blank" })
        {
            var builder = new PdfDocumentBuilder();
            var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
            var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);
            var page = builder.AddPage(612, 792);
            if (kind is not ("scanned" or "blank"))
            {
                page.AddText("Quarterly report", 18, new PdfPoint(40, 740), bold);
                if (kind == "columns")
                {
                    page.AddText("Left column first paragraph.", 11, new PdfPoint(40, 690), regular);
                    page.AddText("Left column second paragraph.", 11, new PdfPoint(40, 670), regular);
                    page.AddText("Right column first paragraph.", 11, new PdfPoint(330, 690), regular);
                    page.AddText("Right column second paragraph.", 11, new PdfPoint(330, 670), regular);
                }
                else page.AddText("Revenue increased by 12 percent.", 11, new PdfPoint(40, 690), regular);
                if (kind == "prose")
                {
                    page.AddText("The local worker preserves source files while the converter", 11, new PdfPoint(40, 660), regular);
                    page.AddText("writes a validated editable Word copy for review.", 11, new PdfPoint(40, 647), regular);
                }
                if (kind == "fields")
                {
                    page.AddText("Explain the setting that should remain a separate field:", 11, new PdfPoint(40, 660), regular);
                    page.AddText("This answer belongs to the preceding prompt.", 11, new PdfPoint(40, 647), regular);
                }
                if (kind == "simple-rule")
                {
                    page.SetStrokeColor(0, 80, 180);
                    page.DrawLine(new PdfPoint(40, 640), new PdfPoint(240, 640), 1);
                }
                if (kind == "default-rule") page.DrawLine(new PdfPoint(40, 640), new PdfPoint(240, 640), 1);
                if (kind == "annotated") page.AddLink("https://example.invalid/", new PdfRectangle(40, 680, 250, 710));
                if (kind == "accents")
                {
                    var unicode = builder.AddTrueTypeFont(await File.ReadAllBytesAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")));
                    page.AddText("Résumé: café, naïve, façade. Ελληνικά. Кириллица.", 11, new PdfPoint(40, 650), unicode);
                }
                if (kind == "artwork")
                {
                    page.DrawLine(new PdfPoint(40, 400), new PdfPoint(200, 550), 3);
                    page.AddText("Chart label", 11, new PdfPoint(80, 460), regular);
                }
                if (kind == "multipage")
                {
                    var second = builder.AddPage(612, 792);
                    second.AddText("Second page heading", 18, new PdfPoint(40, 740), bold);
                    second.AddText("Second page body stays editable.", 11, new PdfPoint(40, 690), regular);
                }
                if (kind == "footers")
                {
                    page.AddText("Context Suite worksheet", 9, new PdfPoint(40, 36), regular);
                    var second = builder.AddPage(612, 792);
                    second.AddText("Second section body", 11, new PdfPoint(40, 690), regular);
                    second.AddText("Final answer near footer", 11, new PdfPoint(40, 57), regular);
                    second.AddText("Context Suite worksheet", 9, new PdfPoint(40, 36), regular);
                    var third = builder.AddPage(612, 792);
                    third.AddText("Third section body", 11, new PdfPoint(40, 690), regular);
                    third.AddText("One-off bottom note", 9, new PdfPoint(40, 36), regular);
                }
                if (kind == "table")
                {
                    foreach (var y in new[] { 640, 610, 580 }) page.DrawLine(new PdfPoint(40, y), new PdfPoint(440, y), 1);
                    foreach (var x in new[] { 40, 240, 440 }) page.DrawLine(new PdfPoint(x, 580), new PdfPoint(x, 640), 1);
                    page.AddText("Category", 11, new PdfPoint(48, 622), bold);
                    page.AddText("Amount", 11, new PdfPoint(248, 622), bold);
                    page.AddText("Services", 11, new PdfPoint(48, 592), regular);
                    page.AddText("1200", 11, new PdfPoint(248, 592), regular);
                }
                if (kind == "table-tolerant")
                {
                    page.DrawLine(new PdfPoint(40, 640), new PdfPoint(440, 640), 1);
                    page.DrawLine(new PdfPoint(40.5, 610), new PdfPoint(439.5, 610), 1);
                    page.DrawLine(new PdfPoint(40, 570), new PdfPoint(440, 570), 1);
                    foreach (var x in new[] { 40, 240, 440 })
                        page.DrawLine(new PdfPoint(x, 569.5), new PdfPoint(x, 640.5), 1);
                    page.AddText("Category", 11, new PdfPoint(48, 622), bold);
                    page.AddText("Amount", 11, new PdfPoint(248, 622), bold);
                    page.AddText("Monthly services", 11, new PdfPoint(48, 592), regular);
                    page.AddText("including support", 11, new PdfPoint(48, 577), regular);
                    page.AddText("1200", 11, new PdfPoint(248, 592), regular);
                }
                if (kind is "overlap-rules" or "overlap-rules-same-font")
                {
                    page.AddText("Answer:", 11, new PdfPoint(40, 650), regular);
                    page.AddText("________________________________", 11, new PdfPoint(40, 630), regular);
                    page.AddText("Routes handle HTTP", 11, new PdfPoint(43, 630.5),
                        kind == "overlap-rules-same-font" ? regular : bold);
                    page.AddText("The identifier team_code_v2 stays intact.", 11, new PdfPoint(40, 590), regular);
                }
            }
            if (kind is "image" or "scanned")
            {
                // Tiny opaque authored RGB checker; no external input or font.
                ushort[][] colors = [[65535, 0, 0], [0, 65535, 0], [0, 0, 65535], [65535, 65535, 0]];
                var png = PngFixture.Create(2, 2, 8, 2, (x, y) => colors[y * 2 + x]);
                page.AddPng(png, new PdfRectangle(40, 500, 140, 600));
            }
            var path = Path.Combine(root, kind + ".pdf");
            var bytes = kind == "dashed-rule" ? BuildDashedRuleFixture() : builder.Build();
            await File.WriteAllBytesAsync(path, bytes);
            var source = await adapter.ProbeAsync(new(Guid.NewGuid(), path), default);
            if (kind is "scanned" or "blank")
            {
                Check(source.NeedsOcr, "no-text input cannot claim editable conversion: " + kind);
                continue;
            }
            var id = Guid.NewGuid();
            var reserved = Path.Combine(root, $".context-suite-{id:N}.tmp");
            await File.WriteAllBytesAsync(reserved, []);
            if (kind == "annotated" || (kind is "artwork" or "dashed-rule") && renderer is null)
            {
                try { await adapter.ConvertAsync(new(source, id, reserved), default); Check(false, "unsupported artwork accepted"); }
                catch (ContextSuite.Core.Images.ImageFailureException error) when (error.Failure == ContextSuite.Core.Images.ImageFailure.UnsupportedInput)
                { Check(new FileInfo(reserved).Length == 0, "unsupported content never silently disappears: " + kind); }
                continue;
            }
            var result = await adapter.ConvertAsync(new(source, id, reserved), default);
            var output = Path.Combine(root, kind + ".docx"); File.Move(reserved, output);
            using var word = WordprocessingDocument.Open(output, false);
            var errors = new OpenXmlValidator().Validate(word).Take(8).ToArray();
            Check(errors.Length == 0, "independent DOCX schema: " + kind + " " + string.Join("; ", errors.Select(error => error.Description)));
            using var zip = ZipFile.OpenRead(output);
            using var part = zip.GetEntry("word/document.xml")!.Open();
            var document = XDocument.Load(part);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace v = "urn:schemas-microsoft-com:vml";
            XNamespace w10 = "urn:schemas-microsoft-com:office:word";
            var text = string.Concat(document.Descendants(w + "t").Select(element => element.Value));
            Check(text.Contains("Quarterly report"), "editable heading survives: " + kind);
            Check(document.Descendants(w + "b").Any(), "font emphasis survives: " + kind);
            Check(document.Descendants(w + "p").Any(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)) == "Quarterly report"),
                "heading is a separate editable paragraph: " + kind);
            Check(document.Descendants(w + "p").Any(p =>
                string.Concat(p.Descendants(w + "t").Select(t => t.Value)) == "Quarterly report" &&
                (p.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value is "Title" or "Heading1")),
                "heading has a semantic Word style: " + kind);
            var unchanged = await File.ReadAllBytesAsync(path);
            Check(bytes.AsSpan().SequenceEqual(unchanged), "source bytes unchanged: " + kind);
            Check(result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output))), "result binds exact output: " + kind);
            if (kind == "columns") Check(text.IndexOf("Left column second", StringComparison.Ordinal) < text.IndexOf("Right column first", StringComparison.Ordinal), "column reading order");
            if (kind == "prose") Check(document.Descendants(w + "p").Count(p =>
                string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("The local worker preserves source files", StringComparison.Ordinal) &&
                string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("writes a validated editable Word copy", StringComparison.Ordinal) &&
                p.Element(w + "pPr")?.Element(w + "framePr") is not null && !p.Descendants(w + "br").Any()) == 1,
                "wrapped prose is one editable positioned paragraph");
            if (kind == "fields") Check(document.Descendants(w + "p").Count(p =>
                string.Concat(p.Descendants(w + "t").Select(t => t.Value)).StartsWith("Explain the setting", StringComparison.Ordinal)) == 1 &&
                document.Descendants(w + "p").Count(p =>
                    string.Concat(p.Descendants(w + "t").Select(t => t.Value)).StartsWith("This answer belongs", StringComparison.Ordinal)) == 1 &&
                !document.Descendants(w + "p").Any(p =>
                    string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("field: This answer", StringComparison.Ordinal)),
                "nearby prompt and answer remain separate paragraphs");
            if (kind == "simple-rule") Check(document.Descendants(v + "line").Count() == 1 &&
                document.Descendants(v + "line").Single().Attribute("strokecolor")?.Value == "#0050B4" &&
                document.Descendants(v + "line").Single().Attribute("to")?.Value == "200pt,0" &&
                document.Descendants(v + "line").Single().Element(w10 + "wrap")?.Attribute("type")?.Value == "none" &&
                result.FigureCount == 0 && document.Descendants(w + "drawing").Count() == 0,
                "straight PDF stroke remains a native editable Word line");
            if (kind == "default-rule") Check(document.Descendants(v + "line").Count() == 1 &&
                document.Descendants(v + "line").Single().Attribute("strokecolor")?.Value == "#000000" &&
                document.Descendants(v + "line").Single().Element(w10 + "wrap")?.Attribute("type")?.Value == "none" &&
                result.FigureCount == 0 && document.Descendants(w + "drawing").Count() == 0,
                "unspecified PDF stroke color uses native black Word line");
            if (kind == "dashed-rule") Check(document.Descendants(v + "line").Count() == 0 &&
                result.FigureCount == 1 && document.Descendants(w + "drawing").Count() == 1 &&
                text.Contains("Revenue increased by 12 percent.", StringComparison.Ordinal),
                "dashed stroke retains bounded figure fallback beside editable text");
            if (kind == "table") Check(result.TableCount == 1 && document.Descendants(w + "tc").Count() == 4 && text.Contains("1200"), "ruled table remains editable cells");
            if (kind == "table-tolerant") Check(result.TableCount == 1 && document.Descendants(w + "tc").Count() == 4 &&
                document.Descendants(w + "tc").Any(cell =>
                    cell.Descendants(w + "p").Any(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("Monthly", StringComparison.Ordinal)) &&
                    cell.Descendants(w + "p").Any(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("including", StringComparison.Ordinal))) &&
                text.Contains("1200") && result.TextCharacters == source.TextCharacters,
                "offset table rules and multiline cell remain editable");
            if (kind is "overlap-rules" or "overlap-rules-same-font")
            {
                var paragraphs = document.Descendants(w + "p").Select(p =>
                    string.Concat(p.Descendants(w + "t").Select(t => t.Value))).ToArray();
                using var sourcePdf = PdfDocument.Open(path);
                var glyphHeight = sourcePdf.GetPage(1).Letters.Where(letter => letter.Value == "_")
                    .Max(letter => letter.BoundingBox.Height);
                var line = document.Descendants(v + "line").Single();
                var strokeWeight = line.Attribute("strokeweight")?.Value.TrimEnd('p', 't');
                Check(paragraphs.Any(p => p.Contains("Routes handle HTTP", StringComparison.Ordinal)) &&
                    paragraphs.Any(p => p.Contains("team_code_v2", StringComparison.Ordinal)) &&
                    !text.Contains("______", StringComparison.Ordinal) &&
                    line.Element(w10 + "wrap")?.Attribute("type")?.Value == "none" &&
                    double.TryParse(strokeWeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) &&
                    weight is > 0 and <= 2 && weight <= glyphHeight + 0.02 &&
                    result.TextCharacters == source.TextCharacters && result.FigureTextCharacters == 0,
                    "overlapping answer and identifier stay editable beside thin non-wrapping native rule");
            }
            if (kind == "image") Check(result.ImageCount == 1 && document.Descendants(w + "drawing").Count() == 1 && zip.Entries.Any(entry => entry.FullName.EndsWith(".png")), "embedded image preserved");
            if (kind == "artwork") Check(result.FigureCount == 1 && result.FigureTextCharacters == 10 &&
                document.Descendants(w + "drawing").Count() == 1 && !text.Contains("Chart label"), "complex figure keeps artwork and its labels together as an image");
            if (kind == "accents") Check(text.Contains("Résumé: café, naïve, façade."), "accented text survives extraction and OOXML");
            if (kind == "multipage") Check(result.PageCount == 2 && document.Descendants(w + "sectPr").Count() == 2 && text.Contains("Second page body"), "page sections and second-page editable content survive");
            if (kind == "footers")
            {
                XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
                using var relationPart = zip.GetEntry("word/_rels/document.xml.rels")!.Open();
                var relationships = XDocument.Load(relationPart);
                var sections = document.Descendants(w + "sectPr").ToArray();
                var footerTexts = new List<string>();
                var footerStyles = new List<string?>();
                for (var index = 1; index <= 3; index++)
                {
                    using var footerPart = zip.GetEntry($"word/footer{index}.xml")!.Open();
                    var footer = XDocument.Load(footerPart);
                    footerTexts.Add(string.Concat(footer.Descendants(w + "t").Select(t => t.Value)));
                    footerStyles.Add(footer.Descendants(w + "pStyle").FirstOrDefault()?.Attribute(w + "val")?.Value);
                }
                Check(result.PageCount == 3 && sections.Length == 3 &&
                    sections.Select(section => section.Element(w + "footerReference")?.Attribute(rel + "id")?.Value)
                        .SequenceEqual(new[] { "footer1", "footer2", "footer3" }) &&
                    relationships.Descendants(pkg + "Relationship").Count(r => r.Attribute("Type")?.Value.EndsWith("/footer", StringComparison.Ordinal) == true) == 3 &&
                    footerTexts.SequenceEqual(new[] { "Context Suite worksheet", "Context Suite worksheet", "" }) &&
                    footerStyles.Take(2).All(style => style == "Footer") &&
                    !text.Contains("Context Suite worksheet", StringComparison.Ordinal) &&
                    text.Contains("Final answer near footer", StringComparison.Ordinal) &&
                    text.Contains("One-off bottom note", StringComparison.Ordinal),
                    "repeated footer uses section parts despite nearby body text; unrelated low text stays in body");
            }
        }
        foreach (var kind in new[] { "stamp", "stamp-unpaired", "stamp-white-paint", "stamp-no-appearance", "stamp-action", "popup-content", "unsupported-annotation" })
        {
            var bytes = BuildStampFixture(kind);
            var path = Path.Combine(root, kind + ".pdf");
            await File.WriteAllBytesAsync(path, bytes);
            var source = await adapter.ProbeAsync(new(Guid.NewGuid(), path), default);
            Check(!source.NeedsOcr && source.TextCharacters > 0, "stamp fixture retains extractable text: " + kind);
            var id = Guid.NewGuid();
            var reserved = Path.Combine(root, $".context-suite-{id:N}.tmp");
            await File.WriteAllBytesAsync(reserved, []);
            if (kind is not ("stamp" or "stamp-unpaired" or "stamp-white-paint") || renderer is null)
            {
                try { await adapter.ConvertAsync(new(source, id, reserved), default); Check(false, "unsafe annotation accepted: " + kind); }
                catch (ContextSuite.Core.Images.ImageFailureException error) when (error.Failure == ContextSuite.Core.Images.ImageFailure.UnsupportedInput)
                { Check(new FileInfo(reserved).Length == 0, "annotation refusal leaves output empty: " + kind); }
                continue;
            }
            var result = await adapter.ConvertAsync(new(source, id, reserved), default);
            var output = Path.Combine(root, kind + ".docx"); File.Move(reserved, output);
            using var word = WordprocessingDocument.Open(output, false);
            Check(!new OpenXmlValidator().Validate(word).Any(), "stamp DOCX schema");
            using var zip = ZipFile.OpenRead(output);
            using var part = zip.GetEntry("word/document.xml")!.Open();
            var document = XDocument.Load(part);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var text = string.Concat(document.Descendants(w + "t").Select(element => element.Value));
            Check(result.FigureCount == 1 && document.Descendants(w + "drawing").Any(), "stamp appearance preserved as a figure");
            Check(result.TableCount == 1 && document.Descendants(w + "tbl").Any() &&
                document.Descendants(w + "tc").Any(cell => cell.Descendants(w + "p").Any(p =>
                    string.Concat(p.Descendants(w + "t").Select(t => t.Value)).Contains("including", StringComparison.Ordinal))),
                "stamp leaves ruled table and multiline text editable");
            Check(text.Contains("Quarterly report") && text.Contains("Reviewed") && result.TextCharacters == source.TextCharacters,
                "stamp leaves underlying page text intact");
            using (var imagePart = zip.Entries.Single(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal)).Open())
            using (var image = new MagickImage(imagePart))
            {
                var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
                var colors = Enumerable.Range(0, pixels.Length / 4).Select(index => index * 4).ToArray();
                Check(image.HasAlpha && colors.Any(index => pixels[index + 3] == 0) &&
                    colors.Any(index => pixels[index + 3] == 255 && pixels[index + 2] > pixels[index]),
                    "stamp preserves transparent paper and opaque colored ink: " + kind);
                Check(colors.Any(index => pixels[index] == 255 && pixels[index + 1] == 255 &&
                    pixels[index + 2] == 255 && pixels[index + 3] == 255) == (kind == "stamp-white-paint"),
                    "explicit white stamp paint survives without inventing a paper matte: " + kind);
            }
            var unchanged = await File.ReadAllBytesAsync(path);
            Check(bytes.AsSpan().SequenceEqual(unchanged), "stamp source unchanged");
        }
        var modeledPage = new DocxPage(612, 792,
        [
            new(new PdfRectangle(40, 700, 210, 720), Paragraph: new([new("Minion body", "MinionPro-Regular", 11, false, false, "000000")], Baseline: 705)),
            new(new PdfRectangle(40, 670, 210, 690), Paragraph: new([new("Bold heading", "DejaVuSans-Bold", 12, true, false, "000000")], Baseline: 675)),
            new(new PdfRectangle(40, 640, 210, 660), Paragraph: new([new("Oblique note", "DejaVuSans-Oblique", 11, false, true, "000000")], Baseline: 645)),
            new(new PdfRectangle(40, 610, 210, 630), Paragraph: new([new("code_name", "DejaVuSansMono", 10, false, false, "000000")], Baseline: 615)),
            new(new PdfRectangle(40, 560, 240, 590), Table: new([200], [[new([new("Minion cell", "MinionPro-Regular", 11, false, false, "000000")])]], [30]))
        ]);
        var modeledBytes = PdfDocxWriter.Create([modeledPage], default);
        using (var memory = new MemoryStream(modeledBytes, false))
        using (var word = WordprocessingDocument.Open(memory, false))
        {
            var errors = new OpenXmlValidator().Validate(word).Take(8).ToArray();
            Check(errors.Length == 0, "authored font fallback DOCX schema: " + string.Join("; ", errors.Select(error => error.Description)));
        }
        using (var memory = new MemoryStream(modeledBytes, false))
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
        using (var part = zip.GetEntry("word/document.xml")!.Open())
        {
            var document = XDocument.Load(part);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var text = string.Concat(document.Descendants(w + "t").Select(element => element.Value));
            var runs = document.Descendants(w + "r").Where(run => run.Element(w + "t") is not null).ToArray();
            Check(text == "Minion bodyBold headingOblique notecode_nameMinion cell" &&
                runs.Length == 5 &&
                runs[0].Element(w + "rPr")?.Element(w + "rFonts")?.Attribute(w + "ascii")?.Value == "Times New Roman" &&
                runs[0].Element(w + "rPr")?.Element(w + "w") is null &&
                runs[1].Element(w + "rPr")?.Element(w + "rFonts")?.Attribute(w + "ascii")?.Value == "DejaVu Sans" &&
                runs[1].Element(w + "rPr")?.Element(w + "b") is not null &&
                runs[2].Element(w + "rPr")?.Element(w + "rFonts")?.Attribute(w + "ascii")?.Value == "DejaVu Sans" &&
                runs[2].Element(w + "rPr")?.Element(w + "i") is not null &&
                runs[3].Element(w + "rPr")?.Element(w + "rFonts")?.Attribute(w + "ascii")?.Value == "DejaVu Sans Mono" &&
                runs[4].Element(w + "rPr")?.Element(w + "rFonts")?.Attribute(w + "ascii")?.Value == "Times New Roman" &&
                runs[4].Element(w + "rPr")?.Element(w + "w")?.Attribute(w + "val")?.Value == "97",
                "authored Minion and DejaVu fallbacks retain editable text, natural prose width, and table width");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "result.txt"), $"Passed {checks} PDF-to-DOCX checks. No visual acceptance claimed.");
        Console.WriteLine($"Passed {checks} PDF-to-DOCX checks. Evidence: {root}");
        void Check(bool passed, string message) { if (!passed) throw new InvalidDataException(message); checks++; Console.WriteLine("PASS " + message); }
    }

    private static byte[] BuildDashedRuleFixture()
    {
        var content = "BT /F2 18 Tf 40 740 Td (Quarterly report) Tj ET\n" +
            "BT /F1 11 Tf 40 690 Td (Revenue increased by 12 percent.) Tj ET\n" +
            "[4 3] 0 d 0 0 1 RG 1 w 40 640 m 240 640 l S\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };
        using var output = new MemoryStream();
        void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] BuildStampFixture(string kind)
    {
        var stream = "BT /F1 18 Tf 40 740 Td (Quarterly report) Tj ET\n" +
            "BT /F1 11 Tf 40 690 Td (Revenue increased by 12 percent.) Tj ET\n" +
            "BT /F1 11 Tf 40 550 Td (Reviewed) Tj ET\n" +
            "40 640 m 440 640 l 40 610 m 440 610 l 40 570 m 440 570 l " +
            "40 570 m 40 640 l 240 570 m 240 640 l 440 570 m 440 640 l S\n" +
            "BT /F1 11 Tf 48 622 Td (Category) Tj ET\n" +
            "BT /F1 11 Tf 248 622 Td (Amount) Tj ET\n" +
            "BT /F1 11 Tf 48 592 Td (Monthly services) Tj ET\n" +
            "BT /F1 11 Tf 48 577 Td (including support) Tj ET\n" +
            "BT /F1 11 Tf 248 592 Td (1200) Tj ET\n";
        var appearance = "q 0 0 1 RG 2 w 2 8 m 6 3 l 14 12 l S Q\n";
        if (kind == "stamp-white-paint") appearance = "q 1 1 1 rg 5 5 5 5 re f Q\n" + appearance;
        var stampType = kind == "unsupported-annotation" ? "Text" : "Stamp";
        var ap = kind == "stamp-no-appearance" ? "" : " /AP << /N 8 0 R >>";
        var action = kind == "stamp-action" ? " /A << /S /URI /URI (https://example.invalid/) >>" : "";
        var popupContent = kind == "popup-content" ? "unsafe note" : "";
        var popup = kind == "stamp-unpaired" ? "" : " /Popup 7 0 R";
        var annotations = kind == "stamp-unpaired" ? "[6 0 R]" : "[6 0 R 7 0 R]";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /Annots {annotations} >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Type /Annot /Subtype /{stampType} /Rect [120 540 137 555] /Contents () /F 4{ap}{action}{popup} >>",
            $"<< /Type /Annot /Subtype /Popup /Parent 6 0 R /Rect [500 540 600 620] /Open false /Contents ({popupContent}) >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 17 15] /Resources << >> /Length {Encoding.ASCII.GetByteCount(appearance)} >>\nstream\n{appearance}endstream"
        };
        using var output = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes);
        }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
