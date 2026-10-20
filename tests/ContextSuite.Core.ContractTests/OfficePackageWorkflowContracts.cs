using System.Buffers.Binary;
using System.IO.Compression;
using ContextSuite.Core.ContractTests;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

internal static class OfficePackageWorkflowContracts
{
    public static async Task RunRepresentativeAsync(string scratch, string executable, string sourcePath, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var retainedSourcePath = sourcePath;
        var original = await File.ReadAllBytesAsync(sourcePath); var originalHash = Convert.ToHexString(SHA256.HashData(original));
        var written = File.GetLastWriteTimeUtc(sourcePath);
        var inputDirectory = Path.Combine(scratch, "input"); Directory.CreateDirectory(inputDirectory);
        sourcePath = Path.Combine(inputDirectory, Path.GetFileName(retainedSourcePath));
        File.Copy(retainedSourcePath, sourcePath, overwrite: false);
        var inputWritten = File.GetLastWriteTimeUtc(sourcePath);
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        var source = await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), sourcePath), default);
        check(source.Format == "xlsx" && source.Sha256 == originalHash && source.FileBytes == original.Length,
            "Representative Office package: real worker admits complete LibreOffice-produced XLSX");
        var access = new Access(new LocalTrialStore(Path.Combine(scratch, "trial", "trial.json")));
        var records = Path.Combine(scratch, "records");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        var batch = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [source], new("optimize", new(ReplaceOriginals: true, OutputDirectory: scratch))).Confirm();
        var execution = await new OfficePackageOptimizationExecutor(worker, publisher).ExecuteAdmittedAsync(batch,
            await access.AdmitOptimizationAsync(batch, default), null, default);
        var result = execution.Results.Single();
        check(access.Admissions == 1 && result.State is OperationState.Succeeded or OperationState.Unchanged,
            "Representative Office package: one admitted execution returns validated smaller copy or exact no-op");
        var outputPath = result.Publication?.OutputPath ?? sourcePath;
        var optimized = await File.ReadAllBytesAsync(outputPath);
        check(optimized.Length <= original.Length && (result.State == OperationState.Unchanged ? optimized.AsSpan().SequenceEqual(original) :
            optimized.Length < original.Length && result.Publication?.Outcome == PublicationOutcome.CopyCreated && outputPath != sourcePath),
            "Representative Office package: output obeys smaller-only copy publication and no-op rules");
        ComparePayloads(original, optimized, check);
        var reopened = await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), outputPath), default);
        check(reopened.Format == "xlsx" && reopened.Sha256 == Convert.ToHexString(SHA256.HashData(optimized)),
            "Representative Office package: independently reopened output passes complete worker ZIP/XML admission");
        var filesBeforeRepeat = Directory.GetFiles(scratch, "*.xlsx").Length;
        var repeatedBatch = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [reopened], new("optimize", new(OutputDirectory: scratch))).Confirm();
        var repeated = await new OfficePackageOptimizationExecutor(worker, publisher).ExecuteAdmittedAsync(repeatedBatch,
            await access.AdmitOptimizationAsync(repeatedBatch, default), null, default);
        check(repeated.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(scratch, "*.xlsx").Length == filesBeforeRepeat,
            "Representative Office package: repeated output remains unchanged without a duplicate");
        check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) == originalHash && File.GetLastWriteTimeUtc(sourcePath) == inputWritten &&
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(retainedSourcePath))) == originalHash && File.GetLastWriteTimeUtc(retainedSourcePath) == written,
            "Representative Office package: copied input and retained backup bytes and timestamps remain unchanged");
        check(Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp").Any(),
            "Representative Office package: no owned reservation or publication journal remains");
        using var archive = new ZipArchive(new MemoryStream(optimized), ZipArchiveMode.Read);
        var inventory = archive.Entries.Select(entry =>
        {
            using var stream = entry.Open();
            return new { entry.FullName, entry.Length, entry.CompressedLength, entry.LastWriteTime, entry.ExternalAttributes,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
        }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(scratch, "representative-office-package.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Provenance = retainedSourcePath, Source = sourcePath, Output = outputPath, SourceSha256 = originalHash,
            OutputSha256 = reopened.Sha256, SourceBytes = original.Length, OutputBytes = optimized.Length,
            result.State, result.EngineIdentity, Entries = inventory,
            Scope = "Existing LibreOffice-produced authored workbook; exact expanded package preservation and complete reopen, without launching Office or asserting rendered fidelity."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static async Task RunAsync(string scratch, string? executable, Action<bool, string> check)
    {
        var source = new OfficePackageSource(Guid.NewGuid(), Path.Combine(scratch, "source.docx"), new('A', 64), 1024, "docx");
        var settings = new BatchSettings("optimize", new(ReplaceOriginals: true));
        var plan = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [source], settings);
        check(plan.Confirm().Plan.Settings == settings, "Office package: immutable overwrite preference can confirm copy-only work");
        Refuses(() => OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [source, source], settings), "duplicate source identity");
        Refuses(() => OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [source], new("convert", new())), "conversion settings");
        Refuses(() => (source with { Format = "zip" }).Validate(), "unreviewed package family");
        Refuses(() => (source with { Path = Path.Combine(scratch, "wrong.xlsx") }).Validate(), "mismatched family extension");
        Refuses(() => (source with { FileBytes = OfficePackageSource.MaximumFileBytes + 1L }).Validate(), "source byte budget");
        Refuses(() => (source with { Sha256 = new('Z', 64) }).Validate(), "invalid source hash");
        var work = new OfficePackageWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"));
        new WorkerCommand(1, Guid.NewGuid(), "office-package-probe", PackageProbe: new(source.ItemId, source.Path)).Validate();
        new WorkerCommand(1, Guid.NewGuid(), "office-package-optimize", PackageWork: work).Validate();
        check(true, "Office package: typed probe and optimization protocol validates");
        Refuses(() => (work with { TemporaryPath = source.Path }).Validate(), "source as output reservation");
        Refuses(() => (work with { Policy = "unreviewed" }).Validate(), "unreviewed preservation policy");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "office-package-probe", PackageProbe: new(source.ItemId, source.Path), PackageWork: work).Validate(), "probe mixed with optimization");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "office-package-optimize", PackageWork: work, PdfBytes: [1]).Validate(), "unrelated media payload");
        Refuses(() => new WorkerCommand(1, Guid.NewGuid(), "capabilities", PackageWork: work).Validate(), "unexpected optimization payload");
        var trialPath = Path.Combine(scratch, "public-trial", "trial.json");
        IOperationAccess trial = new LocalTrialStore(trialPath);
        check(!File.Exists(trialPath), "Office package: planning and protocol validation do not start trial");
        var admission = await trial.AdmitOptimizationAsync(plan.Confirm(), default);
        check(admission.IsAllowed && admission.BatchId == plan.BatchId && File.Exists(trialPath), "Office package: normal confirmed-operation trial admission");
        var deniedRecords = Path.Combine(scratch, "denied-records");
        await using (var absent = new WorkerClient(Path.Combine(scratch, "absent-worker.exe")))
        {
            var executor = new OfficePackageOptimizationExecutor(absent, new OutputPublisher(deniedRecords, new ForbiddenRecycle()));
            try { await executor.ExecuteAdmittedAsync(plan.Confirm(), new(new(true, "Other batch"), Guid.NewGuid()), null, default); check(false, "Office package foreign admission"); }
            catch (InvalidDataException) { check(absent.ProcessId is null && !Directory.Exists(deniedRecords), "Office package: foreign admission touches neither worker nor publication"); }
            var denied = await executor.ExecuteAdmittedAsync(plan.Confirm(), new(new(false, "Activate a license."), plan.BatchId), null, default);
            check(denied.Results.Single().State == OperationState.Failed && denied.Results.Single().Message == "Activate a license." && absent.ProcessId is null &&
                !Directory.Exists(deniedRecords), "Office package: denied access starts no worker or reservation");
        }
        if (executable is null) return;

        var actual = Path.Combine(scratch, "actual"); Directory.CreateDirectory(actual);
        var paths = new[] { "docx", "xlsx", "pptx" }.Select(format => Path.Combine(actual, "Document ü." + format)).ToArray();
        foreach (var path in paths) await File.WriteAllBytesAsync(path, Fixture(Path.GetExtension(path)[1..]));
        var originals = paths.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var timestamps = paths.ToDictionary(path => path, File.GetLastWriteTimeUtc);
        var clock = new Clock(); var access = new Access(new LocalTrialStore(Path.Combine(actual, "trial", "trial.json"), clock));
        var records = Path.Combine(actual, "records");
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, Path.Combine(actual, "workers"));
        var sources = new List<OfficePackageSource>();
        foreach (var path in paths) sources.Add(await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), path), default));
        check(sources.Select(item => item.Format).SequenceEqual(["docx", "xlsx", "pptx"]) && access.Admissions == 0 && !File.Exists(Path.Combine(actual, "trial", "trial.json")),
            "Office package: real-worker complete probes identify three families before paid admission");
        var workerId = worker.ProcessId;
        var collision = Path.Combine(actual, OutputNames.Create(paths[0], "optimize", "docx"));
        await File.WriteAllTextAsync(collision, "collision canary");
        var confirmed = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), sources, settings).Confirm();
        var admitted = await access.AdmitOptimizationAsync(confirmed, default);
        var completed = await new OfficePackageOptimizationExecutor(worker, publisher).ExecuteAdmittedAsync(confirmed, admitted, (_, row) =>
        { if (row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(8); }, default);
        check(access.Admissions == 1 && completed.Results.Count == 3 && completed.Results.All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated),
            "Office package: one admitted batch publishes all three document copies across expiry despite overwrite setting");
        check(completed.Results[0].Publication!.OutputPath != collision && await File.ReadAllTextAsync(collision) == "collision canary",
            "Office package: colliding destination remains intact");
        for (var index = 0; index < paths.Length; index++)
        {
            var published = completed.Results[index].Publication!.OutputPath!;
            var original = await File.ReadAllBytesAsync(paths[index]); var optimized = await File.ReadAllBytesAsync(published);
            check(optimized.Length < original.Length && originals[paths[index]].AsSpan().SequenceEqual(SHA256.HashData(original)) && File.GetLastWriteTimeUtc(paths[index]) == timestamps[paths[index]],
                "Office package: validated smaller " + sources[index].Format + " copy preserves original bytes and timestamp");
            ComparePayloads(original, optimized, check);
        }
        check(worker.ProcessId == workerId, "Office package: one sequential worker serves all package operations");
        var expired = await access.AdmitOptimizationAsync(confirmed, default);
        check(!expired.IsAllowed, "Office package: expiry blocks the next batch after admitted work completes");
        var fresh = new Access(new LocalTrialStore(Path.Combine(actual, "fresh-trial", "trial.json")));
        async Task<ImageBatchExecution> Execute(params OfficePackageSource[] items)
        {
            var batch = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), items, new("optimize", new())).Confirm();
            return await new OfficePackageOptimizationExecutor(worker, publisher).ExecuteAdmittedAsync(batch, await fresh.AdmitOptimizationAsync(batch, default), null, default);
        }
        var optimizedSource = await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), completed.Results[0].Publication!.OutputPath!), default);
        var beforeFiles = Directory.GetFiles(actual).Length;
        var unchanged = await Execute(optimizedSource);
        check(unchanged.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(actual).Length == beforeFiles,
            "Office package: no-smaller result creates no duplicate");
        var changedPath = Path.Combine(actual, "Changed.docx"); await File.WriteAllBytesAsync(changedPath, Fixture("docx"));
        var changed = await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), changedPath), default);
        await File.AppendAllTextAsync(changedPath, "changed");
        var failed = await Execute(changed);
        check(failed.Results.Single().State == OperationState.Failed && new FileInfo(changedPath).Length == changed.FileBytes + 7,
            "Office package: source change after planning refuses publication and preserves changed source");
        var reserved = Path.Combine(actual, $".context-suite-{sources[0].ItemId:N}.tmp");
        await File.WriteAllTextAsync(reserved, "reservation canary");
        try { await worker.OptimizeOfficePackageAsync(new(sources[0], reserved), default); check(false, "Office package nonempty reservation"); }
        catch (MediaWorkerException) { check(await File.ReadAllTextAsync(reserved) == "reservation canary", "Office package: independently checked nonempty reservation is untouched"); }
        File.Delete(reserved);
        await File.WriteAllBytesAsync(reserved, []);
        try { await worker.OptimizeOfficePackageAsync(new(sources[0] with { Sha256 = new('0', 64) }, reserved), default); check(false, "Office package stale source digest"); }
        catch (MediaWorkerException) { check(new FileInfo(reserved).Length == 0, "Office package: stale source digest refuses before reservation writes"); }
        File.Delete(reserved);
        foreach (var (name, bytes) in new[] { ("Signed.docx", Fixture("docx", signed: true)), ("Macro.docx", Fixture("docx", macro: true)), ("Archive.docx", new byte[] { 1, 2, 3 }) })
        {
            var path = Path.Combine(actual, name); await File.WriteAllBytesAsync(path, bytes);
            try { await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), path), default); check(false, "Office package unsupported " + name); }
            catch (MediaWorkerException) { check((await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytes), "Office package: complete probe refuses " + name + " without source changes"); }
        }
        check((await Execute(sources[1])).Results.Single().State == OperationState.Succeeded && worker.ProcessId == workerId,
            "Office package: later valid source succeeds in the same worker after reservation/protection refusals");
        var cancelPlan = OfficePackageOptimizationPlan.Create(Guid.NewGuid(), [sources[0], sources[2]], new("optimize", new())).Confirm();
        using var cancellation = new CancellationTokenSource();
        var cancelled = await new OfficePackageOptimizationExecutor(worker, publisher).ExecuteAdmittedAsync(cancelPlan,
            await fresh.AdmitOptimizationAsync(cancelPlan, default), (_, row) => { if (row.State == OperationState.Succeeded) cancellation.Cancel(); }, cancellation.Token);
        check(cancelled.Results[0].State == OperationState.Succeeded && cancelled.Results[1].State == OperationState.Cancelled,
            "Office package: cancellation between sources preserves committed copy and leaves remaining source untouched");
        check(!Directory.GetFiles(actual, ".context-suite-*.tmp").Any() && Directory.GetFiles(records).Length == 0,
            "Office package: completed, unchanged, and failed operations leave no owned reservation or publication journal");

        // This exercises application routing and a shared admission across formats.
        var png = Path.Combine(actual, "Image.png"); ImageWorkerContracts.WritePng(png, compression: CompressionLevel.NoCompression);
        var mixedAccess = new Access(new LocalTrialStore(Path.Combine(actual, "mixed-trial", "trial.json")));
        await using (var vm = new MainViewModel(new WorkerClient(executable, Path.Combine(actual, "mixed-workers")), new() { PlayCompletionSound = false }, publisher, mixedAccess))
        {
            check(vm.Admit(new(Guid.NewGuid(), "optimize", "auto", [paths[0], png, paths[1], paths[2]])).Accepted, "Office package direct: mixed selection is admitted");
            await vm.WaitForIdleAsync();
            check(mixedAccess.Admissions == 1 && vm.Rows.Count == 4 && vm.Rows.All(row => row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated),
                "Office package direct: PNG and all three document families share one admission and copy publication");
        }

        void Refuses(Action action, string reason)
        {
            try { action(); check(false, "Office package rejects " + reason); }
            catch (InvalidDataException) { check(true, "Office package rejects " + reason); }
        }
    }

    private static byte[] Fixture(string family, bool signed = false, bool macro = false)
    {
        var (part, root, format, type) = family switch
        {
            "docx" => ("word/document.xml", "document", "wordprocessingml", "wordprocessingml.document"),
            "xlsx" => ("xl/workbook.xml", "workbook", "spreadsheetml", "spreadsheetml.sheet"),
            _ => ("ppt/presentation.xml", "presentation", "presentationml", "presentationml.presentation")
        };
        var contentType = macro ? "application/vnd.ms-word.document.macroEnabled.main+xml" : "application/vnd.openxmlformats-officedocument." + type + ".main+xml";
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{part}\" ContentType=\"{contentType}\"/></Types>");
            Add("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"main\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"{part}\"/></Relationships>");
            Add(part, $"<{root} xmlns=\"http://schemas.openxmlformats.org/{format}/2006/main\"/>");
            Add("docProps/opaque.xml", "<opaque>" + new string('x', 65536) + "</opaque>");
            if (signed) Add("_xmlsignatures/sig1.xml", "<Signature/>");
            void Add(string name, string content)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                entry.LastWriteTime = new(2025, 6, 7, 8, 9, 10, TimeSpan.Zero);
                using var stream = entry.Open(); stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        var package = bytes.ToArray();
        // Author ZIP 2.0 extraction declarations for the stored fixtures so a
        // Deflate rewrite does not need to change compatibility metadata.
        var directory = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(package.Length - 6)));
        while (BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(directory)) == 0x02014b50)
        {
            var local = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(directory + 42)));
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(local + 4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(directory + 6), 20);
            directory += 46 + BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(directory + 28)) +
                BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(directory + 30)) + BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(directory + 32));
        }
        return package;
    }
    private static void ComparePayloads(byte[] source, byte[] candidate, Action<bool, string> check)
    {
        using var before = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
        using var after = new ZipArchive(new MemoryStream(candidate), ZipArchiveMode.Read);
        var preserved = before.Entries.Count == after.Entries.Count;
        for (var index = 0; preserved && index < before.Entries.Count; index++)
        {
            var left = before.Entries[index]; var right = after.Entries[index];
            using var leftBytes = new MemoryStream(); using var rightBytes = new MemoryStream();
            using (var stream = left.Open()) stream.CopyTo(leftBytes);
            using (var stream = right.Open()) stream.CopyTo(rightBytes);
            preserved = left.FullName == right.FullName && left.LastWriteTime == right.LastWriteTime && left.ExternalAttributes == right.ExternalAttributes &&
                leftBytes.ToArray().AsSpan().SequenceEqual(rightBytes.ToArray());
        }
        check(preserved, "Office package: independent ZIP reader confirms all parts and member metadata remain intact");
    }
    private sealed class Access(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected conversion admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedOfficePackageOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Document optimization must publish copies without recycling originals.");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
}
