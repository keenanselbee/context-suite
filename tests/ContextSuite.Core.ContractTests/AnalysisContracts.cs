using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        await AnalysisIoContracts.RunAsync(scratch, check);
        await AnalysisMappingContracts.RunAsync(scratch, check);
        AudioAnalysisContracts.Run(Path.Combine(scratch, "audio-analysis"), check);
        await InterchangeAudioAnalysisContracts.RunAsync(Path.Combine(scratch, "interchange-audio"), check);
        await FontAnalysisContracts.RunAsync(Path.Combine(scratch, "font-headers"), check);
        var catalog = FileTypeCatalog.Default;
        catalog.Validate();
        check(catalog.Types.Count(type => !type.MimeTypes.IsEmpty) >= 36 &&
            catalog.Get("png").MimeTypes.Select(mime => mime.Value).SequenceEqual(new[] { "image/png", "image/apng" }) &&
            catalog.Get("ogg").MimeTypes.Length == 3 && catalog.Get("docx").MimeTypes.Length == 4,
            "analysis catalog: reviewed MIME descriptions preserve container and document variants");
        var legacyDescription = JsonSerializer.Deserialize<FileTypeDescription>("""
            {"Id":"legacy","Name":"Legacy record","Family":"Data","CommonUses":"An authored compatibility fixture.",
             "Extensions":[".legacy"],"Source":"https://example.test/format"}
            """);
        check(legacyDescription is { MimeTypes.IsEmpty: true }, "analysis catalog: omitted additive MIME metadata defaults to empty");
        var mimeRoundtrip = JsonSerializer.Deserialize<FileTypeDescription>(JsonSerializer.Serialize(catalog.Get("docx") with { FileNames = [] }));
        check(mimeRoundtrip!.MimeTypes.SequenceEqual(catalog.Get("docx").MimeTypes), "analysis catalog: MIME identifiers and provenance survive JSON roundtrip");
        foreach (var invalidMime in new string?[] { null, "", "text", "text/", "/plain", "text//plain", "text/PLAIN",
            "text/*", "text/plain; charset=utf-8", "text/pl ain", "text/pl\r\nain", "text/pl\u00e4in", "text/" + new string('a', 128) })
        {
            var invalid = catalog with { Types = [catalog.Get("text") with { MimeTypes = [new(invalidMime!, "https://example.test/type")] }] };
            try { invalid.Validate(); check(false, "analysis catalog: reject malformed or noncanonical MIME identifier"); }
            catch (InvalidDataException) { check(true, "analysis catalog: reject malformed or noncanonical MIME identifier"); }
        }
        foreach (var invalidMimes in new ImmutableArray<CatalogMimeType>[] { default, [null!],
            [new("text/plain", "file:///local")], [new("text/plain", "https://example.test/type"), new("text/plain", "https://example.test/duplicate")],
            Enumerable.Range(0, 17).Select(index => new CatalogMimeType("text/type" + index, "https://example.test/type")).ToImmutableArray() })
        {
            var invalid = catalog with { Types = [catalog.Get("text") with { MimeTypes = invalidMimes }] };
            try { invalid.Validate(); check(false, "analysis catalog: reject missing arrays, null entries, unreviewable sources, duplicates and excess MIME entries"); }
            catch (InvalidDataException) { check(true, "analysis catalog: reject missing arrays, null entries, unreviewable sources, duplicates and excess MIME entries"); }
        }
        check(catalog.FindByName("REPORT.DOCX").Single().Id == "docx", "analysis catalog: case-insensitive extension hints");
        check(catalog.FindByName("unknown.unregistered").IsEmpty, "analysis catalog: unknown extension has no invented entry");
        check(catalog.Types.Length >= 200 && catalog.Types.Select(type => type.Id).Distinct().Count() == catalog.Types.Length,
            "analysis catalog: at least 200 distinct inventory records, excluding extension aliases");
        foreach (var type in catalog.Types.Where(type => !type.Extensions.IsEmpty || !type.FileNames.IsDefaultOrEmpty))
        {
            var name = !type.FileNames.IsDefaultOrEmpty ? type.FileNames[0] : "fixture" + type.Extensions[0];
            check(catalog.FindByName(name).Any(hint => hint.Id == type.Id), "analysis catalog: reachable description for " + type.Id);
        }
        check(catalog.FindByName("backup.TAR.GZ").Single().Id == "tar-gzip" && catalog.FindByName("backup.gz").Single().Id == "gzip",
            "analysis catalog: compound extension wins over its generic compression suffix");
        check(catalog.FindByName("CMakeLists.txt").Single().Id == "cmake" && catalog.FindByName("Dockerfile").Single().Id == "dockerfile" &&
            catalog.FindByName(".gitignore").Single().Id == "gitignore", "analysis catalog: known filenames precede suffix lookup");
        check(catalog.FindByName("code.h").Select(type => type.Id).Order().SequenceEqual(new[] { "c", "cpp" }) &&
            catalog.FindByName("data.obj").Length == 2 && catalog.FindByName("data.key").Length == 2,
            "analysis catalog: shared extensions retain multiple plausible meanings");
        foreach (var invalid in new[] {
            catalog with { SchemaVersion = 2 }, catalog with { Types = [null!] }, catalog with { Types = [catalog.Types[0], catalog.Types[0]] },
            catalog with { Types = [catalog.Types[0] with { Source = "file:///untrusted" }] },
            catalog with { Types = [catalog.Types[0] with { Extensions = [".dds", ".DDS"] }] } })
        {
            try { invalid.Validate(); check(false, "analysis catalog: reject invalid schema, IDs, provenance or aliases"); }
            catch (InvalidDataException) { check(true, "analysis catalog: reject invalid schema, IDs, provenance or aliases"); }
        }

        FileAnalysis Inspect(byte[] bytes, string name = "unknown", long? length = null) => HeaderAnalyzer.Analyze(name, bytes, length ?? bytes.Length);
        foreach (var (extension, candidates) in new[] {
            (".fit", new[] { "fit-activity", "fits" }), (".pdb", new[] { "pdb", "protein-data-bank" }),
            (".hdf", new[] { "hdf4", "hdf5" }), (".cdf", new[] { "cdf", "netcdf" }),
            (".res", new[] { "godot-resource", "windows-resource" }), (".ase", new[] { "adobe-swatches", "aseprite" }),
            (".heif", new[] { "avif", "heif" }), (".heifs", new[] { "avif", "heif" }),
            (".hif", new[] { "avif", "heif" }),
            (".apk", new[] { "alpine-apk", "apk" }), (".rar", new[] { "java-archive", "rar" }),
            (".ts", new[] { "mpeg-ts", "typescript" }), (".mts", new[] { "mpeg-ts", "typescript" }) })
        {
            var name = "fixture" + extension.ToUpperInvariant();
            check(catalog.FindByName(name).Select(type => type.Id).Order().SequenceEqual(candidates),
                "analysis catalog: distinct common meanings retained for " + extension);
            var undecoded = Inspect([0, 1, 2, 255], name);
            check(undecoded.Identity is { FormatId: null, Confidence: IdentificationConfidence.Ambiguous } &&
                undecoded.FilenameHints.Select(type => type.Id).Order().SequenceEqual(candidates),
                "analysis: undecoded content cannot choose a meaning for " + extension);
            var identified = Inspect("%PDF-1.7\n"u8.ToArray(), name);
            check(identified.Identity is { FormatId: "pdf", Basis: IdentificationBasis.Content } &&
                identified.Warnings.Any(warning => warning.Contains("different type")),
                "analysis: content evidence overrides shared filename meanings for " + extension);
        }
        foreach (var name in new[] { "application.APK", "adapter.RAR" })
        {
            var container = Inspect("PK\x03\x04"u8.ToArray(), name);
            check(container.Identity is { FormatId: "zip", Basis: IdentificationBasis.Content,
                Confidence: IdentificationConfidence.Likely } && container.FilenameHints.Length == 2 &&
                container.Warnings.Any(warning => warning.Contains("does not confirm")),
                "analysis: ZIP signature does not certify the package meaning of " + name);
        }
        check(Inspect([0, 1, 2, 255], "sequence.HEICS").Identity is
            { FormatId: "heif", Basis: IdentificationBasis.Filename, Confidence: IdentificationConfidence.Likely },
            "analysis: HEIC sequence suffix remains a qualified family hint without frame or codec inference");
        check(Inspect("OggS"u8.ToArray(), "speech.SPX").Identity is
            { FormatId: "ogg", Basis: IdentificationBasis.Content, Confidence: IdentificationConfidence.Likely },
            "analysis: SPX with an Ogg marker identifies the container without claiming a Speex codec");
        check(Inspect("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head/><body/></html>"u8.ToArray(), "page.XHT").Identity is
            { FormatId: "xml", Basis: IdentificationBasis.Content, Confidence: IdentificationConfidence.Confirmed },
            "analysis: XHT filename cannot promote generic XML parsing into validated XHTML");
        check(Inspect("HEADER    AUTHORED TEXT ONLY\n"u8.ToArray(), "model.pdb").Identity is
            { FormatId: "protein-data-bank", Basis: IdentificationBasis.Filename, Confidence: IdentificationConfidence.Likely },
            "analysis: readable PDB text is a qualified molecular filename hint, not a validated structure");
        var sourceText = "print('Hello')\n"u8.ToArray();
        foreach (var (name, id) in new[] { ("script.py", "python"), ("Dockerfile", "dockerfile"),
            ("data.json", "json"), ("CMakeLists.txt", "cmake"), ("README.md", "markdown"), ("app.ts", "typescript"),
            ("module.MTS", "typescript"), ("module.CTS", "typescript"), ("types.D.MTS", "typescript"), ("types.D.CTS", "typescript"),
            ("GEMFILE", "ruby"), ("RAKEFILE", "ruby"), ("module.mjs", "javascript"), ("module.cjs", "javascript"),
            ("component.jsx", "javascript"), ("window.pyw", "python"), ("interface.pyi", "python") })
        {
            var hinted = Inspect(sourceText, name);
            check(hinted.Identity.FormatId == id && hinted.Identity.Basis == IdentificationBasis.Filename &&
                hinted.Identity.Confidence == IdentificationConfidence.Likely && hinted.Warnings.IsEmpty,
                "analysis: readable text provides a qualified filename hint, not a false parse, for " + name);
        }
        check(catalog.FindByName("types.D.MTS").Single().Id == "typescript" && catalog.FindByName("types.D.CTS").Single().Id == "typescript",
            "analysis catalog: compound module declarations take precedence over shorter suffix hints");
        check(new[] { "Gemfile.lock", "Gemfile.txt", "another.Gemfile", "Rakefile.backup" }.All(name =>
            catalog.FindByName(name).All(type => type.Id != "ruby")),
            "analysis catalog: Ruby build and dependency names require an exact filename");
        foreach (var name in new[] { "module.CTS", "types.D.MTS", "types.D.CTS", "GEMFILE", "RAKEFILE" })
            check(Inspect("%PDF-1.7\n"u8.ToArray(), name).Identity is { FormatId: "pdf", Basis: IdentificationBasis.Content },
                "analysis: recognized content overrides added module or exact-name hints: " + name);
        check(Inspect(sourceText, "code.h").Identity.Confidence == IdentificationConfidence.Ambiguous &&
            Inspect(sourceText, "script.m").Identity.Confidence == IdentificationConfidence.Ambiguous,
            "analysis: text alone cannot choose between shared C/C++ or MATLAB/Objective-C extensions");
        check(Inspect([0, 1, 2, 255], "movie.ts").Identity.Confidence == IdentificationConfidence.Ambiguous,
            "analysis: undecoded binary cannot select TypeScript or MPEG transport stream from .ts");
        var binaryHint = Inspect([0, 1, 2, 255], "database.sqlite");
        check(binaryHint.Identity is { FormatId: "sqlite", Basis: IdentificationBasis.Filename } && !binaryHint.Warnings.IsEmpty,
            "analysis: unprobed database is a filename hint, never validated database content");
        var json = Inspect("{\"items\":[1,2],\"enabled\":true}"u8.ToArray(), "data.unknown");
        check(json.Identity is { FormatId: "json", Basis: IdentificationBasis.Content, Confidence: IdentificationConfidence.Confirmed } &&
            json.Facts.Single(fact => fact.Id == "json.properties").Integer == 2,
            "analysis: complete bounded JSON object provides structural facts independently of extension");
        check(Inspect("[1,2,3]"u8.ToArray()).Facts.Single(fact => fact.Id == "json.items").Integer == 3,
            "analysis: JSON array item count is parsed, not estimated from delimiters");
        check(Inspect("\"a quoted sentence\""u8.ToArray()).Identity.FormatId == "text" &&
            Inspect("{\"x\":1,}"u8.ToArray(), "bad.json").Identity.Basis == IdentificationBasis.Filename,
            "analysis: scalar text and malformed JSON do not acquire confirmed JSON identity");
        check(Inspect("{}"u8.ToArray(), length: 100).Identity.FormatId == "text",
            "analysis: a valid JSON prefix cannot certify the unread remainder");
        var nestedJson = Encoding.UTF8.GetBytes(new string('[', 40) + "0" + new string(']', 40));
        check(Inspect(nestedJson, "deep.json").Identity.Basis == IdentificationBasis.Filename,
            "analysis: JSON nesting budget retains useful fallback");
        var xml = Inspect("<?xml version=\"1.0\"?><root xmlns=\"urn:fixture\"><a/><b/></root>"u8.ToArray());
        check(xml.Identity is { FormatId: "xml", Basis: IdentificationBasis.Content, Confidence: IdentificationConfidence.Confirmed } &&
            xml.Facts.Single(fact => fact.Id == "xml.root").Text == "root" &&
            xml.Facts.Single(fact => fact.Id == "xml.namespace").Text == "urn:fixture" &&
            xml.Facts.Single(fact => fact.Id == "xml.elements").Integer == 3,
            "analysis: bounded XML reads names and counts without application execution");
        foreach (var document in new[] { "<!DOCTYPE r SYSTEM 'file:///must-not-read'><r/>",
            "<!DOCTYPE r [<!ENTITY a 'expanded'>]><r>&a;</r>", "<?xml version='1.0'?><root>",
            "<?xml version='1.0'?>" + string.Concat(Enumerable.Repeat("<r>", 40)) + string.Concat(Enumerable.Repeat("</r>", 40)) })
        {
            var rejected = Inspect(Encoding.UTF8.GetBytes(document), "fixture.xml");
            check(rejected.Identity.Basis == IdentificationBasis.Filename && !rejected.Facts.Any(fact => fact.Id == "xml.elements") &&
                rejected.Warnings.Any(warning => warning.Contains("External references are never resolved")),
                "analysis: DTDs, malformed XML and excessive nesting retain local-only fallback");
        }
        // Authored signature fixtures exercise identification only, not validity of complete media.
        var signatures = new (byte[] Bytes, string Id)[] {
            ("DDS "u8.ToArray(), "dds"), ([137, 80, 78, 71, 13, 10, 26, 10], "png"),
            ("%PDF-1.7\n"u8.ToArray(), "pdf"), ("PK\x03\x04"u8.ToArray(), "zip"),
            ("PK\x05\x06"u8.ToArray(), "zip"), ("PK\x07\x08"u8.ToArray(), "zip"),
            ([0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1], "ole"),
            ("MZ"u8.ToArray(), "mz"), ("RIFF\0\0\0\0WAVE"u8.ToArray(), "wave"),
            ("fLaC"u8.ToArray(), "flac"), ("OggS"u8.ToArray(), "ogg") };
        foreach (var fixture in signatures)
        {
            var result = Inspect(fixture.Bytes);
            check(result.Identity.FormatId == fixture.Id && result.Identity.Confidence == IdentificationConfidence.Likely,
                "analysis: signature-only evidence remains qualified for " + fixture.Id);
            for (var length = 0; length < fixture.Bytes.Length; length++)
            {
                result = Inspect(fixture.Bytes[..length]);
                check(result.Identity.Confidence != IdentificationConfidence.Confirmed, "analysis: truncated signature never certifies " + fixture.Id);
            }
            var misleading = fixture.Bytes.ToArray(); misleading[0] ^= 0x80;
            check(Inspect(misleading).Identity.FormatId != fixture.Id, "analysis: altered signature is not accepted as " + fixture.Id);
        }
        check(Inspect([0, 1, 2, 3, 255]).Identity.Confidence == IdentificationConfidence.Unknown,
            "analysis: unknown binary remains useful with unknown identity");
        check(Inspect([], "empty.pdf").Identity is { FormatId: null, Name: "Empty file" },
            "analysis: empty file does not become a PDF from its name");
        check(Inspect([0, 1, 2, 255], "report.docx").Warnings.Any(warning => warning.Contains("only on the filename")),
            "analysis: extension-only document identity is explicitly unverified");
        var packaged = Inspect("PK\x03\x04"u8.ToArray(), "report.docx");
        check(packaged.Identity.FormatId == "zip" && packaged.Warnings.Any(warning => warning.Contains("does not confirm")),
            "analysis: ZIP does not establish Word document identity");
        var renamed = Inspect("%PDF-1.7\n"u8.ToArray(), "image.png");
        check(renamed.Identity.FormatId == "pdf" && renamed.Warnings.Any(warning => warning.Contains("different type")),
            "analysis: content evidence wins over a misleading filename");
        check(renamed.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable &&
            renamed.Facts.Single(fact => fact.Id == "document.encryption").Availability == FactAvailability.Unavailable,
            "analysis: PDF header never guesses page count or encryption");
        check(Inspect("%PDF-"u8.ToArray()).Warnings.Length == 1, "analysis: incomplete PDF version is explained");
        foreach (var encoder in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true),
            new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true) })
        {
            var bytes = encoder.GetPreamble().Concat(encoder.GetBytes("Hello, 世界.\n")).ToArray();
            check(Inspect(bytes).Identity.FormatId == "text", "analysis: Unicode text detected for " + encoder.WebName);
        }
        check(Inspect([0xc0, 0xaf]).Identity.FormatId is null && Inspect("A\0B"u8.ToArray()).Identity.FormatId is null,
            "analysis: invalid UTF-8 and embedded nulls are not declared text");
        check(Inspect([0x41, 0xe2, 0x82], length: 4).Identity.FormatId == "text" &&
            Inspect([0x41, 0xe2, 0x82]).Identity.FormatId is null,
            "analysis: partial UTF-8 at the read limit differs from truncated file content");
        var png = new byte[33]; signatures[1].Bytes.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 300); BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 200);
        png[24] = 8; png[25] = 2;
        var image = Inspect(png);
        check(image.Facts.Single(fact => fact.Id == "image.width").Integer == 300 &&
            image.Facts.Single(fact => fact.Id == "image.height").Integer == 200,
            "analysis: PNG declared dimensions use big-endian values");
        check(image.Facts.Single(fact => fact.Id == "image.transparency").Availability == FactAvailability.Unavailable,
            "analysis: truecolor PNG may still contain transparency outside its header");
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.MaxValue);
        check(Inspect(png).Warnings.Length == 1, "analysis: oversized PNG dimensions are not promoted to valid facts");
        var pe = new byte[88]; "MZ"u8.CopyTo(pe); BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(60), 64);
        "PE\0\0"u8.CopyTo(pe.AsSpan(64)); BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(68), 0x8664);
        check(Inspect(pe).Identity.FormatId == "pe" && Inspect(pe).Facts.Single(fact => fact.Id == "pe.machine").Integer == 0x8664,
            "analysis: PE header read at declared offset without executing");
        BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(60), uint.MaxValue);
        check(Inspect(pe).Identity.FormatId == "mz", "analysis: hostile PE offset stays bounded and does not imply Windows PE");
        try { Inspect(new byte[HeaderAnalyzer.MaximumBytes + 1]); check(false, "analysis: parser input budget"); }
        catch (ArgumentOutOfRangeException) { check(true, "analysis: parser input budget"); }

        var root = Path.Combine(scratch, "analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var binary = Path.Combine(root, "unknown.data");
        var pdf = Path.Combine(root, "document.pdf");
        var locked = Path.Combine(root, "locked.txt");
        var text = Path.Combine(root, "notes.txt");
        await File.WriteAllBytesAsync(binary, [0, 1, 2, 3, 255]);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.7\n"u8.ToArray());
        await File.WriteAllTextAsync(locked, "Locked fixture");
        await File.WriteAllTextAsync(text, "Readable notes");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(binary));
        var timestamp = File.GetLastWriteTimeUtc(binary);
        await using (var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        await using (var worker = new WorkerClient(Path.Combine(root, "must-not-start.exe")))
        await using (var view = new MainViewModel(worker, trial: new ForbiddenAccess()))
        {
            view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [locked, binary, pdf, text]));
            await view.WaitForIdleAsync();
            check(view.Rows.Select(row => row.Result.State).SequenceEqual(new[] { OperationState.Failed, OperationState.Succeeded,
                OperationState.Succeeded, OperationState.Succeeded }), "analysis: locked input does not stop mixed batch or access licensing/worker");
            check(view.Rows[1].Analysis?.Identity.Confidence == IdentificationConfidence.Unknown &&
                view.Rows[2].Analysis?.Identity.FormatId == "pdf" && view.Rows[3].Analysis?.Identity.FormatId == "text",
                "analysis: unknown binary, PDF and text share the actual application batch");
            check(view.Rows.All(row => row.Result.Publication is null && !row.HasOutput) && !view.CanRetry,
                "analysis: read-only results never publish or enter transformation retry");
            check(view.Rows[0].Status.Contains("Another program is using") && view.Rows[0].Status.Contains("Close it there"),
                "analysis: actual sharing violation explains how to retry");
            check(view.Rows[2].AnalysisSummary.Contains("Commonly used for:") && view.Rows[2].AnalysisDetails.Contains("not parsed"),
                "analysis: ordinary summary and evidence details remain separate");
            check(view.Rows[2].AnalysisDetails.Contains("Catalog MIME types (descriptive; exact variant not determined): application/pdf") &&
                !view.Rows[2].AnalysisSummary.Contains("MIME") && !view.Rows[1].AnalysisDetails.Contains("MIME"),
                "analysis: MIME descriptions stay in technical details and unknown files receive no invented type");
        }
        await using (var worker = new WorkerClient(Path.Combine(root, "must-not-start.exe")))
        await using (var view = new MainViewModel(worker, trial: new ForbiddenAccess()))
        {
            var missing = Path.Combine(root, "disappeared.bin");
            var request = new OperationRequest(Guid.NewGuid(), "analyze", "open-details", [missing, binary, root, pdf]);
            check(view.Admit(request).Accepted, "analysis admission: unavailable members do not reject a mixed selection");
            check(view.Admit(request).Accepted && view.Rows.Count == 4, "analysis admission: duplicate request does not duplicate unavailable rows");
            await view.WaitForIdleAsync();
            check(view.Rows.Select(row => row.Result.State).SequenceEqual(new[] { OperationState.Failed, OperationState.Succeeded,
                OperationState.Unsupported, OperationState.Succeeded }), "analysis admission: missing and directory rows do not stop valid results");
            check(view.Rows.All(row => row.Result.Publication is null && !row.HasOutput) && !view.CanRetry,
                "analysis admission: unavailable rows grant no publication or transformation retry");
            check(view.Rows[0].Status.Contains("moved or deleted") && view.Rows[2].Status.Contains("regular file"),
                "analysis admission: unavailable and non-file rows explain the next action");
        }
        var denied = new FileInfo(Path.Combine(root, "read-denied.txt"));
        await File.WriteAllTextAsync(denied.FullName, "Authored access-denial fixture");
        var deniedHash = SHA256.HashData(await File.ReadAllBytesAsync(denied.FullName));
        var deniedTime = File.GetLastWriteTimeUtc(denied.FullName);
        var originalSecurity = denied.GetAccessControl();
        var restrictedSecurity = denied.GetAccessControl();
        using (var identity = WindowsIdentity.GetCurrent())
            restrictedSecurity.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            denied.SetAccessControl(restrictedSecurity);
            await using var worker = new WorkerClient(Path.Combine(root, "must-not-start.exe"));
            await using var view = new MainViewModel(worker, trial: new ForbiddenAccess());
            check(view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [denied.FullName, text])).Accepted,
                "analysis: read-denied member is admitted for per-file inspection");
            await view.WaitForIdleAsync();
            check(view.Rows[0].Result.State == OperationState.Failed && view.Rows[0].Status.Contains("does not have permission") &&
                view.Rows[1].Result.State == OperationState.Succeeded && view.Rows.All(row => !row.HasOutput),
                "analysis: actual read denial has specific guidance while the next file succeeds");
        }
        finally
        {
            var restoredSecurity = new FileSecurity();
            restoredSecurity.SetSecurityDescriptorBinaryForm(originalSecurity.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            denied.SetAccessControl(restoredSecurity);
        }
        var restoredHash = SHA256.HashData(await File.ReadAllBytesAsync(denied.FullName));
        check(deniedHash.SequenceEqual(restoredHash) && deniedTime == File.GetLastWriteTimeUtc(denied.FullName) &&
            denied.GetAccessControl().GetSecurityDescriptorBinaryForm().SequenceEqual(originalSecurity.GetSecurityDescriptorBinaryForm()),
            "analysis: disposable denial fixture retains contents/time and its original access rules");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(binary));
        check(before.SequenceEqual(after) && timestamp == File.GetLastWriteTimeUtc(binary),
            "analysis: source bytes and write timestamp unchanged");
        var large = Path.Combine(root, "large.bin");
        await using (var stream = new FileStream(large, FileMode.CreateNew, FileAccess.Write)) stream.SetLength(4 * 1024 * 1024);
        var largeResult = await FileAnalysisReader.ReadAsync(large, CancellationToken.None);
        check(largeResult.FileBytes == 4 * 1024 * 1024 && largeResult.InspectedBytes == HeaderAnalyzer.MaximumBytes,
            "analysis: large-file summary reads only the bounded prefix");
        var audioPath = Path.Combine(root, "large.mp3");
        await using (var stream = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write))
        { stream.Write("ID3"u8); stream.SetLength(4 * 1024 * 1024); }
        var audioFacts = ContextSuite.Core.Audio.AudioProbeParser.Parse("{\"streams\":[{\"index\":0,\"codec_type\":\"audio\",\"codec_name\":\"mp3\",\"sample_rate\":\"48000\"}],\"format\":{\"format_name\":\"mp3\"}}"u8.ToArray());
        var enriched = await FileAnalysisReader.ReadAsync(audioPath, CancellationToken.None, (snapshot, token) =>
        {
            check(snapshot.Length == ContextSuite.Core.Transport.WorkerCommand.MaximumAudioProbeBytes,
                "analysis: deeper audio snapshot remains bounded on larger files");
            try { using var writer = new FileStream(audioPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); check(false, "analysis: source lease held during audio probe"); }
            catch (IOException) { check(true, "analysis: source lease held during audio probe"); }
            return Task.FromResult(audioFacts);
        });
        check(enriched.Identity.Basis == IdentificationBasis.Content && enriched.Facts.Any(fact => fact.Id == "audio.probe.stream.0.rate") &&
            enriched.Warnings.All(warning => !warning.Contains("only on the filename")),
            "analysis: bounded audio evidence replaces an unconfirmed filename hint");
        var fallback = await FileAnalysisReader.ReadAsync(audioPath, CancellationToken.None, (_, _) =>
            Task.FromException<ContextSuite.Core.Audio.AudioProbeFacts>(new InvalidDataException("Test probe failure")));
        check(fallback.Identity.FormatId == "mp3" && fallback.Warnings.Any(warning => warning.Contains("Deeper audio")),
            "analysis: probe failure preserves basic information instead of failing the file");
        var routing = await FileAnalysisReader.ReadAsync(audioPath, CancellationToken.None,
            (_, _) => throw new InvalidOperationException("Header routing must not invoke deeper media parsing."), headerOnly: true);
        check(routing.Facts.All(fact => !fact.Id.StartsWith("audio.probe.")), "analysis: header-only routing skips optional native probes");
        var empty = Path.Combine(root, "empty"); await File.WriteAllBytesAsync(empty, []);
        check((await FileAnalysisReader.ReadAsync(empty, CancellationToken.None)).Identity.Name == "Empty file",
            "analysis: zero-byte file survives real reader");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await FileAnalysisReader.ReadAsync(binary, cancelled.Token); check(false, "analysis: pre-cancellation"); }
            catch (OperationCanceledException) { check(true, "analysis: pre-cancellation"); }
        }
        File.SetAttributes(binary, FileAttributes.Offline);
        try
        {
            try { await FileAnalysisReader.ReadAsync(binary, CancellationToken.None); check(false, "analysis: offline-marked file is not opened"); }
            catch (InvalidDataException) { check(true, "analysis: offline-marked file is not opened"); }
        }
        finally { File.SetAttributes(binary, FileAttributes.Normal); }
        // Retain authored fixtures for reproducible inspection inside repository scratch.
    }

    private sealed class ForbiddenAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Analyze accessed licensing.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted conversion.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted optimization.");
    }
}
