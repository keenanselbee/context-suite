using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

internal static class PdfDarkApplicationContracts
{
    public static async Task RunAsync(string root, string workerExecutable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new PDF dark-mode evidence directory.");
        Directory.CreateDirectory(root);
        var pdf = Path.Combine(root, "Reading.pdf");
        var other = Path.Combine(root, "Notes.txt");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4\npublic app-routing fixture\n");
        await File.WriteAllTextAsync(other, "not a PDF");

        var request = new OperationRequest(Guid.NewGuid(), "convert", "pdf-dark", [pdf, other]);
        request.Validate();
        check(request.IsQuickAction && request.IsPdfDarkConversion && !request.IsImagePdfConversion,
            "PDF dark: action is a direct conversion distinct from combined-image PDF");
        var routeAccess = new Access();
        await using (var vm = new MainViewModel(new WorkerClient(Path.Combine(root, "missing-worker.exe")),
            new(), new OutputPublisher(Path.Combine(root, "route-records"), new ForbiddenRecycle()), routeAccess))
        {
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("PDF dark mode must not open the general conversion planner.");
            check(vm.Admit(request).Accepted, "PDF dark: direct batch is accepted");
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 2 && vm.Rows.All(row => row.ImagePdfPaths.IsEmpty) &&
                vm.Rows[0].Action == "pdf-dark" && vm.Rows[1].Action == "pdf-dark",
                "PDF dark: mixed selection keeps independent rows without PDF grouping");
            check(vm.Rows[0].Result.State == OperationState.Unsupported && vm.Rows[0].Status.Contains("engine") &&
                vm.Rows[1].Result.State == OperationState.Unsupported && vm.Rows[1].Status.Contains("PDF files only") &&
                routeAccess.Admissions == 0,
                "PDF dark: missing engine and non-PDF selection are reported independently before admission");
            vm.RecordResult(vm.Rows[0], new(pdf, OperationState.Failed, "Retry fixture"));
            check(vm.CanRetry, "PDF dark: a failed source can be retried");
            vm.RetryFailed();
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 3 && vm.Rows[^1].Action == "pdf-dark" && vm.Rows[^1].Path == pdf &&
                vm.Rows[^1].ImagePdfPaths.IsEmpty && vm.Rows[^1].Result.State == OperationState.Unsupported,
                "PDF dark: retry retains the dark action and excludes an unrelated unsupported file");
        }
        var oversizedSelection = Enumerable.Range(0, 129).Select(index => Path.Combine(root, $"Many-{index:D3}.pdf")).ToArray();
        foreach (var path in oversizedSelection) File.Copy(pdf, path);
        var boundedAccess = new Access();
        await using (var vm = new MainViewModel(new WorkerClient(Path.Combine(root, "missing-worker.exe")),
            new(), new OutputPublisher(Path.Combine(root, "bounded-records"), new ForbiddenRecycle()), boundedAccess))
        {
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf-dark", [.. oversizedSelection, other]));
            await vm.WaitForIdleAsync();
            check(vm.Rows.Count == 130 && vm.Rows.Take(129).All(row => row.Result.State == OperationState.Unsupported &&
                row.Status.Contains("128 distinct PDFs")) && vm.Rows[^1].Status.Contains("PDF files only") &&
                boundedAccess.Admissions == 0,
                "PDF dark: oversized PDF selection is refused before any probe while non-PDF stays distinct");
        }

        var source = MakeSource(pdf, changedPages: 0);
        var access = new Access { Allowed = true };
        await using var worker = new WorkerClient(workerExecutable, Path.Combine(root, "worker"));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new ForbiddenRecycle(), replacementVerified: true);
        var executor = new PdfDarkExecutor(worker, publisher, access);
        var results = new List<FileResult>();
        await executor.ExecuteAsync(Plan(source), (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
        check(access.Admissions == 1 && results.Single().State == OperationState.Unchanged &&
            !Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any() && worker.ProcessId is null,
            "PDF dark: an already-dark batch is admitted once and remains unchanged without worker output");
        access.Allowed = false;
        results.Clear();
        await executor.ExecuteAsync(Plan(source), (_, result) => results.Add(result), default);
        check(access.Admissions == 2 && results.Single().State == OperationState.Failed && worker.ProcessId is null,
            "PDF dark: denied access blocks even an already-dark batch before reporting unchanged");

        access.Allowed = true;
        var changed = MakeSource(pdf, changedPages: 1);
        await File.AppendAllTextAsync(pdf, "changed after inspection");
        results.Clear();
        await executor.ExecuteAsync(Plan(changed), (_, result) => { if (result.State != OperationState.Running) results.Add(result); }, default);
        check(results.Single().State == OperationState.Failed && worker.ProcessId is null &&
            !Directory.GetFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "PDF dark: source identity change is rejected before worker execution and reservation is cleaned");

        var settings = new BatchSettings("convert", new(ReplaceOriginals: true, OutputDirectory: root));
        var original = await File.ReadAllBytesAsync(pdf);
        var collision = Path.Combine(root, "Reading - Dark Mode.pdf");
        await File.WriteAllTextAsync(collision, "existing dark copy");
        var candidate = Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string('D', original.Length + 64));
        for (var number = 2; number <= 3; number++)
        {
            var reservation = await publisher.ReserveAsync(new(Guid.NewGuid(), pdf, "pdf", settings, PdfDark: true));
            check(!reservation.Intent.ReplaceOriginal && !reservation.Policy.SkipIfLarger &&
                Path.GetFileName(reservation.Record.OutputPath) == $"Reading - Dark Mode ({number}).pdf",
                "PDF dark: overwrite preference still reserves a collision-safe copy and allows a larger output");
            await File.WriteAllBytesAsync(reservation.TemporaryPath, candidate);
            var validation = new OutputValidation(reservation.Intent.ItemId, Convert.ToHexString(SHA256.HashData(candidate)), true);
            var published = await publisher.PublishAsync(reservation, validation);
            check(published.Outcome == PublicationOutcome.CopyCreated && File.Exists(published.OutputPath) &&
                (await File.ReadAllBytesAsync(published.OutputPath!)).SequenceEqual(candidate),
                "PDF dark: validated candidate publishes through the real transactional publisher");
        }
        check((await File.ReadAllBytesAsync(pdf)).SequenceEqual(original) &&
            await File.ReadAllTextAsync(collision) == "existing dark copy",
            "PDF dark: original and prior collision survive both publications");
    }

    private static PdfDarkSource MakeSource(string path, int changedPages) => new(Guid.NewGuid(), path,
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length,
        new PdfDarkFacts(1, changedPages, 1 - changedPages, 0, 0));

    private static ConfirmedPdfDark Plan(params PdfDarkSource[] sources) => new PdfDarkPlan(Guid.NewGuid(),
        new("convert", new()), sources.ToImmutableArray()).Confirm();

    private sealed class Access : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) =>
            Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Access active." : "Activate access."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDark confirmed, CancellationToken token)
        {
            Admissions++;
            return Task.FromResult(new OperationAdmission(new(Allowed, Allowed ? "Access active." : "Activate access."), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new NotSupportedException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF dark-mode copies must not recycle originals.");
    }
}
