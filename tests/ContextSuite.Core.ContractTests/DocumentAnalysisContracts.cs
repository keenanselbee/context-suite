using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static class DocumentAnalysisContracts
{
    private const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        foreach (var id in new[] { "docx", "xlsx", "pptx" })
        foreach (var strict in new[] { false, true })
        {
            var analysis = await AnalyzeAsync(OpenXml(id, strict), "renamed.bin");
            check(analysis.Identity.FormatId == id && analysis.Identity.Basis == IdentificationBasis.Content &&
                analysis.Identity.Confidence == IdentificationConfidence.Likely,
                $"documents: {id} {(strict ? "strict" : "transitional")} identification uses agreeing package declarations");
            check(id == "docx" ? analysis.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable :
                analysis.Facts.Single(fact => fact.Id == (id == "xlsx" ? "document.sheets" : "document.slides")).Integer == 2,
                $"documents: {id} reports declared lists without inventing rendered pages");
        }
        foreach (var id in new[] { "odt", "ods", "odp" })
        {
            var analysis = await AnalyzeAsync(OpenDocument(id), "renamed.bin");
            check(analysis.Identity.FormatId == id && analysis.Identity.Basis == IdentificationBasis.Content,
                $"documents: {id} package MIME, manifest and body family agree");
            check(id == "odt" ? analysis.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable :
                analysis.Facts.Single(fact => fact.Id == (id == "ods" ? "document.sheets" : "document.slides")).Integer == 2,
                $"documents: {id} counts only the relevant direct child elements");
        }
        var encrypted = await AnalyzeAsync(OpenDocument("odt", encrypted: true));
        check(encrypted.Identity.FormatId == "odt" && encrypted.Facts.Single(fact => fact.Id == "document.encrypted-content").Boolean == true &&
            encrypted.Facts.All(fact => fact.Id != "document.pages"), "documents: encrypted ODF content is described from declarations without decoding");
        var misleading = await AnalyzeAsync(OpenXml("xlsx"), "wrong.docx");
        check(misleading.Identity.FormatId == "xlsx" && misleading.Warnings.Any(warning => warning.Contains("different type")),
            "documents: content takes precedence over an incorrect document extension");
        var correct = await AnalyzeAsync(OpenXml("docx"), "right.docx");
        check(correct.Warnings.All(warning => !warning.Contains("container alone")) && correct.Identity.Evidence.All(item => !item.Contains("were not checked")),
            "documents: successful evidence replaces obsolete header-only explanations");
        var generic = await AnalyzeAsync(Zip(("ordinary.txt", "Authored fixture")), "wrong.docx");
        check(generic.Identity.FormatId == "zip" && generic.Warnings.Any(warning => warning.Contains("container alone")),
            "documents: a ZIP renamed DOCX is still a ZIP");

        var word = OpenXmlParts("docx");
        var macro = Zip(word.Select(part => part.Name == "[Content_Types].xml" ?
            (part.Name, part.Text.Replace("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                "application/vnd.ms-word.document.macroEnabled.main+xml")) : part).ToArray());
        var macroResult = await AnalyzeAsync(macro, "fixture.docm");
        check(macroResult.Identity.FormatId == "docx" && macroResult.Facts.Single(fact => fact.Id == "document.macro-type").Boolean == true,
            "documents: macro-enabled type is a declaration, not proof that a VBA project is present");
        var unsupported = new List<(string Name, byte[] Bytes)>
        {
            ("missing main relationship", Zip(word.Where(part => part.Name != "_rels/.rels").ToArray())),
            ("missing main part", Zip(word.Where(part => part.Name != "content/main.xml").ToArray())),
            ("duplicate ZIP names", Zip([.. word, word[0]])),
            ("case-ambiguous ZIP names", Zip([.. word, ("CONTENT/main.xml", "other")])),
            ("external main relationship", Zip(word.Select(part => part.Name == "_rels/.rels" ?
                (part.Name, part.Text.Replace("Target=", "TargetMode=\"External\" Target=")) : part).ToArray())),
            ("traversal main relationship", Zip(word.Select(part => part.Name == "_rels/.rels" ?
                (part.Name, part.Text.Replace("content/main.xml", "../content/main.xml")) : part).ToArray())),
            ("wrong content root", Zip(word.Select(part => part.Name == "content/main.xml" ? (part.Name, "<document/>") : part).ToArray())),
            ("DTD forbidden", Zip(word.Select(part => part.Name == "content/main.xml" ?
                (part.Name, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///must-not-be-read'>]><x>&e;</x>") : part).ToArray())),
            ("excessive XML depth", Zip(word.Select(part => part.Name == "content/main.xml" ?
                (part.Name, string.Concat(Enumerable.Repeat("<x>", 40)) + string.Concat(Enumerable.Repeat("</x>", 40))) : part).ToArray())),
            ("expanded part limit", Zip(word.Select(part => part.Name == "content/main.xml" ?
                (part.Name, "<x>" + new string('x', 300 * 1024) + "</x>") : part).ToArray())),
            ("conflicting document families", Zip([.. word, ("mimetype", "application/vnd.oasis.opendocument.text"), ("META-INF/manifest.xml", "<x/>")])),
            ("ODF MIME conflict", OpenDocument("odt", mismatched: true)),
            ("ODF body conflict", OpenDocument("odt", wrongBody: true))
        };
        var original = OpenXml("docx");
        var truncated = original[..^8]; unsupported.Add(("truncated ZIP", truncated));
        var zip64 = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(zip64.AsSpan(zip64.Length - 12), ushort.MaxValue);
        unsupported.Add(("ZIP64/count limit", zip64));
        var directorySize = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(directorySize.AsSpan(directorySize.Length - 10), uint.MaxValue);
        unsupported.Add(("directory allocation limit", directorySize));
        var badCrc = (byte[])original.Clone();
        var central = Find(badCrc, "PK\u0001\u0002"u8);
        badCrc[central + 16] ^= 1;
        unsupported.Add(("conflicting ZIP checksum", badCrc));
        var payloadCrc = Zip(word, CompressionLevel.NoCompression);
        var payloadStart = 30 + BinaryPrimitives.ReadUInt16LittleEndian(payloadCrc.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(payloadCrc.AsSpan(28));
        payloadCrc[payloadStart] ^= 1;
        unsupported.Add(("corrupted entry bytes", payloadCrc));
        var tinyDeclaration = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(tinyDeclaration.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(tinyDeclaration.AsSpan(central + 24), 1);
        unsupported.Add(("inflation exceeds declared size", tinyDeclaration));
        foreach (var item in unsupported)
        {
            var result = await AnalyzeAsync(item.Bytes);
            check(result.Identity.FormatId == "zip" && result.Warnings.Any(warning => warning.Contains("details are unavailable")),
                "documents: safe header fallback for " + item.Name);
        }

        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await AnalyzeAsync(original, cancellationToken: canceled.Token); check(false, "documents: cancellation propagates"); }
            catch (OperationCanceledException) { check(true, "documents: cancellation propagates"); }
        }
        // A large, unrelated stored member must not be inflated or copied into a
        // whole-file snapshot just to identify a small main document part.
        var large = Zip([.. word, ("media/unused.bin", new string('x', 8 * 1024 * 1024))], CompressionLevel.NoCompression);
        var largeResult = await AnalyzeAsync(large);
        check(largeResult.Identity.FormatId == "docx" && largeResult.InspectedBytes < 150000 &&
            largeResult.Facts.Single(fact => fact.Id == "package.bytes-read").Integer < 100000,
            "documents: large unrelated payload is skipped with bounded seeking");
        var random = new Random(701);
        var mutationSafe = true;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var mutated = (byte[])original.Clone();
            for (var change = 0; change < 3; change++) mutated[random.Next(mutated.Length)] ^= (byte)random.Next(1, 256);
            var result = await AnalyzeAsync(mutated);
            mutationSafe &= result.InspectedBytes <= mutated.Length;
        }
        check(mutationSafe, "documents: 100 deterministic damaged packages retain bounded results");
        var root = Path.Combine(scratch, "documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "authored.docx");
        await File.WriteAllBytesAsync(path, original);
        var hash = SHA256.HashData(original);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var read = await FileAnalysisReader.ReadAsync(path, CancellationToken.None);
        check(read.Identity.FormatId == "docx" && read.Facts.Any(fact => fact.Id == "document.pages"),
            "documents: real application reader enriches a generated document under its existing read lease");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        check(hash.SequenceEqual(after) && timestamp == File.GetLastWriteTimeUtc(path),
            "documents: original bytes and write timestamp remain unchanged");
    }

    private static async Task<FileAnalysis> AnalyzeAsync(byte[] bytes, string name = "fixture.docx", CancellationToken cancellationToken = default)
    {
        var header = HeaderAnalyzer.Analyze(name, bytes.AsSpan(0, Math.Min(bytes.Length, HeaderAnalyzer.MaximumBytes)), bytes.Length);
        using var stream = new MemoryStream(bytes, writable: false);
        return await DocumentAnalysis.AddPackageAsync(header, stream, cancellationToken);
    }

    private static byte[] OpenXml(string id, bool strict = false) => Zip(OpenXmlParts(id, strict));

    private static (string Name, string Text)[] OpenXmlParts(string id, bool strict = false)
    {
        var family = id switch { "docx" => "wordprocessingml", "xlsx" => "spreadsheetml", _ => "presentationml" };
        var kind = id switch { "docx" => "document", "xlsx" => "sheet", _ => "presentation" };
        var root = id switch { "docx" => "document", "xlsx" => "workbook", _ => "presentation" };
        var body = id switch { "docx" => "<body><p/></body>", "xlsx" => "<sheets><sheet/><sheet/></sheets>", _ => "<sldIdLst><sldId/><sldId/></sldIdLst>" };
        var ns = strict ? $"http://purl.oclc.org/ooxml/{family}/main" : $"http://schemas.openxmlformats.org/{family}/2006/main";
        var rel = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument" :
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        return
        [
            ("[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/content/main.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.{family}.{kind}.main+xml\"/></Types>"),
            ("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"{rel}\" Target=\"content/main.xml\"/></Relationships>"),
            ("content/main.xml", $"<{root} xmlns=\"{ns}\">{body}</{root}>")
        ];
    }

    private static byte[] OpenDocument(string id, bool encrypted = false, bool mismatched = false, bool wrongBody = false)
    {
        var family = id switch { "odt" => "text", "ods" => "spreadsheet", _ => "presentation" };
        var mime = "application/vnd.oasis.opendocument." + family;
        var body = id switch { "odt" => "", "ods" => "<t:table/><t:table/>", _ => "<d:page/><d:page/>" };
        var content = encrypted ? "opaque encrypted content" :
            $"<o:document-content xmlns:o=\"{Office}\" xmlns:t=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:d=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"><o:body><o:{(wrongBody ? "other" : family)}>{body}</o:{(wrongBody ? "other" : family)}></o:body></o:document-content>";
        return Zip(("mimetype", mime),
            ("META-INF/manifest.xml", $"<m:manifest xmlns:m=\"{Manifest}\"><m:file-entry m:full-path=\"/\" m:media-type=\"{(mismatched ? "other" : mime)}\"/><m:file-entry m:full-path=\"content.xml\" m:media-type=\"text/xml\">{(encrypted ? "<m:encryption-data/>" : "")}</m:file-entry></m:manifest>"),
            ("content.xml", content));
    }

    private static byte[] Zip(params (string Name, string Text)[] parts) => Zip(parts, CompressionLevel.Optimal);

    private static byte[] Zip((string Name, string Text)[] parts, CompressionLevel compression)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var part in parts)
        {
            using var output = archive.CreateEntry(part.Name, compression).Open();
            output.Write(Encoding.UTF8.GetBytes(part.Text));
        }
        return memory.ToArray();
    }

    private static int Find(byte[] bytes, ReadOnlySpan<byte> pattern)
    {
        return bytes.AsSpan().IndexOf(pattern);
    }
}
