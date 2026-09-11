using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class ImagePdfWorkerContracts
{
    public static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new combined PDF workflow directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool pass, string name) { check(pass, name); checks.Add(name); }
        var names = new[] { "samples-16-3-True.png", "orientation-6.jpg", "wide color ü.png", "common.webp", "common.bmp", "alpha.tga", "samples-16-1-True.png" };
        foreach (var name in names) File.Copy(Path.Combine(fixtures, name), Path.Combine(root, name));
        var originalHashes = names.ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
        var clock = new Clock();
        var trialPath = Path.Combine(root, "trial.json");
        var access = new Access(new LocalTrialStore(trialPath, clock));
        var io = new GuardedIo();
        var records = Path.Combine(root, "records");
        var publisher = new OutputPublisher(records, new NoRecycle(), replacementVerified: true, io: io);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        Check(worker.HasImagePdfConverter, "combined PDF: isolated validator is available");
        var sources = new List<ImageSourceFacts>();
        foreach (var name in names) sources.Add(await worker.ProbeAsync(new(Guid.NewGuid(), Path.Combine(root, name)), default));
        var plan = Plan(sources);
        var workerId = worker.ProcessId;
        Check(!File.Exists(trialPath), "combined PDF: probing images does not start trial");
        var collision = Path.Combine(root, "samples-16-3-True - Combined.pdf"); File.WriteAllText(collision, "existing PDF canary");
        var executor = new ImagePdfExecutor(worker, publisher, access);
        io.Check = stage => {
            if (stage != PublicationStage.Publishing) return;
            foreach (var source in sources)
            {
                try { using var writable = new FileStream(source.Path, FileMode.Open, FileAccess.Write, FileShare.Read); throw new Exception("Original writable during publication."); }
                catch (IOException) { }
                try { File.Move(source.Path, source.Path + ".moved"); throw new Exception("Original movable during publication."); }
                catch (IOException) { }
            }
            Check(true, "combined PDF: every original is locked against write/rename at final publication boundary");
            var record = publisher.FindRecoveryRecords().Single();
            using var json = JsonDocument.Parse(File.ReadAllBytes(record));
            var members = json.RootElement.GetProperty("Sources");
            Check(members.EnumerateArray().Select(member => member.GetProperty("ItemId").GetGuid()).SequenceEqual(sources.Select(source => source.ItemId)),
                "combined PDF: recovery journal retains every original in reviewed order");
        };
        var completed = await executor.ExecuteAsync(plan, row => { if (row.Message == "Creating and validating PDF") clock.Now = clock.Now.AddDays(4); }, default);
        io.Check = null;
        Check(completed.Result.Publication?.Outcome == PublicationOutcome.CopyCreated && access.Admissions == 1 && worker.ProcessId == workerId,
            "combined PDF: one sequential worker and one admission complete seven mixed images across trial expiry");
        var output = completed.Result.Publication!.OutputPath!;
        Check(Path.GetFileName(output) == "samples-16-3-True - Combined (2).pdf" && File.ReadAllText(collision) == "existing PDF canary",
            "combined PDF: named copy preserves collision canary and ignores overwrite preference");
        Check(completed.Result.Publication.SourceBytes == sources.Sum(source => source.FileBytes) && Directory.GetFiles(records).Length == 0,
            "combined PDF: aggregate source bytes and successful journal cleanup");
        var denied = await executor.ExecuteAsync(plan, null, default);
        Check(!denied.Admission.IsAllowed && denied.Result.Publication is null && Directory.GetFiles(records).Length == 0,
            "combined PDF: expired access starts no reservation");
        var fresh = new Access(new LocalTrialStore(Path.Combine(root, "fresh trial.json")));
        executor = new ImagePdfExecutor(worker, publisher, fresh);
        using (var cancelled = new CancellationTokenSource())
        {
            var stopped = await executor.ExecuteAsync(Plan(sources), row => { if (row.Message == "Creating and validating PDF") cancelled.Cancel(); }, cancelled.Token);
            Check(stopped.Result.State == OperationState.Cancelled && Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(root, ".context-suite-*.tmp").Any(),
                "combined PDF: cancellation after reservation cleans candidate/journal and releases source group");
        }
        var changedPath = Path.Combine(root, "changed.png"); File.Copy(sources[1].Path, changedPath);
        var changed = await worker.ProbeAsync(new(Guid.NewGuid(), changedPath), default); File.AppendAllText(changedPath, "changed");
        var stale = await executor.ExecuteAsync(Plan([sources[0], changed]), null, default);
        Check(stale.Result.State == OperationState.Failed && stale.Result.Publication is null && Directory.GetFiles(records).Length == 0,
            "combined PDF: changed secondary original blocks the entire document before reservation");
        using (var writable = new FileStream(sources[0].Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(true, "combined PDF: partial source-group acquisition releases earlier originals on failure");
        foreach (var afterMove in new[] { false, true })
        {
            var faultRoot = Path.Combine(root, afterMove ? "after-move" : "before-move"); Directory.CreateDirectory(faultRoot);
            var faultPublisher = new OutputPublisher(Path.Combine(faultRoot, "records"), new NoRecycle(), io: new MoveFault(afterMove));
            var faultPlan = ImagePdfPlan.Create(Guid.NewGuid(), sources, new("convert", new(OutputDirectory: faultRoot))).Confirm(true);
            var fault = await new ImagePdfExecutor(worker, faultPublisher, fresh).ExecuteAsync(faultPlan, null, default);
            Check(fault.Result.Publication?.IsCommitted == afterMove && fault.Result.Publication?.RecoveryRecordPath is not null,
                "combined PDF: move failure distinguishes committed output and preserves recovery evidence " + afterMove);
            using var journal = JsonDocument.Parse(File.ReadAllBytes(faultPublisher.FindRecoveryRecords().Single()));
            Check(journal.RootElement.GetProperty("Sources").GetArrayLength() == sources.Count,
                "combined PDF: failure journal preserves all original fingerprints " + afterMove);
        }
        var single = await executor.ExecuteAsync(Plan([sources[0]]), null, default);
        Check(single.Result.Publication?.IsCommitted == true && Path.GetFileName(single.Result.Publication.OutputPath) == "samples-16-3-True - Converted.pdf",
            "combined PDF: one image uses ordinary converted-copy naming");
        await using (var crashWorker = new WorkerClient(executable, Path.Combine(root, "crash-workers")))
        {
            await crashWorker.ProbeAsync(new(sources[0].ItemId, sources[0].Path), default);
            var killed = crashWorker.ProcessId;
            var crashExecutor = new ImagePdfExecutor(crashWorker, publisher, fresh);
            var crashed = await crashExecutor.ExecuteAsync(Plan(sources), row => {
                if (row.Message != "Creating and validating PDF") return;
                using var owned = Process.GetProcessById(crashWorker.ProcessId!.Value);
                owned.Kill(true);
            }, default);
            Check(crashed.Result.State == OperationState.Failed && Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(root, ".context-suite-*.tmp").Any(),
                "combined PDF: owned worker death after reservation releases all sources and discards unpublished output");
            var retry = await crashExecutor.ExecuteAsync(Plan(sources), null, default);
            Check(retry.Result.Publication?.IsCommitted == true && crashWorker.ProcessId != killed,
                "combined PDF: explicit whole-document retry uses a fresh worker after its crash");
        }
        var id = Guid.NewGuid(); var temporary = Path.Combine(root, $".context-suite-{id:N}.tmp");
        var work = new ImagePdfWork(plan.Plan, id, temporary, true);
        File.WriteAllText(temporary, "nonempty reservation canary");
        await Refuse(work, "worker refuses nonempty reservation");
        Check(File.ReadAllText(temporary) == "nonempty reservation canary", "combined PDF: refused reservation is untouched");
        File.Delete(temporary);
        var empty = Path.Combine(root, "empty.tmp"); File.WriteAllBytes(empty, []);
        if (!CreateHardLinkW(temporary, empty, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        await Refuse(work, "worker refuses hard-linked reservation"); File.Delete(temporary); File.Delete(empty);
        File.WriteAllBytes(temporary, []);
        var forged = plan.Plan with { Pages = plan.Plan.Pages.SetItem(1, plan.Plan.Pages[1] with { Source = sources[1] with { Sha256 = new('0', 64) } }) };
        await Refuse(work with { Plan = forged }, "worker independently refuses changed secondary source");
        Check(new FileInfo(temporary).Length == 0, "combined PDF: worker validates originals before writing output"); File.Delete(temporary);
        foreach (var source in sources)
        {
            Check(Hash(source.Path) == originalHashes[Path.GetFileName(source.Path)], "combined PDF: original preserved " + Path.GetFileName(source.Path));
            using var writable = new FileStream(source.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        await worker.DisposeAsync();
        Check(!Directory.Exists(Path.Combine(root, "workers")) || !Directory.EnumerateFileSystemEntries(Path.Combine(root, "workers")).Any(),
            "combined PDF: owned worker scratch cleaned on exit");
        await ImagePdfPublicationCrashContracts.RunAsync(Path.Combine(root, "app-crashes"), executable, fixtures, Check);
        File.WriteAllText(Path.Combine(root, "image-pdf-worker.json"), JsonSerializer.Serialize(new { checks, output, outputSha256 = Hash(output) }, new JsonSerializerOptions { WriteIndented = true }));

        ConfirmedImagePdf Plan(IEnumerable<ImageSourceFacts> values) => ImagePdfPlan.Create(Guid.NewGuid(), values, new("convert", new(ReplaceOriginals: true))).Confirm(true);
        async Task Refuse(ImagePdfWork invalid, string name)
        {
            try { await worker.ConvertImagesToPdfAsync(invalid, default); Check(false, "combined PDF unexpectedly accepted " + name); }
            catch (MediaWorkerException) { Check(true, "combined PDF: " + name); }
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class NoRecycle : IFileRecycler
    { public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new Exception("Combined PDF must not recycle."); }
    private sealed class GuardedIo : PublicationIo
    { public Action<PublicationStage>? Check { get; set; } public override void Checkpoint(PublicationStage stage) => Check?.Invoke(stage); }
    private sealed class MoveFault(bool afterMove) : PublicationIo
    { public override void Move(string source, string destination) { if (afterMove) base.Move(source, destination); throw new IOException("Generated move failure."); } }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now { get; set; } = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; public override long GetTimestamp() => 0; }
    private sealed class Access(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new Exception("Wrong admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new Exception("Wrong admission.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken token) { Admissions++; return inner.AdmitConversionAsync(confirmed, token); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
}
