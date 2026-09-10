using System.Globalization;
using System.Text;

internal static class PdfFixture
{
    // Independently authored test objects, not a general-purpose PDF writer.
    public static byte[] Create(int port = 0, bool signatureField = false, bool signatureWidget = true)
    {
        var content = "% Context Suite authored compression fixture\n" + new string(' ', 24000) +
            "\n/Span <</MCID 0>> BDC BT /F1 18 Tf 36 700 Td (Authored PDF fixture) Tj ET EMC\n" +
            "q 0.2 0.4 0.6 rg 36 600 100 40 re f Q\nq 40 0 0 40 36 500 cm /Im0 Do Q\n";
        var objects = new List<byte[]>
        {
            Text("<< /Type /Catalog /Pages 2 0 R /Outlines 8 0 R /AcroForm 10 0 R /Names << /EmbeddedFiles << /Names [(fixture.txt) 15 0 R] >> >> /Metadata 17 0 R /MarkInfo << /Marked true >> /StructTreeRoot 18 0 R /Lang (en-US)" +
                (port == 0 ? "" : $" /OpenAction << /S /JavaScript /JS (app.launchURL\\('http://127.0.0.1:{port}/must-not-run'\\);) >>") + " >>"),
            Text("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>"),
            Text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /CropBox [18 18 594 774] /Resources << /Font << /F1 7 0 R >> /XObject << /Im0 20 0 R >> >> /Contents 5 0 R /Annots [9 0 R 11 0 R" +
                (signatureField && signatureWidget ? " 23 0 R" : "") + "] /StructParents 0 >>"),
            Text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Rotate 90 /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>"),
            Stream("", Text(content)),
            Stream("", Text("BT /F1 12 Tf 20 30 Td (Second page, different size and rotation) Tj ET\n")),
            Text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Text("<< /Type /Outlines /First 12 0 R /Last 12 0 R /Count 1 >>"),
            Text("<< /Type /Annot /Subtype /Link /Rect [36 600 136 640] /Border [0 0 0] /A << /S /URI /URI (https://example.invalid/authored-link) >> >>"),
            Text("<< /Fields [11 0 R" + (signatureField ? " 23 0 R" : "") + "] /NeedAppearances false /DA (/F1 12 Tf 0 g) /DR << /Font << /F1 7 0 R >> >> >>"),
            Text("<< /Type /Annot /Subtype /Widget /FT /Tx /T (AuthoredField) /V (Authored value) /Rect [36 450 200 475] /P 3 0 R /AP << /N 13 0 R >> >>"),
            Text("<< /Title (Authored bookmark) /Parent 8 0 R /Dest [3 0 R /Fit] >>"),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 164 25] /Resources << /Font << /F1 7 0 R >> >>",
                Text("BT /F1 12 Tf 2 6 Td (Authored value) Tj ET\n")),
            Stream("/Type /EmbeddedFile /Subtype /text#2Fplain", Text("Independently authored attachment.\n")),
            Text("<< /Type /Filespec /F (fixture.txt) /UF (fixture.txt) /EF << /F 14 0 R >> >>"),
            Text("<< /Title (Context Suite disposable PDF) /Producer (Context Suite test fixture writer) >>"),
            Stream("/Type /Metadata /Subtype /XML", Text("<?xpacket begin='\ufeff'?><x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:description>Authored fixture metadata</dc:description></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>")),
            Text("<< /Type /StructTreeRoot /K 19 0 R /ParentTree 21 0 R /ParentTreeNextKey 1 >>"),
            Text("<< /Type /StructElem /S /P /P 18 0 R /Pg 3 0 R /K 0 >>"),
            Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 22 0 R",
                [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]),
            Text("<< /Nums [0 [19 0 R]] >>"),
            Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8", [255, 128, 64, 0])
        };
        if (signatureField)
        {
            objects.Add(Text("<< /FT /Sig /T (Signature canary - not a valid signature) /V 24 0 R" +
                (signatureWidget ? " /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /F 132 /P 3 0 R" : "") + " >>"));
            objects.Add(Text("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <0000> >>"));
        }
        using var output = new MemoryStream();
        output.Write(Text("%PDF-1.7\n% Authored fixture\n"));
        var positions = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            positions.Add(output.Position);
            output.Write(Text($"{index + 1} 0 obj\n")); output.Write(objects[index]); output.Write(Text("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Text($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var position in positions) output.Write(Text(position.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        output.Write(Text($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 16 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();
    }

    private static byte[] Stream(string dictionary, byte[] bytes)
    {
        using var result = new MemoryStream();
        result.Write(Text($"<< {dictionary} /Length {bytes.Length} >>\nstream\n"));
        result.Write(bytes); result.Write(Text("\nendstream"));
        return result.ToArray();
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
}
