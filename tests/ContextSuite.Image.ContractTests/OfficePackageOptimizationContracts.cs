using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ContextSuite.Private.Office;
using ImageMagick;

internal static class OfficePackageOptimizationContracts
{
    public static void RunDocuments(string root, Action<bool, string> check)
    {
        System.IO.Directory.CreateDirectory(root);
        foreach (var family in new[] { "odt", "ods", "odp", "epub" })
        {
            SaveFixture(root, "document-original." + family, CreateDocument(family));
            foreach (var compressed in new[] { false, true })
            foreach (var descriptor in new[] { false, true })
            foreach (var populated in descriptor ? new[] { false, true } : new[] { false })
            {
                var source = CreateDocument(family, compression: compressed ? CompressionLevel.Fastest : null,
                    descriptor: descriptor, populatedLocal: populated);
                var original = source.ToArray();
                check(OfficePackageOptimizer.Probe(source, default) == family, "Document package: bounded identity " + family);
                var candidate = OfficePackageOptimizer.Optimize(source, ImageOptimizationPreset.Lossless, default);
                check(candidate.Length < source.Length && source.AsSpan().SequenceEqual(original), "Document package: smaller output and unchanged original " + family);
                Compare(source, candidate, check);
                OfficePackageOptimizer.RequireUnchangedMembers(source, candidate, default);
                var firstBytes = 30 + U16(source, 26) + checked((int)U32(source, 18));
                check(U16(candidate, 8) == 0 && source.AsSpan(0, firstBytes).SequenceEqual(candidate.AsSpan(0, firstBytes)),
                    "Document package: first stored mimetype local header and payload are entirely exact");
                check(OfficePackageOptimizer.Optimize(source, ImageOptimizationPreset.Auto, default).AsSpan().SequenceEqual(candidate),
                    "Document package: conservative Auto equals Lossless " + family);
                check(OfficePackageOptimizer.Optimize(candidate, default).AsSpan().SequenceEqual(candidate), "Document package: repeat is unchanged " + family);
            }
            foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
                RejectDocument(CreateDocument(family), preset, "lossy container preset refusal " + family);
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { OfficePackageOptimizer.Optimize(CreateDocument(family), ImageOptimizationPreset.Lossless, stop.Token); check(false, "Document package cancellation"); }
            catch (OperationCanceledException) { check(true, "Document package: cancellation before parsing " + family); }
            RejectDocument(CreateDocument(family, mutate: parts => { var first = parts[0]; parts.RemoveAt(0); parts.Add(first); }), ImageOptimizationPreset.Lossless, "mimetype must be first");
            RejectDocument(CreateDocument(family, storedMimetype: false, compression: CompressionLevel.Fastest), ImageOptimizationPreset.Lossless, "mimetype cannot be compressed");
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "mimetype", text => text + "\n")), ImageOptimizationPreset.Lossless, "mimetype exact value");
            RejectDocument(CreateDocument(family, mutate: parts => parts.Add(("META-INF/signatures.xml", "<signatures/>"u8.ToArray()))), ImageOptimizationPreset.Lossless, "signature declarations");
        }
        foreach (var family in new[] { "odt", "ods", "odp" })
        {
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "META-INF/manifest.xml", text => text.Replace("full-path=\"content.xml\"", "full-path=\"missing.xml\"", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "ODF missing main manifest part");
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "META-INF/manifest.xml", text => text.Replace("media-type=\"application/octet-stream\"/>", "media-type=\"application/octet-stream\"><manifest:encryption-data/></manifest:file-entry>", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "ODF auxiliary resource encryption");
            RejectDocument(CreateDocument(family, mutate: parts => parts.Add(("Basic/Standard/script.xml", "<script/>"u8.ToArray()))), ImageOptimizationPreset.Lossless, "ODF macro directory");
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "content.xml", text => text.Replace("<office:body>", "<office:body text:protected=\"true\">", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "ODF protection declaration");
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "content.xml", text => text.Replace("<office:body>", "<office:scripts><office:script/></office:scripts><office:body>", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "ODF script declaration");
            RejectDocument(CreateDocument(family, mutate: parts => ReplacePart(parts, "content.xml", text => "<!DOCTYPE x [<!ENTITY x 'bad'>]>" + text)), ImageOptimizationPreset.Lossless, "ODF DTD");
        }
        foreach (var extra in new[] { "META-INF/encryption.xml", "META-INF/rights.xml" })
            RejectDocument(CreateDocument("epub", mutate: parts => parts.Add((extra, "<declaration/>"u8.ToArray()))), ImageOptimizationPreset.Lossless, "EPUB protection inventory");
        foreach (var (oldText, newText, reason) in new[]
        {
            ("idref=\"chapter\"", "idref=\"missing\"", "spine reference"),
            ("href=\"chapter.xhtml\"", "href=\"https://example.invalid/chapter.xhtml\"", "external manifest resource"),
            ("id=\"nav\"", "id=\"chapter\"", "duplicate manifest identity"),
            ("properties=\"nav\"", "properties=\"nav scripted\"", "scripted manifest declaration"),
            ("unique-identifier=\"bookid\"", "unique-identifier=\"missing\"", "missing identifier"),
        }) RejectDocument(CreateDocument("epub", mutate: parts => ReplacePart(parts, "OPS/package.opf", text => text.Replace(oldText, newText, StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "EPUB " + reason);
        RejectDocument(CreateDocument("epub", mutate: parts => ReplacePart(parts, "META-INF/container.xml", text => text.Replace("</rootfiles>", "<rootfile full-path=\"OPS/package.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "EPUB multiple rootfiles");
        RejectDocument(CreateDocument("epub", mutate: parts => ReplacePart(parts, "OPS/chapter.xhtml", text => text.Replace("<body>", "<body><script>void(0)</script>", StringComparison.Ordinal))), ImageOptimizationPreset.Lossless, "EPUB inline scripts");
        RejectDocument(CreateDocument("epub", mutate: parts => parts.RemoveAll(part => part.Name == "OPS/chapter.xhtml")), ImageOptimizationPreset.Lossless, "EPUB missing required chapter");
        var epubRoot = CreateDocument("epub", rootPackage: true);
        check(OfficePackageOptimizer.Probe(epubRoot, default) == "epub", "Document package: root-level EPUB package resolves relative manifest paths");
        Compare(epubRoot, OfficePackageOptimizer.Optimize(epubRoot, default), check);
        var originalOdf = CreateDocument("ods");
        var forgedOdf = CreateDocument("ods", mutate: parts => ReplacePart(parts, "content.xml", text => text.Replace("office:value=\"42\"", "office:value=\"43\"", StringComparison.Ordinal)));
        try { OfficePackageOptimizer.RequireUnchangedMembers(originalOdf, forgedOdf, default); check(false, "ODF changed cell refusal"); }
        catch (InvalidDataException) { check(true, "Document package: recomputed ZIP checksums cannot authorize a changed spreadsheet cell"); }

        void RejectDocument(byte[] source, ImageOptimizationPreset preset, string reason)
        {
            try { OfficePackageOptimizer.Optimize(source, preset, default); check(false, "Document package refuses " + reason); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "Document package refuses " + reason); }
        }
    }

    private static byte[] CreateDocument(string family, CompressionLevel? compression = null, bool descriptor = false,
        bool populatedLocal = false, bool storedMimetype = true, bool rootPackage = false,
        Action<List<(string Name, byte[] Bytes)>>? mutate = null)
    {
        var parts = new List<(string Name, byte[] Bytes)>();
        var prose = string.Concat(Enumerable.Repeat("Exact book and document content, formulas, metadata, and reading order remain intact. ", 240));
        if (family == "epub")
        {
            var prefix = rootPackage ? "" : "OPS/";
            parts.Add(("mimetype", "application/epub+zip"u8.ToArray()));
            parts.Add(("META-INF/container.xml", Encoding.UTF8.GetBytes($"<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\" version=\"1.0\"><rootfiles><rootfile full-path=\"{prefix}package.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>")));
            parts.Add((prefix + "package.opf", Encoding.UTF8.GetBytes("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"bookid\">urn:uuid:7a3ced27-027d-4c24-8b1b-8a90f0bb16f4</dc:identifier><dc:title>Preserved fixture book</dc:title><dc:language>en</dc:language></metadata><manifest><item id=\"chapter\" href=\"chapter.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/><item id=\"style\" href=\"style.css\" media-type=\"text/css\"/></manifest><spine><itemref idref=\"chapter\"/></spine></package>")));
            parts.Add((prefix + "chapter.xhtml", Encoding.UTF8.GetBytes($"<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Preserved chapter</title></head><body><h1>Chapter</h1><p>{prose}</p></body></html>")));
            parts.Add((prefix + "nav.xhtml", Encoding.UTF8.GetBytes("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Contents</title></head><body><nav epub:type=\"toc\"><ol><li><a href=\"chapter.xhtml\">Chapter</a></li></ol></nav></body></html>")));
            parts.Add((prefix + "style.css", "p { color: #182028; }"u8.ToArray()));
        }
        else
        {
            var kind = family switch { "odt" => "text", "ods" => "spreadsheet", _ => "presentation" };
            var mime = "application/vnd.oasis.opendocument." + kind;
            var body = family switch
            {
                "odt" => $"<text:p>{prose}</text:p>",
                "ods" => $"<table:table table:name=\"Sheet1\"><table:table-row><table:table-cell office:value-type=\"float\" office:value=\"42\" table:formula=\"of:=SUM([.A1:.A2])\"><text:p>42</text:p></table:table-cell><table:table-cell office:value-type=\"string\"><text:p>{prose}</text:p></table:table-cell></table:table-row></table:table>",
                _ => $"<draw:page draw:name=\"Slide1\"><draw:frame><draw:text-box><text:p>{prose}</text:p></draw:text-box></draw:frame></draw:page>"
            };
            parts.Add(("mimetype", Encoding.UTF8.GetBytes(mime)));
            parts.Add(("META-INF/manifest.xml", Encoding.UTF8.GetBytes($"<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.3\"><manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"{mime}\"/><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/><manifest:file-entry manifest:full-path=\"resource.bin\" manifest:media-type=\"application/octet-stream\"/></manifest:manifest>")));
            parts.Add(("content.xml", Encoding.UTF8.GetBytes($"<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:of=\"urn:oasis:names:tc:opendocument:xmlns:of:1.2\" office:version=\"1.3\"><office:body><office:{kind}>{body}</office:{kind}></office:body></office:document-content>")));
            parts.Add(("resource.bin", [7, 4, 8, 2, 9]));
        }
        mutate?.Invoke(parts);
        return WriteParts(parts, compression: compression, descriptor: descriptor, populatedLocal: populatedLocal, storedMimetype: storedMimetype);
    }

    private static void ReplacePart(List<(string Name, byte[] Bytes)> parts, string name, Func<string, string> transform)
    {
        var index = parts.FindIndex(part => part.Name == name);
        if (index < 0) throw new InvalidDataException("Fixture part is missing.");
        parts[index] = (name, Encoding.UTF8.GetBytes(transform(Encoding.UTF8.GetString(parts[index].Bytes))));
    }

    public static void RunImages(string root, Action<bool, string> check)
    {
        _ = new ImageAdapter(Path.Combine(root, "office-image-scratch"));
        using var image = new MagickImage(PngFixture.Create(384, 256, 8, 2,
            (x, y) => [(ushort)((40 + x / 3) * 257), (ushort)((35 + y / 2) * 257), (ushort)((60 + (x + y) / 5) * 257)]));
        image.Quality = 100;
        image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", false);
        var jpeg = image.ToByteArray(MagickFormat.Jpeg);
        image.Quality = 100;
        image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
        image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
        image.Settings.SetDefine(MagickFormat.WebP, "method", "0");
        var webp = image.ToByteArray(MagickFormat.WebP);
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
            SaveFixture(root, "office-image-original." + family, CreateImages(family, jpeg, "jpeg"));
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
        foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
        foreach (var descriptor in new[] { false, true })
        foreach (var descriptorStyle in descriptor ? new[] { 0, 1, 2, 3 } : new[] { 0 })
        {
            var source = CreateImages(family, jpeg, "jpeg", descriptor: descriptor,
                signedDescriptor: (descriptorStyle & 1) == 0, populatedLocal: (descriptorStyle & 2) != 0);
            var original = source.ToArray();
            var candidate = OfficePackageOptimizer.Optimize(source, preset, default);
            check(candidate.Length < source.Length && source.AsSpan().SequenceEqual(original), $"Office images: {family} {preset} descriptor={descriptor} smaller copy and original preserved");
            CompareImages(source, candidate, check, true);
            var lossless = OfficePackageOptimizer.Optimize(source, ImageOptimizationPreset.Lossless, default);
            var automatic = OfficePackageOptimizer.Optimize(source, ImageOptimizationPreset.Auto, default);
            CompareImages(source, lossless, check, false);
            check(lossless.AsSpan().SequenceEqual(automatic), "Office images: Auto retains conservative package-only recipe");
            try { OfficePackageOptimizer.RequireUnchangedMembers(source, candidate, default); check(false, "Office image changes require explicit certificates"); }
            catch (InvalidDataException) { check(true, "Office image changes cannot pass the unchanged-member oracle without certificates"); }
        }
        var webpSource = CreateImages("docx", webp, "webp");
        CompareImages(webpSource, OfficePackageOptimizer.Optimize(webpSource, ImageOptimizationPreset.Balanced, default), check, true);
        var png = image.ToByteArray(MagickFormat.Png);
        var unsupported = CreateImages("pptx", png, "png");
        CompareImages(unsupported, OfficePackageOptimizer.Optimize(unsupported, ImageOptimizationPreset.Smallest, default), check, false);
        var mismatched = CreateImages("docx", jpeg, "png");
        CompareImages(mismatched, OfficePackageOptimizer.Optimize(mismatched, ImageOptimizationPreset.Balanced, default), check, false);
        foreach (var mode in new[] { "external", "unreferenced", "shared-package", "encoded-package", "fragment-package", "query-package", "case-package", "unknown-mode", "missing-target-package", "noncanonical-relationship-path" })
        {
            var retained = CreateImages("docx", jpeg, "jpeg", relationshipMode: mode);
            CompareImages(retained, OfficePackageOptimizer.Optimize(retained, ImageOptimizationPreset.Smallest, default), check, false);
        }
        var inactiveWorkbook = Create("xlsx", extraParts:
            [("xl/security.xml", "<workbookProtection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>"u8.ToArray())]);
        check(OfficePackageOptimizer.Probe(inactiveWorkbook, default) == "xlsx", "Office protection: exactly empty workbookProtection is inactive");
        Compare(inactiveWorkbook, OfficePackageOptimizer.Optimize(inactiveWorkbook, default), check);
        foreach (var declaration in new[]
        {
            "<workbookProtection lockStructure=\"1\"/>",
            "<workbookProtection lockStructure=\"false\"/>",
            "<workbookProtection workbookPassword=\"ABCD\"/>",
            "<workbookProtection> </workbookProtection>",
        })
        {
            var protectedWorkbook = Create("xlsx", extraParts:
                [("xl/security.xml", Encoding.UTF8.GetBytes("<root xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + declaration + "</root>"))]);
            try { OfficePackageOptimizer.Optimize(protectedWorkbook, default); check(false, "Office populated workbook protection refusal"); }
            catch (NotSupportedException) { check(true, "Office protection: any nonempty workbookProtection remains conservative refusal"); }
        }
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
        {
            var protectedSource = CreateImages(family, jpeg, "jpeg", protect: true);
            foreach (var preset in new[] { ImageOptimizationPreset.Lossless, ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
            {
                try { OfficePackageOptimizer.Optimize(protectedSource, preset, default); check(false, "Office protected package refusal"); }
                catch (NotSupportedException) { check(true, $"Office images: {family} protection refuses {preset}"); }
            }
        }
        var forged = CreateImages("xlsx", jpeg, "jpeg", alteredFormula: true);
        var control = CreateImages("xlsx", jpeg, "jpeg");
        try { OfficePackageOptimizer.RequireUnchangedMembers(control, forged, default); check(false, "Office forged formula refusal"); }
        catch (InvalidDataException) { check(true, "Office images: recomputed ZIP CRC cannot authorize a changed formula member"); }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { OfficePackageOptimizer.Optimize(control, ImageOptimizationPreset.Smallest, stop.Token); check(false, "Office image cancellation"); }
        catch (OperationCanceledException) { check(true, "Office images: cancellation precedes image encoding"); }
        var manyImages = Enumerable.Range(0, 129).Select(index => ($"word/media/image{index}.jpeg", jpeg)).ToList();
        var manyRelationships = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(Enumerable.Range(0, 129).Select(index => $"<Relationship Id=\"i{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image{index}.jpeg\"/>")) + "</Relationships>";
        manyImages.Add(("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(manyRelationships)));
        var overBudget = Create("docx", extraParts: manyImages, extraTypes: "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
        try { OfficePackageOptimizer.Optimize(overBudget, ImageOptimizationPreset.Balanced, default); check(false, "Office image aggregate budget"); }
        catch (NotSupportedException) { check(true, "Office images: aggregate image-count budget refuses before any encoder runs"); }
    }

    private static void SaveFixture(string root, string name, byte[] bytes)
    {
        var path = Path.Combine(root, name);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("An existing worker fixture differs; use a fresh owned test root.");
        }
        else File.WriteAllBytes(path, bytes);
    }

    private static byte[] CreateImages(string family, byte[] image, string extension, bool descriptor = false,
        bool protect = false, bool alteredFormula = false, bool signedDescriptor = true, bool populatedLocal = false,
        string relationshipMode = "image")
    {
        var folder = family switch { "docx" => "word", "xlsx" => "xl", _ => "ppt" };
        var main = family switch { "docx" => "document", "xlsx" => "workbook", _ => "presentation" };
        var ns = family switch { "docx" => "wordprocessingml", "xlsx" => "spreadsheetml", _ => "presentationml" };
        var protection = family switch { "docx" => "documentProtection", "xlsx" => "sheetProtection", _ => "modifyVerifier" };
        var relationship = relationshipMode == "unreferenced" ? "" :
            $"<Relationship Id=\"img1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image.{extension}\"{(relationshipMode == "external" ? " TargetMode=\"External\"" : "")}/>";
        var packageTarget = relationshipMode switch
        {
            "shared-package" or "unknown-mode" => $"media/image.{extension}",
            "encoded-package" => $"media/%69mage.{extension}",
            "fragment-package" => $"media/image.{extension}#attachment",
            "query-package" => $"media/image.{extension}?attachment=1",
            "case-package" => $"media/IMAGE.{extension.ToUpperInvariant()}",
            "missing-target-package" => "",
            _ => null
        };
        if (packageTarget is not null) relationship +=
            $"<Relationship Id=\"pkg1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/package\" Target=\"{packageTarget}\"{(relationshipMode == "unknown-mode" ? " TargetMode=\"Unknown\"" : "")}/>";
        var parts = new List<(string Name, byte[] Bytes)>
        {
            ($"{folder}/media/image.{extension}", image),
            ($"{folder}/_rels/{main}.xml.rels", Encoding.UTF8.GetBytes($"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{relationship}</Relationships>")),
            ($"{folder}/preserved.xml", Encoding.UTF8.GetBytes($"<root xmlns=\"http://schemas.openxmlformats.org/{ns}/2006/main\"><f>{(alteredFormula ? "A1+2" : "A1+1")}</f><v>42</v>{(protect ? $"<{protection}/>" : "")}</root>"))
        };
        if (relationshipMode == "noncanonical-relationship-path") parts.Add(($"{folder}/unresolved.rels", Encoding.UTF8.GetBytes(
            $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"pkg\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/package\" Target=\"media/image.{extension}\"/></Relationships>")));
        var mime = extension switch { "jpeg" => "image/jpeg", "webp" => "image/webp", _ => "image/png" };
        return Create(family, descriptor: descriptor, signedDescriptor: signedDescriptor, populatedLocal: populatedLocal, extraParts: parts,
            extraTypes: $"<Default Extension=\"{extension}\" ContentType=\"{mime}\"/>");
    }

    private static void CompareImages(byte[] source, byte[] candidate, Action<bool, string> check, bool imageChanges)
    {
        var sourceHeaders = Headers(source, images: true); var candidateHeaders = Headers(candidate, images: true);
        check(sourceHeaders.Count == candidateHeaders.Count && sourceHeaders.Zip(candidateHeaders).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
            "Office images: ZIP names, flags, extraction versions, dates, attributes, and comments preserved");
        using var before = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var after = new ZipArchive(new MemoryStream(candidate), ZipArchiveMode.Read);
        var changedImage = false;
        for (var index = 0; index < before.Entries.Count; index++)
        {
            var left = before.Entries[index]; var right = after.Entries[index];
            using var leftBytes = new MemoryStream(); using var rightBytes = new MemoryStream();
            using (var stream = left.Open()) stream.CopyTo(leftBytes);
            using (var stream = right.Open()) stream.CopyTo(rightBytes);
            var same = leftBytes.ToArray().AsSpan().SequenceEqual(rightBytes.ToArray());
            if (left.FullName.Contains("/media/", StringComparison.Ordinal)) changedImage |= !same;
            else check(same, "Office images: complete nonimage member remains exact: " + left.FullName);
            check(left.FullName == right.FullName && left.Comment == right.Comment && left.LastWriteTime == right.LastWriteTime &&
                left.ExternalAttributes == right.ExternalAttributes, "Office images: independent ZIP reader preserves member metadata");
        }
        check(changedImage == imageChanges, "Office images: only explicitly eligible image payloads change");
    }

    public static void Run(Action<bool, string> check)
    {
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
            foreach (var strict in new[] { false, true })
            {
                var source = Create(family, strict); var original = source.ToArray();
                check(OfficePackageOptimizer.Probe(source, default) == family, $"Office optimization: {family} strict={strict} identity");
                var candidate = OfficePackageOptimizer.Optimize(source, default);
                check(candidate.Length < source.Length && source.AsSpan().SequenceEqual(original), $"Office optimization: {family} smaller copy and unchanged source");
                check(OfficePackageOptimizer.Probe(candidate, default) == family, "Office optimization: independently reparsed candidate identity");
                Compare(source, candidate, check);
                check(OfficePackageOptimizer.Optimize(candidate, default).AsSpan().SequenceEqual(candidate), "Office optimization: repeat has no smaller result");
            }
        var stored = Create("docx", needed: 10);
        check(ReferenceEquals(stored, OfficePackageOptimizer.Optimize(stored, default)), "Office optimization: ZIP 1.0 compatibility keeps exact no-smaller source");
        foreach (var level in new[] { CompressionLevel.Fastest, CompressionLevel.SmallestSize, CompressionLevel.NoCompression })
        {
            var compressed = Create("xlsx", compression: level);
            check(OfficePackageOptimizer.Probe(compressed, default) == "xlsx", "Office optimization: independent .NET Deflate writer framing " + level);
        }
        foreach (var family in new[] { "docx", "xlsx", "pptx" })
        foreach (var signedDescriptor in new[] { false, true })
        foreach (var populatedLocal in new[] { false, true })
        {
            var source = Create(family, descriptor: true, signedDescriptor: signedDescriptor, populatedLocal: populatedLocal);
            var original = source.ToArray();
            var name = $"{family} descriptor signed={signedDescriptor} local-values={populatedLocal}";
            check(OfficePackageOptimizer.Probe(source, default) == family, "Office descriptor: complete source admitted " + name);
            var candidate = OfficePackageOptimizer.Optimize(source, default);
            check(candidate.Length < source.Length && source.AsSpan().SequenceEqual(original), "Office descriptor: smaller output and unchanged source " + name);
            Compare(source, candidate, check);
            CompareDescriptors(source, candidate, signedDescriptor, populatedLocal, check, name);
            check(OfficePackageOptimizer.Probe(candidate, default) == family &&
                OfficePackageOptimizer.Optimize(candidate, default).AsSpan().SequenceEqual(candidate),
                "Office descriptor: reparsed result and exact repeat no-op " + name);
        }
        var descriptorFixture = Create("xlsx", descriptor: true);
        var descriptorRecords = DescriptorRecords(descriptorFixture);
        var firstDescriptor = descriptorRecords[0].Offset;
        foreach (var (offset, reason) in new[] { (4, "CRC"), (8, "compressed extent"), (12, "expanded extent") })
        {
            var mismatch = descriptorFixture.ToArray();
            mismatch[firstDescriptor + offset] ^= 1;
            Reject(mismatch, "descriptor mismatched " + reason);
        }
        var conflictingSignature = descriptorFixture.ToArray();
        conflictingSignature[firstDescriptor] ^= 1;
        Reject(conflictingSignature, "descriptor signature conflicts with its signed layout");
        var unsignedFixture = Create("docx", descriptor: true, signedDescriptor: false);
        var unsignedOffset = DescriptorRecords(unsignedFixture)[0].Offset;
        var signatureShaped = unsignedFixture.ToArray();
        Put32(signatureShaped, unsignedOffset, 0x08074b50);
        Reject(signatureShaped, "unsigned descriptor cannot be reinterpreted as a valid signed record");
        var truncatedSigned = RemoveByte(descriptorFixture, firstDescriptor + 15);
        Reject(truncatedSigned, "truncated signed descriptor before next local record");
        var truncatedUnsigned = RemoveByte(unsignedFixture, unsignedOffset + 11);
        Reject(truncatedUnsigned, "truncated unsigned descriptor before next local record");
        var lastDescriptor = descriptorRecords[^1];
        Reject(RemoveByte(descriptorFixture, lastDescriptor.Offset + lastDescriptor.Bytes.Length - 1), "truncated final descriptor at central directory boundary");
        var localConflict = Create("pptx", descriptor: true, populatedLocal: true);
        localConflict[14] ^= 1;
        Reject(localConflict, "populated descriptor local CRC conflicts with central record");
        Reject(Create("docx", descriptor: true, needed: 10), "descriptor requires ZIP 2.0 extraction version");
        var doubleInterpretation = descriptorFixture.ToArray();
        var firstCentral = Directory(doubleInterpretation);
        // Both interpretations could match only when all four words equal the
        // signature. That declared compressed extent is outside the 64 MiB bound.
        foreach (var offset in new[] { 0, 4, 8, 12 }) Put32(doubleInterpretation, firstDescriptor + offset, 0x08074b50);
        foreach (var offset in new[] { 16, 20, 24 }) Put32(doubleInterpretation, firstCentral + offset, 0x08074b50);
        Reject(doubleInterpretation, "dual-interpretable descriptor declaration exceeds the existing extent budget");
        var signatureCrc = Create("docx", descriptor: true, signedDescriptor: false, signatureCrc: true);
        check(U32(DescriptorRecords(signatureCrc)[^1].Bytes, 0) == 0x08074b50,
            "Office descriptor: authored unsigned CRC equals the optional signature value");
        var signatureCandidate = OfficePackageOptimizer.Optimize(signatureCrc, default);
        check(signatureCandidate.Length < signatureCrc.Length, "Office descriptor: signature-valued CRC retains its unique unsigned interpretation");
        Compare(signatureCrc, signatureCandidate, check);
        CompareDescriptors(signatureCrc, signatureCandidate, false, false, check, "signature-valued CRC");
        Reject(Create("docx", contentType: "application/vnd.ms-word.document.macroEnabled.main+xml"), "macro type");
        Reject(Create("docx", contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"), "template type");
        Reject(Create("pptx", contentType: "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml"), "slideshow type");
        Reject(Create("docx", extraName: "_xmlsignatures/sig1.xml"), "package signature part");
        Reject(Create("docx", relationshipType: "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin"), "signature relationship without its part");
        Reject(Create("xlsx", extraName: "xl/vbaProject.bin"), "VBA project");
        Reject(Create("docx", extraName: "../opaque.bin"), "parent path");
        Reject(Create("docx", extraName: "OPAQUE.BIN"), "case duplicate");
        Reject(Create("docx", external: true), "external main relationship");
        Reject(Create("docx", mainRoot: "workbook"), "conflicting main root");
        Reject(Create("docx", dtd: true), "DTD declaration");
        var sourceFixture = Create("docx");
        Mutate(bytes => bytes[14] ^= 1, "conflicting CRC");
        Mutate(bytes => { bytes[6] |= 1; bytes[Directory(bytes) + 8] |= 1; }, "encrypted flags");
        Mutate(bytes => { bytes[6] |= 8; bytes[Directory(bytes) + 8] |= 8; }, "data descriptor flags");
        Mutate(bytes => Put16(bytes, Directory(bytes) + 30, 4), "unsupported extra fields");
        Mutate(bytes => Put32(bytes, Directory(bytes) + 42, 1), "preamble or local gap");
        Mutate(bytes => Put32(bytes, Directory(bytes) + 24, 65 * 1024 * 1024), "entry inflation budget");
        Mutate(bytes => Put16(bytes, End(bytes) + 10, 4097), "entry count budget");
        Reject([.. sourceFixture, 0], "trailing opaque bytes");
        var damagedPayload = sourceFixture.ToArray(); damagedPayload[30 + U16(damagedPayload, 26)] ^= 1;
        Reject(damagedPayload, "complete member payload CRC");
        foreach (var compressed in new[] { new byte[] { 3 }, new byte[] { 3, 0, 0 }, new byte[] { 7 } })
        {
            try { OfficePackageDeflate.Validate(compressed, 0, default); check(false, "Deflate malformed extent"); }
            catch (InvalidDataException) { check(true, "Office optimization: truncated, trailing, or reserved Deflate framing refuses"); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { OfficePackageOptimizer.Optimize(sourceFixture, cancelled.Token); check(false, "Office optimization cancellation"); }
        catch (OperationCanceledException) { check(true, "Office optimization: cancellation before parsing"); }

        void Mutate(Action<byte[]> mutate, string reason)
        {
            var bytes = sourceFixture.ToArray(); mutate(bytes); Reject(bytes, reason);
        }
        void Reject(byte[] bytes, string reason)
        {
            try { OfficePackageOptimizer.Optimize(bytes, default); check(false, "Office optimization refuses " + reason); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "Office optimization refuses " + reason); }
        }
    }

    // Authored declaration packages, not a rendering or Office-schema oracle.
    internal static byte[] Create(string family, bool strict = false, ushort needed = 20, CompressionLevel? compression = null,
        string? contentType = null, string? extraName = null, bool external = false, string? mainRoot = null, bool dtd = false, string? relationshipType = null,
        bool descriptor = false, bool signedDescriptor = true, bool populatedLocal = false, bool signatureCrc = false,
        IEnumerable<(string Name, byte[] Bytes)>? extraParts = null, string extraTypes = "")
    {
        var format = family switch { "docx" => "wordprocessingml", "xlsx" => "spreadsheetml", _ => "presentationml" };
        var part = family switch { "docx" => "word/document.xml", "xlsx" => "xl/workbook.xml", _ => "ppt/presentation.xml" };
        contentType ??= "application/vnd.openxmlformats-officedocument." + (family switch
            { "docx" => "wordprocessingml.document", "xlsx" => "spreadsheetml.sheet", _ => "presentationml.presentation" }) + ".main+xml";
        mainRoot ??= family switch { "docx" => "document", "xlsx" => "workbook", _ => "presentation" };
        var ns = strict ? $"http://purl.oclc.org/ooxml/{format}/main" : $"http://schemas.openxmlformats.org/{format}/2006/main";
        var relation = relationshipType ?? (strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        var parts = new List<(string Name, byte[] Bytes)>
        {
            ("[Content_Types].xml", Encoding.UTF8.GetBytes($"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{part}\" ContentType=\"{contentType}\"/>{extraTypes}</Types>")),
            ("_rels/.rels", Encoding.UTF8.GetBytes($"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"{relation}\" Target=\"{part}\"{(external ? " TargetMode=\"External\"" : "")}/></Relationships>")),
            (part, Encoding.UTF8.GetBytes((dtd ? "<!DOCTYPE document [<!ENTITY x 'bad'>]>" : "") + $"<{mainRoot} xmlns=\"{ns}\"/>")),
            // These four authored bytes have CRC32 0x08074b50, independently
            // exercising the optional descriptor-signature collision.
            ("opaque.bin", signatureCrc ? [172, 10, 122, 213] : Enumerable.Repeat((byte)91, 128 * 1024).ToArray())
        };
        if (extraName is not null) parts.Add((extraName, [1, 2, 3]));
        if (extraParts is not null) parts.AddRange(extraParts);
        return WriteParts(parts, needed, compression, descriptor, signedDescriptor, populatedLocal);
    }

    private static byte[] WriteParts(List<(string Name, byte[] Bytes)> parts, ushort needed = 20,
        CompressionLevel? compression = null, bool descriptor = false, bool signedDescriptor = true, bool populatedLocal = false,
        bool storedMimetype = true)
    {
        using var output = new MemoryStream(); var directories = new List<byte[]>();
        foreach (var (name, bytes) in parts)
        {
            var hasDescriptor = descriptor && !(name == "mimetype" && storedMimetype);
            var rawName = Encoding.UTF8.GetBytes(name); var payload = bytes; ushort method = 0;
            if (!(name == "mimetype" && storedMimetype) && (compression ?? (hasDescriptor ? CompressionLevel.NoCompression : (CompressionLevel?)null)) is { } level)
            {
                using var encoded = new MemoryStream();
                using (var deflater = new DeflateStream(encoded, level, leaveOpen: true)) deflater.Write(bytes);
                payload = encoded.ToArray(); method = 8;
            }
            var local = new byte[30 + rawName.Length]; var directory = new byte[46 + rawName.Length + 7];
            Put32(local, 0, 0x04034b50); Put16(local, 4, needed); Put16(local, 6, hasDescriptor ? (ushort)0x808 : (ushort)0x800); Put16(local, 8, method);
            Put16(local, 10, 0x1234); Put16(local, 12, 0x5678); Put32(local, 14, Crc(bytes));
            Put32(local, 18, (uint)payload.Length); Put32(local, 22, (uint)bytes.Length); Put16(local, 26, (ushort)rawName.Length); rawName.CopyTo(local, 30);
            Put32(directory, 0, 0x02014b50); Put16(directory, 4, 0x0314); local.AsSpan(4, 26).CopyTo(directory.AsSpan(6));
            Put16(directory, 32, 7); Put16(directory, 36, 1); Put32(directory, 38, 0x81a40020); Put32(directory, 42, (uint)output.Position);
            rawName.CopyTo(directory, 46); "comment"u8.CopyTo(directory.AsSpan(46 + rawName.Length));
            if (hasDescriptor && !populatedLocal) { Put32(local, 14, 0); Put32(local, 18, 0); Put32(local, 22, 0); }
            output.Write(local); output.Write(payload);
            if (hasDescriptor)
            {
                var trailer = new byte[signedDescriptor ? 16 : 12];
                var fieldsOffset = signedDescriptor ? 4 : 0;
                if (signedDescriptor) Put32(trailer, 0, 0x08074b50);
                Put32(trailer, fieldsOffset, Crc(bytes)); Put32(trailer, fieldsOffset + 4, (uint)payload.Length); Put32(trailer, fieldsOffset + 8, (uint)bytes.Length);
                output.Write(trailer);
            }
            directories.Add(directory);
        }
        var start = (uint)output.Position;
        foreach (var directory in directories) output.Write(directory);
        var end = new byte[22 + 7]; Put32(end, 0, 0x06054b50); Put16(end, 8, (ushort)parts.Count); Put16(end, 10, (ushort)parts.Count);
        Put32(end, 12, (uint)output.Position - start); Put32(end, 16, start); Put16(end, 20, 7); "archive"u8.CopyTo(end.AsSpan(22)); output.Write(end);
        return output.ToArray();
    }

    private static void Compare(byte[] source, byte[] candidate, Action<bool, string> check)
    {
        var sourceHeaders = Headers(source); var candidateHeaders = Headers(candidate);
        check(sourceHeaders.Count == candidateHeaders.Count && sourceHeaders.Zip(candidateHeaders).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
            "Office optimization: raw names, flags, versions, local/central dates, attributes, and comments remain exact");
        using var before = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var after = new ZipArchive(new MemoryStream(candidate), ZipArchiveMode.Read);
        check(before.Entries.Count == after.Entries.Count && before.Comment == after.Comment, "Office optimization: entry count and archive comment preserved");
        for (var index = 0; index < before.Entries.Count; index++)
        {
            var left = before.Entries[index]; var right = after.Entries[index];
            using var leftBytes = new MemoryStream(); using var rightBytes = new MemoryStream();
            using (var stream = left.Open()) stream.CopyTo(leftBytes);
            using (var stream = right.Open()) stream.CopyTo(rightBytes);
            check(left.FullName == right.FullName && left.LastWriteTime == right.LastWriteTime && left.ExternalAttributes == right.ExternalAttributes &&
                left.Comment == right.Comment && leftBytes.ToArray().AsSpan().SequenceEqual(rightBytes.ToArray()), "Office optimization: independent ZIP reader verifies payload, order, names, date, attributes, and comment");
        }
    }
    private static List<byte[]> Headers(byte[] bytes, bool images = false)
    {
        var headers = new List<byte[]>(); var position = Directory(bytes);
        for (var index = 0; index < U16(bytes, End(bytes) + 10); index++)
        {
            var nameLength = U16(bytes, position + 28); var length = 46 + nameLength + U16(bytes, position + 30) + U16(bytes, position + 32);
            var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 42)));
            var local = bytes.AsSpan(localOffset, 30 + nameLength).ToArray(); var central = bytes.AsSpan(position, length).ToArray();
            Put16(local, 8, 0); Put32(local, 18, 0); Put16(central, 10, 0); Put32(central, 20, 0); Put32(central, 42, 0);
            if (images) { Put32(local, 14, 0); Put32(local, 22, 0); Put32(central, 16, 0); Put32(central, 24, 0); }
            headers.Add(local); headers.Add(central); position += length;
        }
        return headers;
    }

    private static void CompareDescriptors(byte[] source, byte[] candidate, bool signed, bool populatedLocal, Action<bool, string> check, string name)
    {
        var before = DescriptorRecords(source); var after = DescriptorRecords(candidate);
        var changedCompressed = false;
        for (var index = 0; index < before.Count; index++)
        {
            var left = before[index]; var right = after[index];
            var words = signed ? 4 : 0;
            var same = left.Bytes.Length == (signed ? 16 : 12) && right.Bytes.Length == left.Bytes.Length &&
                (!signed || U32(right.Bytes, 0) == 0x08074b50) &&
                U32(left.Bytes, words) == U32(right.Bytes, words) && U32(right.Bytes, words) == U32(candidate, right.Central + 16) &&
                U32(left.Bytes, words + 8) == U32(right.Bytes, words + 8) && U32(right.Bytes, words + 8) == U32(candidate, right.Central + 24) &&
                U32(right.Bytes, words + 4) == U32(candidate, right.Central + 20) &&
                U16(candidate, right.Local + 4) == 20 && U16(candidate, right.Central + 6) == 20 &&
                U16(candidate, right.Local + 6) == 0x808 && U16(candidate, right.Central + 8) == 0x808 &&
                U16(candidate, right.Local + 8) == 8 && U16(candidate, right.Central + 10) == 8;
            if (populatedLocal)
                same &= U32(candidate, right.Local + 14) == U32(source, left.Local + 14) &&
                    U32(candidate, right.Local + 18) == U32(candidate, right.Central + 20) &&
                    U32(candidate, right.Local + 22) == U32(source, left.Local + 22);
            else same &= U32(candidate, right.Local + 14) == 0 && U32(candidate, right.Local + 18) == 0 && U32(candidate, right.Local + 22) == 0;
            var normalized = right.Bytes.ToArray();
            Put32(normalized, words + 4, U32(left.Bytes, words + 4));
            same &= normalized.AsSpan().SequenceEqual(left.Bytes);
            changedCompressed |= U32(left.Bytes, words + 4) != U32(right.Bytes, words + 4);
            check(same, $"Office descriptor: exact framing, CRC, expanded size, local declaration style, and updated compressed size {name} entry={index}");
        }
        check(changedCompressed, "Office descriptor: recompression changes at least one descriptor extent " + name);
    }

    private static List<(int Local, int Central, int Offset, byte[] Bytes)> DescriptorRecords(byte[] bytes)
    {
        var central = Directory(bytes);
        var entries = new List<(int Local, int Central, int Offset, byte[] Bytes)>();
        var count = U16(bytes, End(bytes) + 10);
        for (var index = 0; index < count; index++)
        {
            var local = checked((int)U32(bytes, central + 42));
            var offset = checked(local + 30 + U16(bytes, local + 26) + U16(bytes, local + 28) + (int)U32(bytes, central + 20));
            var nextCentral = central + 46 + U16(bytes, central + 28) + U16(bytes, central + 30) + U16(bytes, central + 32);
            var boundary = index + 1 == count ? Directory(bytes) : checked((int)U32(bytes, nextCentral + 42));
            entries.Add((local, central, offset, bytes[offset..boundary]));
            central = nextCentral;
        }
        return entries;
    }

    private static byte[] RemoveByte(byte[] bytes, int offset)
    {
        var directory = Directory(bytes);
        var result = bytes[..offset].Concat(bytes[(offset + 1)..]).ToArray();
        var central = directory - 1;
        for (var index = 0; index < U16(result, End(result) + 10); index++)
        {
            var local = U32(result, central + 42);
            if (local > offset) Put32(result, central + 42, local - 1);
            central += 46 + U16(result, central + 28) + U16(result, central + 30) + U16(result, central + 32);
        }
        Put32(result, End(result) + 16, checked((uint)(directory - 1)));
        return result;
    }
    private static uint Crc(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
        }
        return ~crc;
    }
    private static int End(byte[] bytes) => bytes.Length - 29;
    private static int Directory(byte[] bytes) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(End(bytes) + 16)));
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Put16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
