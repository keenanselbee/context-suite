using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static class LegacyDocumentAnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        foreach (var id in new[] { "doc", "xls", "ppt" })
        foreach (var size in new[] { 512, 4096 })
        foreach (var small in new[] { false, true })
        foreach (var fragmented in new[] { false, true })
        {
            var fixture = CompoundFixture.Create(Parts(id), size, small, fragmented);
            var result = await Analyze(fixture.Bytes, "renamed.bin");
            check(result.Identity.FormatId == id && result.Identity.Confidence == IdentificationConfidence.Likely &&
                result.Identity.Basis == IdentificationBasis.Content,
                $"legacy: {id} CFB{(size == 512 ? 3 : 4)} {(small ? "mini" : "normal")} {(fragmented ? "fragmented" : "contiguous")} agrees with binary headers");
            check(result.InspectedBytes <= fixture.Bytes.Length && result.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable &&
                result.Facts.Single(fact => fact.Id == "document.active-content").Availability == FactAvailability.Unavailable,
                "legacy: bounded report does not invent rendered pages or macro absence");
        }
        var encryptedWord = Parts("doc"); CompoundFixture.Set16(encryptedWord[0].Data, 10, 0x9301);
        // Select 1Table consistently while declaring encryption/obfuscation and template status.
        encryptedWord[1] = ("1Table", encryptedWord[1].Data);
        var encrypted = await Analyze(CompoundFixture.Create(encryptedWord).Bytes);
        check(encrypted.Identity.FormatId == "doc" && encrypted.Facts.Single(fact => fact.Id == "document.encrypted-content").Boolean == true &&
            encrypted.Facts.Single(fact => fact.Id == "document.template").Boolean == true,
            "legacy: Word base encryption/template flags are declarations without decryption");
        var pptEncrypted = Parts("ppt"); CompoundFixture.Set32(pptEncrypted[1].Data, 12, 0xf3d1c4df);
        Array.Fill(pptEncrypted[0].Data, (byte)0xe7);
        var protectedSlides = await Analyze(CompoundFixture.Create(pptEncrypted).Bytes);
        check(protectedSlides.Identity.FormatId == "ppt" && protectedSlides.Facts.Single(fact => fact.Id == "document.encrypted-content").Boolean == true,
            "legacy: encrypted presentation is identified from clear current-user declarations without interpreting encrypted records");
        var excel = await Analyze(CompoundFixture.Create(Parts("xls")).Bytes, "wrong.doc");
        check(excel.Identity.FormatId == "xls" && excel.Warnings.Any(warning => warning.Contains("different type")) &&
            excel.Facts.Single(fact => fact.Id == "document.encrypted-content").Availability == FactAvailability.Unavailable,
            "legacy: workbook content overrides misleading extension; encryption remains unavailable");
        var word = CompoundFixture.Create(Parts("doc"));
        var correct = await Analyze(word.Bytes, "right.doc");
        check(correct.Warnings.All(warning => !warning.Contains("container alone")) && correct.Identity.Evidence.All(item => !item.Contains("were not checked")),
            "legacy: successful identification removes obsolete header-only explanations");
        var nested = await Analyze(CompoundFixture.Create(Parts("doc"), nested: true).Bytes);
        check(nested.Identity.FormatId == "ole" && nested.Facts.Single(fact => fact.Id == "compound.root-streams").Integer == 0,
            "legacy: embedded document names cannot identify the outer container");
        var opaque = await Analyze(CompoundFixture.Create(new[] { ("EncryptionInfo", new byte[32]), ("EncryptedPackage", new byte[32]) }).Bytes);
        check(opaque.Identity.FormatId == "ole" && opaque.Facts.Single(fact => fact.Id == "compound.encrypted-package-streams").Boolean == true,
            "legacy: encrypted-package stream names alone cannot establish Word/Excel/PowerPoint identity");
        var big = CompoundFixture.Create(Parts("doc").Concat(new[] { ("Unrelated payload", new byte[8 * 1024 * 1024]) }));
        var bigResult = await Analyze(big.Bytes);
        check(big.FatIds.Length > 109 && bigResult.Identity.FormatId == "doc" && bigResult.InspectedBytes < 200000 &&
            bigResult.Facts.Single(fact => fact.Id == "compound.bytes-read").Integer < 100000,
            "legacy: DIFAT extension is followed while an unrelated 8 MiB stream is skipped");
        var manyMini = CompoundFixture.Create(Enumerable.Range(0, 140).Select(index => ("Dummy " + index, new byte[32])).Concat(Parts("doc")), fragmented: true);
        check(manyMini.MiniFatIds.Length > 1 && (await Analyze(manyMini.Bytes)).Identity.FormatId == "doc",
            "legacy: fragmented directory/mini-FAT/root mini-stream map selected headers beyond the first sector");

        var bad = new List<(string Name, byte[] Bytes)>();
        void Mutate(string name, CompoundFixture fixture, Action<byte[]> edit)
        { var bytes = (byte[])fixture.Bytes.Clone(); edit(bytes); bad.Add((name, bytes)); }
        Mutate("sector shift", word, bytes => CompoundFixture.Set16(bytes, 30, 12));
        Mutate("major version", word, bytes => CompoundFixture.Set16(bytes, 26, 7));
        Mutate("reserved header bytes", word, bytes => bytes[8] = 1);
        Mutate("v3 directory sector count", word, bytes => CompoundFixture.Set32(bytes, 40, 1));
        Mutate("byte order", word, bytes => CompoundFixture.Set16(bytes, 28, 0xfeff));
        Mutate("FAT count budget", word, bytes => CompoundFixture.Set32(bytes, 44, uint.MaxValue));
        Mutate("DIFAT count budget", word, bytes => CompoundFixture.Set32(bytes, 72, uint.MaxValue));
        Mutate("DIFAT terminator", word, bytes => CompoundFixture.Set32(bytes, 68, 0));
        Mutate("FAT marker", word, bytes => CompoundFixture.Set32(bytes, word.FatOffset(word.FatIds[0]), 0xffffffff));
        Mutate("directory cycle", word, bytes => CompoundFixture.Set32(bytes, word.FatOffset(0), 0));
        Mutate("directory points into FAT", word, bytes => CompoundFixture.Set32(bytes, 48, word.FatIds[0]));
        Mutate("directory name length", word, bytes => CompoundFixture.Set16(bytes, word.DirectoryOffsets["WordDocument"] + 64, 65));
        Mutate("directory embedded null", word, bytes => CompoundFixture.Set16(bytes, word.DirectoryOffsets["WordDocument"] + 4, 0));
        Mutate("sibling cycle", word, bytes => CompoundFixture.Set32(bytes, word.DirectoryOffsets["WordDocument"] + 72, 1));
        Mutate("directory pointer range", word, bytes => CompoundFixture.Set32(bytes, word.DirectoryOffsets["WordDocument"] + 68, uint.MaxValue - 8));
        Mutate("stream with children", word, bytes => CompoundFixture.Set32(bytes, word.DirectoryOffsets["WordDocument"] + 76, 2));
        Mutate("mini-FAT cycle", word, bytes => CompoundFixture.Set32(bytes, ((int)word.MiniFatIds[0] + 1) * 512, 0));
        Mutate("mini-sector range", word, bytes => CompoundFixture.Set32(bytes, word.DirectoryOffsets["WordDocument"] + 116, 5000));
        Mutate("mini-FAT count budget", word, bytes => CompoundFixture.Set32(bytes, 64, uint.MaxValue));
        Mutate("mini-stream crosslink", word, bytes => CompoundFixture.Set32(bytes, word.DirectoryOffsets["0Table"] + 116, word.Starts["WordDocument"]));
        Mutate("Word magic", word, bytes => bytes[word.DataOffsets["WordDocument"]] ^= 1);
        Mutate("Word version", word, bytes => CompoundFixture.Set16(bytes, word.DataOffsets["WordDocument"] + 2, 0x0065));
        Mutate("selected table missing", word, bytes => CompoundFixture.Set16(bytes, word.DataOffsets["WordDocument"] + 10, 0x1200));
        var largeWord = CompoundFixture.Create(Parts("doc"), small: false);
        Mutate("normal stream cycle", largeWord, bytes => CompoundFixture.Set32(bytes, largeWord.FatOffset(largeWord.Starts["WordDocument"]), largeWord.Starts["WordDocument"]));
        Mutate("normal stream crosslink", largeWord, bytes => CompoundFixture.Set32(bytes, largeWord.DirectoryOffsets["0Table"] + 116, largeWord.Starts["WordDocument"]));
        var workbook = CompoundFixture.Create(Parts("xls"));
        Mutate("worksheet BOF is not workbook", workbook, bytes => CompoundFixture.Set16(bytes, workbook.DataOffsets["Workbook"] + 6, 0x0010));
        Mutate("unsupported early BIFF", workbook, bytes => CompoundFixture.Set16(bytes, workbook.DataOffsets["Workbook"] + 4, 0x0500));
        var slides = CompoundFixture.Create(Parts("ppt"));
        Mutate("PowerPoint token", slides, bytes => CompoundFixture.Set32(bytes, slides.DataOffsets["Current User"] + 12, 0));
        Mutate("PowerPoint edit offset", slides, bytes => CompoundFixture.Set32(bytes, slides.DataOffsets["Current User"] + 16, uint.MaxValue));
        Mutate("PowerPoint main record", slides, bytes => CompoundFixture.Set16(bytes, slides.DataOffsets["PowerPoint Document"] + 2, 0));
        var version4 = CompoundFixture.Create(Parts("doc"), sectorSize: 4096);
        Mutate("v4 directory sector count", version4, bytes => CompoundFixture.Set32(bytes, 40, 2));
        Mutate("v4 64-bit stream size", version4, bytes => CompoundFixture.Set32(bytes, version4.DirectoryOffsets["WordDocument"] + 124, 1));
        bad.Add(("truncated sector", word.Bytes[..^1]));
        bad.Add(("conflicting root families", CompoundFixture.Create(Parts("doc").Concat(Parts("xls"))).Bytes));
        bad.Add(("case-ambiguous root stream", CompoundFixture.Create(Parts("doc").Concat(new[] { ("worddocument", new byte[32]) })).Bytes));
        foreach (var item in bad)
        {
            var result = await Analyze(item.Bytes);
            check(result.Identity.FormatId == "ole" && result.Warnings.Any(warning => warning.Contains("details are unavailable")), "legacy: fallback for " + item.Name);
        }
        var highSize = (byte[])word.Bytes.Clone(); CompoundFixture.Set32(highSize, word.DirectoryOffsets["WordDocument"] + 124, 0x12345678);
        check((await Analyze(highSize)).Identity.FormatId == "doc", "legacy: v3 high stream-size DWORD is ignored as specified for older writers");
        // Redundant FAT declarations are bounded even when every declared sector fits the file.
        var excessiveReads = new byte[516 * 4096]; version4.Bytes.AsSpan(0, 512).CopyTo(excessiveReads);
        CompoundFixture.Set32(excessiveReads, 44, 513); CompoundFixture.Set32(excessiveReads, 68, 514); CompoundFixture.Set32(excessiveReads, 72, 1);
        for (var index = 0; index < 109; index++) CompoundFixture.Set32(excessiveReads, 76 + index * 4, (uint)index + 1);
        excessiveReads.AsSpan(515 * 4096, 4096).Fill(255);
        for (var index = 0; index < 404; index++) CompoundFixture.Set32(excessiveReads, 515 * 4096 + index * 4, (uint)index + 110);
        CompoundFixture.Set32(excessiveReads, 516 * 4096 - 4, 0xfffffffe);
        using (var tracked = new TrackingStream(excessiveReads))
        {
            var header = HeaderAnalyzer.Analyze("budget.doc", excessiveReads.AsSpan(0, HeaderAnalyzer.MaximumBytes), excessiveReads.Length);
            var result = await LegacyDocumentAnalysis.AddCompoundAsync(header, tracked, CancellationToken.None);
            check(result.Identity.FormatId == "ole" && tracked.BytesRead <= 2 * 1024 * 1024 && tracked.BytesRead > 1024 * 1024,
                "legacy: metadata reads stop at the 2 MiB budget before directory/payload work");
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await Analyze(word.Bytes, token: canceled.Token); check(false, "legacy: cancellation propagates"); }
            catch (OperationCanceledException) { check(true, "legacy: cancellation propagates"); }
        }
        using (var during = new CancellationTokenSource())
        using (var tracked = new TrackingStream(word.Bytes, during))
        {
            var header = HeaderAnalyzer.Analyze("cancel.doc", word.Bytes, word.Bytes.Length);
            try { await LegacyDocumentAnalysis.AddCompoundAsync(header, tracked, during.Token); check(false, "legacy: cancellation during metadata reading propagates"); }
            catch (OperationCanceledException) { check(true, "legacy: cancellation during metadata reading propagates"); }
        }
        using (var partial = new TrackingStream(word.Bytes, maximumRead: 7))
        {
            var header = HeaderAnalyzer.Analyze("partial.doc", word.Bytes, word.Bytes.Length);
            var result = await LegacyDocumentAnalysis.AddCompoundAsync(header, partial, CancellationToken.None);
            check(result.Identity.FormatId == "doc" && result.InspectedBytes == word.Bytes.Length &&
                result.Facts.Single(fact => fact.Id == "compound.bytes-read").Integer == partial.BytesRead,
                "legacy: partial stream reads preserve identity and physical/read-traffic byte accounting");
        }
        var random = new Random(3619); var bounded = true;
        for (var index = 0; index < 1000; index++)
        {
            var bytes = (byte[])word.Bytes.Clone();
            for (var change = 0; change < 3; change++) bytes[random.Next(bytes.Length)] ^= (byte)random.Next(1, 256);
            var result = await Analyze(bytes);
            bounded &= result.InspectedBytes <= bytes.Length;
        }
        check(bounded, "legacy: 1000 deterministic damaged compound files return bounded reports");
        var path = Path.Combine(scratch, "legacy-" + Guid.NewGuid().ToString("N") + ".doc");
        await File.WriteAllBytesAsync(path, word.Bytes); var timestamp = File.GetLastWriteTimeUtc(path);
        var resultRead = await FileAnalysisReader.ReadAsync(path, CancellationToken.None);
        var onlyHeader = await FileAnalysisReader.ReadAsync(path, CancellationToken.None, headerOnly: true);
        check(resultRead.Identity.FormatId == "doc" && onlyHeader.Identity.FormatId == "ole", "legacy: app reader enriches Analyze while header-only command probes remain unchanged");
        check(SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(word.Bytes)) && timestamp == File.GetLastWriteTimeUtc(path),
            "legacy: app reader preserves generated original bytes and timestamp");
    }

    private static async Task<FileAnalysis> Analyze(byte[] bytes, string name = "fixture.doc", CancellationToken token = default)
    {
        var header = HeaderAnalyzer.Analyze(name, bytes.AsSpan(0, Math.Min(bytes.Length, HeaderAnalyzer.MaximumBytes)), bytes.Length);
        using var stream = new MemoryStream(bytes, writable: false);
        return await LegacyDocumentAnalysis.AddCompoundAsync(header, stream, token);
    }
    private sealed class TrackingStream(byte[] bytes, CancellationTokenSource? cancel = null, int maximumRead = int.MaxValue) : MemoryStream(bytes, writable: false)
    {
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumRead)], cancellationToken); BytesRead += count;
            if (BytesRead >= 1024) cancel?.Cancel();
            return count;
        }
    }
    private static (string Name, byte[] Data)[] Parts(string id)
    {
        if (id == "doc")
        {
            var fib = new byte[32]; CompoundFixture.Set16(fib, 0, 0xa5ec); CompoundFixture.Set16(fib, 2, 0x00c1);
            CompoundFixture.Set16(fib, 10, 0x1000); CompoundFixture.Set16(fib, 12, 0x00bf);
            return [("WordDocument", fib), ("0Table", new byte[64])];
        }
        if (id == "xls")
        {
            var bof = new byte[20]; CompoundFixture.Set16(bof, 0, 0x0809); CompoundFixture.Set16(bof, 2, 16);
            CompoundFixture.Set16(bof, 4, 0x0600); CompoundFixture.Set16(bof, 6, 5); CompoundFixture.Set16(bof, 10, 0x07cd);
            return [("Workbook", bof)];
        }
        var document = new byte[64]; CompoundFixture.Set16(document, 0, 0x000f); CompoundFixture.Set16(document, 2, 0x03e8); CompoundFixture.Set32(document, 4, 56);
        var current = new byte[32]; CompoundFixture.Set16(current, 2, 0x0ff6); CompoundFixture.Set32(current, 4, 24);
        CompoundFixture.Set32(current, 8, 20); CompoundFixture.Set32(current, 12, 0xe391c05f);
        CompoundFixture.Set16(current, 22, 0x03f4); current[24] = 3; CompoundFixture.Set32(current, 28, 8);
        return [("PowerPoint Document", document), ("Current User", current)];
    }
}
