using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using Microsoft.Win32;

internal static class OfficeDirectExecutionContracts
{
    internal static async Task RunAsync(string workerPath, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use new repository Office direct-execution evidence.");
        var originals = Path.Combine(root, "originals"); Directory.CreateDirectory(originals);
        var contexts = Path.Combine(root, "contexts");
        var retained = Path.Combine(root, "retained-journals"); Directory.CreateDirectory(retained);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var documents = new[] { "docx", "xlsx", "pptx" }.Select(format =>
        {
            var fixture = Directory.EnumerateFiles(fixtures, "*." + format).Single();
            var path = Path.Combine(originals, Path.GetFileName(fixture)); File.Copy(fixture, path); return path;
        }).ToArray();
        var images = new[] { Path.Combine(originals, "first.bmp"), Path.Combine(originals, "second.bmp") };
        foreach (var image in images)
        {
            using var writer = new BinaryWriter(File.Create(image));
            writer.Write((ushort)0x4d42); writer.Write(58); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(1); writer.Write(1); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(new byte[] { 40, 90, 160, 0 });
        }
        var before = documents.Concat(images).ToDictionary(path => path, path => (Hash: Hash(path), Written: File.GetLastWriteTimeUtc(path)));
        var checks = new List<string>(); var reports = new List<object>(); var results = new List<FileResult>();
        var access = new Access(); var prompts = 0; var orderPrompts = 0;
        var worker = new WorkerClient(workerPath, Path.Combine(root, "workers"));
        var collision = Path.Combine(output, OutputNames.Create(documents[0], "convert", "pdf")); File.WriteAllText(collision, "keep existing PDF name");
        await using (var vm = Create(new OutputPublisher(Path.Combine(root, "records"), new NoRecycle())))
        {
            var quiet = new QuietWorkflow(); var started = 0; var finished = 0;
            vm.QuickBatchStarted += (request, _) => { started++; quiet.Begin(request.RequestId, true, DateTimeOffset.UtcNow); };
            vm.QuickBatchCompleted += (request, rows) => { finished++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
            vm.ImagePdfOrderRequested += async (decision, token) =>
            {
                orderPrompts++; ConfirmedImagePdf? confirmed = null;
                decision.Confirmed += value => confirmed = value;
                await decision.RefreshAccessAsync(); token.ThrowIfCancellationRequested();
                decision.SelectedPage = decision.Pages.Last(); decision.MoveUpCommand.Execute(null); decision.ConfirmCommand.Execute(null);
                return confirmed;
            };
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [documents[0], images[0], documents[1], images[1], documents[2]]));
            Observe(vm); await vm.WaitForIdleAsync();
            results.AddRange(vm.Rows.Select(row => row.Result));
            Check(vm.Rows.Count == 4 && vm.Rows.All(row => row.Result.State == OperationState.Succeeded && row.HasOutput),
                "mixed direct PDF command publishes three Office copies and one combined image PDF");
            Check(access.Admissions == 1 && prompts == 1 && orderPrompts == 1 && started == 1 && finished == 1 && !quiet.NeedsAttention,
                "mixed command shares one admission, batch completion and focused choices with quiet success");
            Check(vm.Rows.Single(row => row.IsImagePdf).ReviewedImagePdf!.Pages.Select(page => page.Source.Path).SequenceEqual(images.Reverse()),
                "combined image PDF retains the reviewed subset order");
            Check(File.ReadAllText(collision) == "keep existing PDF name" && Path.GetFileName(vm.Rows[0].OutputPath) == OutputNames.Create(documents[0], "convert", "pdf", 2),
                "Office output collision preserves the existing name and publishes a numbered copy");
            Check(!vm.CanRetry && vm.Rows.Where(row => !row.IsImagePdf).All(row => row.Result.EngineIdentity!.Contains("qpdf 12.4.1; PDFium 8044")),
                "successful Office rows retain independent validation identity and cannot be retried");
            reports.Add(new { Results = vm.Rows.Where(row => !row.IsImagePdf).Select(row => row.Result).ToArray() });
        }
        var retryOutput = Path.Combine(root, "retry-output"); Directory.CreateDirectory(retryOutput);
        var failure = new FailPublication();
        await using (var vm = Create(new OutputPublisher(Path.Combine(root, "retry-records"), new NoRecycle(), io: failure)))
        {
            vm.Settings = vm.Settings with { Convert = new(OutputDirectory: retryOutput, ReplaceOriginals: true) };
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [documents[0]])); Observe(vm); await vm.WaitForIdleAsync();
            Check(vm.Rows.Single().Result.State == OperationState.Failed && vm.CanRetry && !Directory.EnumerateFiles(retryOutput).Any(),
                "failed Office publication leaves a retryable row and no output");
            reports.Add(new { Results = vm.Rows.Select(row => row.Result).ToArray() });
            vm.RetryFailed(); vm.RetryFailed(); Observe(vm); await vm.WaitForIdleAsync();
            Check(vm.Rows.Count == 2 && vm.DisplayRows.Single().Result.State == OperationState.Succeeded && Directory.EnumerateFiles(retryOutput, "*.pdf").Count() == 1 && !vm.CanRetry,
                "direct Office retry queues once and publishes exactly one copy");
            reports.Add(new { Results = new[] { vm.DisplayRows.Single().Result } });
        }
        Check(before.All(pair => Hash(pair.Key) == pair.Value.Hash && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "all image and Office originals retain their bytes and modification times");
        Check(!Directory.EnumerateFileSystemEntries(contexts).Any() && worker.ProcessId is null,
            "view-model disposal leaves no active worker or generated Office context");
        var profiles = new List<object>();
        foreach (var record in Directory.EnumerateFiles(retained, "*.ownership"))
        {
            using var journal = OfficeOwnershipJournal.Open(record, contexts, worker.OfficeEngineDirectory);
            var work = journal.Owner.Work;
            var sid = journal.Changes.Single(change => change.Step == OfficeOwnershipStep.ProfileCreated).Sid!;
            using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
            Check(mapping is null && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) && !Directory.Exists(work.DirectoryPath),
                "direct command retires native profile and context: " + work.Format);
            profiles.Add(new { work.ProfileName, Sid = sid, Removed = true, Journal = record, ContextRetired = true });
        }
        Check(profiles.Count == 5 && access.Admissions == 3, "five actual exports use three admitted direct-command attempts");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Reports = reports, Profiles = profiles, MixedResults = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} direct Office execution checks.");

        MainViewModel Create(OutputPublisher publisher)
        {
            var vm = new MainViewModel(worker, new SuiteSettings { Convert = new(OutputDirectory: output, ReplaceOriginals: true) }, publisher, access, contexts);
            vm.OfficeCalculationRequested += _ => { prompts++; return Task.FromResult<string?>("cached"); };
            return vm;
        }
        void Observe(MainViewModel vm)
        {
            foreach (var row in vm.Rows)
                row.PropertyChanged += (_, _) =>
                {
                    if (row.Result.State != OperationState.Running || row.Status != "Removing temporary files") return;
                    var record = Directory.EnumerateFiles(contexts, "*.ownership").Single();
                    var copy = Path.Combine(retained, Path.GetFileName(record));
                    if (!File.Exists(copy)) File.Copy(record, copy);
                };
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Direct Office conversion must keep originals.");
    }
    private sealed class FailPublication : PublicationIo
    {
        private int _remaining = 1;
        public override void Checkpoint(PublicationStage stage) { if (stage == PublicationStage.Validated && _remaining-- > 0) throw new IOException("Authored publication failure."); }
    }
    private sealed class Access : IOperationAccess
    {
        internal int Admissions;
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OperationAccessStatus(true, "Test access."));
        private Task<OperationAdmission> Admit(Guid batch) { Admissions++; return Task.FromResult(new OperationAdmission(new(true, "Test access."), batch)); }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedOfficeConversion confirmed, CancellationToken token) => Admit(confirmed.Plan.BatchId);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken token) => Admit(confirmed.Plan.BatchId);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected raster admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected Optimize admission.");
    }
}
