using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

internal static class PdfDarkFailureContracts
{
    public static async Task RunDocumentRefusalAsync(string root, string workerExecutable, string document,
        string expectedPageText, string expectedFailureText, Action<bool, string> check)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root) ||
            !Path.IsPathFullyQualified(document) || !File.Exists(document))
            throw new IOException("Use a fresh absolute PDF dark-mode refusal directory and an existing absolute source path.");
        int? expectedPage = null;
        if (expectedPageText != "none")
        {
            if (!int.TryParse(expectedPageText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number is < 1 or > 512 || expectedPageText != number.ToString(CultureInfo.InvariantCulture))
                throw new ArgumentException("Expected a canonical physical page from 1 to 512, or none for a page-free refusal.");
            expectedPage = number;
        }
        if (!Enum.TryParse<ImageFailure>(expectedFailureText, ignoreCase: false, out var expectedFailure) ||
            expectedFailureText != expectedFailure.ToString() || !PdfDarkFailures.IsUnsupported(expectedFailure))
            throw new ArgumentException("Expected a PDF dark-mode refusal category.");

        var originalHash = Hash(document);
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Document.pdf");
        File.Copy(document, sourcePath, overwrite: false);
        await using (var worker = new WorkerClient(workerExecutable, Path.Combine(root, "worker-scratch")))
        {
            try
            {
                await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), sourcePath), default);
                check(false, "Supplied PDF was unexpectedly admitted for dark-mode conversion");
            }
            catch (MediaWorkerException error)
            {
                check(error.Failure == expectedFailure && error.PdfDarkPage == expectedPage &&
                    error.Message == PdfDarkFailures.Describe(expectedFailure, expectedPage),
                    $"Actual worker identifies expected refusal {expectedFailure}/{expectedPage?.ToString(CultureInfo.InvariantCulture) ?? "none"}; received {error.Failure}/{error.PdfDarkPage?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
            }
        }
        check(Hash(document) == originalHash && Hash(sourcePath) == originalHash,
            "Supplied original and disposable source copy remain byte-identical");
        check(Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories)
                .All(path => string.Equals(path, sourcePath, StringComparison.OrdinalIgnoreCase)) &&
              !Directory.EnumerateFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "Refused document leaves no output PDF or partial publication");
    }

    public static async Task RunAsync(string root, string workerExecutable, Action<bool, string> check)
    {
        check(PdfDarkFailures.Describe(ImageFailure.InvalidInput).Contains("Re-export", StringComparison.Ordinal) &&
            PdfDarkFailures.Describe(ImageFailure.InvalidInput).Contains("Original kept.", StringComparison.Ordinal) &&
            !PdfDarkFailures.Describe(ImageFailure.InvalidInput).Contains("image is damaged", StringComparison.Ordinal),
            "PDF dark failure: malformed source has a PDF-specific repair or re-export action");
        check(PdfDarkFailures.Describe(ImageFailure.UnsupportedInput).Contains("feature", StringComparison.Ordinal) &&
            PdfDarkFailures.Describe(ImageFailure.UnsupportedInput).Contains("static PDF export", StringComparison.Ordinal),
            "PDF dark failure: unsupported feature has a distinct static-export action");
        foreach (var failure in new[] { ImageFailure.PdfDarkContrast, ImageFailure.PdfDarkTransparency,
                     ImageFailure.PdfDarkProtected, ImageFailure.InvalidInput })
        {
            var message = PdfDarkFailures.Describe(failure, 512);
            check(message.StartsWith("Page 512: ", StringComparison.Ordinal) &&
                message.EndsWith("Original kept.", StringComparison.Ordinal) && message.Length < 300,
                "PDF dark failure: typed category carries a bounded physical page and unchanged original");
        }
        foreach (var page in new[] { int.MinValue, -1, 0, 513, int.MaxValue })
        {
            try
            {
                _ = PdfDarkFailures.Describe(ImageFailure.InvalidInput, page);
                check(false, "PDF dark failure: out-of-range page was accepted");
            }
            catch (ArgumentOutOfRangeException)
            {
                check(true, "PDF dark failure: out-of-range page is rejected");
            }
        }
        var malformed = new MediaWorkerException(ImageFailure.InvalidInput, command: "pdf-dark-probe", pdfDarkPage: 12);
        var unsupported = new MediaWorkerException(ImageFailure.PdfDarkTransparency, command: "pdf-dark-convert", pdfDarkPage: 1);
        var limited = new MediaWorkerException(ImageFailure.ResourceLimit, command: "pdf-dark-probe");
        var otherTool = new MediaWorkerException(ImageFailure.InvalidInput, command: "image-convert");
        check(malformed.PdfDarkPage == 12 && malformed.Message.StartsWith("Page 12: This PDF appears damaged", StringComparison.Ordinal) &&
            unsupported.Message.StartsWith("Page 1: This PDF uses transparency", StringComparison.Ordinal) &&
            limited.PdfDarkPage is null && limited.Message.StartsWith("This PDF exceeds", StringComparison.Ordinal) &&
            otherTool.Message == ImageFailureException.Describe(ImageFailure.InvalidInput),
            "PDF dark failure: page-aware damage and feature messages, page-free limit, and unchanged other tools");
        var legacyReply = JsonSerializer.Deserialize<WorkerReply>("{\"Version\":1,\"RequestId\":\"00000000-0000-0000-0000-000000000000\",\"Capabilities\":[],\"Failure\":0}");
        check(legacyReply?.PdfDarkFailurePage is null, "PDF dark failure: older wire replies retain a missing page");
        var typedReply = new WorkerReply(1, Guid.NewGuid(), [], Failure: ImageFailure.InvalidInput, PdfDarkFailurePage: 12);
        WorkerClient.ValidatePdfDarkFailurePage(typedReply, "pdf-dark-probe");
        check(JsonSerializer.Deserialize<WorkerReply>(JsonSerializer.Serialize(typedReply))?.PdfDarkFailurePage == 12,
            "PDF dark failure: typed page survives the existing wire version");
        foreach (var reply in new[]
        {
            typedReply with { PdfDarkFailurePage = 0 }, typedReply with { PdfDarkFailurePage = 513 },
            typedReply with { Failure = null }, typedReply with { PdfDarkFailurePage = int.MaxValue }
        })
        {
            try { WorkerClient.ValidatePdfDarkFailurePage(reply, "pdf-dark-probe"); check(false, "PDF dark failure: malformed reply was accepted"); }
            catch (InvalidDataException) { check(true, "PDF dark failure: malformed reply is rejected"); }
        }
        try { WorkerClient.ValidatePdfDarkFailurePage(typedReply, "image-convert"); check(false, "PDF dark failure: other tool accepted page metadata"); }
        catch (InvalidDataException) { check(true, "PDF dark failure: other tool rejects page metadata"); }
        if (Directory.Exists(root)) throw new IOException("Use a new PDF dark-mode failure directory.");
        Directory.CreateDirectory(root);
        var invalidPath = Path.Combine(root, "InvalidPath.pdf");
        WritePdf(invalidPath, pages: 1, rectanglesPerPage: 0, extraContent: "1 m\n");
        var invalidPathHash = Hash(invalidPath);
        await using (var diagnosticWorker = new WorkerClient(workerExecutable, Path.Combine(root, "diagnostic-worker")))
        {
            try
            {
                await diagnosticWorker.ProbePdfDarkAsync(new(Guid.NewGuid(), invalidPath), default);
                check(false, "PDF dark failure: invalid native path was accepted");
            }
            catch (MediaWorkerException error)
            {
                check(error.Failure == ImageFailure.InvalidInput && error.PdfDarkPage == 1 &&
                    error.Message.StartsWith("Page 1: This PDF appears damaged", StringComparison.Ordinal) &&
                    error.Message.EndsWith("Original kept.", StringComparison.Ordinal) &&
                    !error.Message.Contains(invalidPath, StringComparison.Ordinal) && Hash(invalidPath) == invalidPathHash,
                    "PDF dark failure: real worker identifies damaged page and keeps source unchanged");
            }
        }
        var pageLimit = Path.Combine(root, "page-limit");
        Directory.CreateDirectory(pageLimit);
        var tooManyPages = Path.Combine(pageLimit, "TooManyPages.pdf");
        WritePdf(tooManyPages, pages: 513, rectanglesPerPage: 0);
        var pageLimitHash = Hash(tooManyPages);
        await using (var limitWorker = new WorkerClient(workerExecutable, Path.Combine(pageLimit, "probe-worker")))
        {
            try
            {
                await limitWorker.ProbePdfDarkAsync(new(Guid.NewGuid(), tooManyPages), default);
                check(false, "PDF dark page limit: 513-page source was unexpectedly admitted");
            }
            catch (MediaWorkerException error)
            {
                check(error.Failure == ImageFailure.ResourceLimit && error.PdfDarkPage is null &&
                    error.Message == PdfDarkFailures.Describe(ImageFailure.ResourceLimit) &&
                    Hash(tooManyPages) == pageLimitHash,
                    "PDF dark page limit: real worker reports a page-free resource limit and leaves the source unchanged");
            }
        }
        var limitOutput = Path.Combine(pageLimit, "output");
        var limitRecords = Path.Combine(pageLimit, "records");
        var limitAccess = new Access();
        await using (var limitView = new MainViewModel(new WorkerClient(workerExecutable, Path.Combine(pageLimit, "view-worker")),
            new() { Convert = new(ReplaceOriginals: true, OutputDirectory: limitOutput) },
            new OutputPublisher(limitRecords, new NoRecycle()), limitAccess))
        {
            check(limitView.Admit(new(Guid.NewGuid(), "convert", "pdf-dark", [tooManyPages])).Accepted,
                "PDF dark page limit: direct action accepts the one-file batch for inspection");
            await limitView.WaitForIdleAsync();
            check(limitView.Rows.Count == 1 && limitView.Rows[0].Result.State == OperationState.Unsupported &&
                limitView.Rows[0].Status == PdfDarkFailures.Describe(ImageFailure.ResourceLimit) &&
                limitAccess.Count == 0,
                "PDF dark page limit: application reports the native limit before conversion admission");
        }
        check(Hash(tooManyPages) == pageLimitHash &&
            Directory.EnumerateFiles(pageLimit, "*.pdf", SearchOption.AllDirectories)
                .All(path => string.Equals(path, tooManyPages, StringComparison.OrdinalIgnoreCase)) &&
            !Directory.EnumerateFiles(pageLimit, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() &&
            (!Directory.Exists(limitOutput) || !Directory.EnumerateFileSystemEntries(limitOutput).Any()) &&
            (!Directory.Exists(limitRecords) || !Directory.EnumerateFileSystemEntries(limitRecords).Any()),
            "PDF dark page limit: no output, partial file or publication reservation remains");
        var first = Path.Combine(root, "First.pdf");
        var second = Path.Combine(root, "Second.pdf");
        WritePdf(first, pages: 1, rectanglesPerPage: 8);
        WritePdf(second, pages: 1, rectanglesPerPage: 8);
        var firstHash = Hash(first);
        var secondHash = Hash(second);
        var initial = Path.Combine(root, "initial-output");
        var retry = Path.Combine(root, "retry-output");
        Directory.CreateDirectory(initial);
        Directory.CreateDirectory(retry);
        var io = new FailSecondMove();
        var access = new Access();
        await using (var vm = new MainViewModel(new WorkerClient(workerExecutable, Path.Combine(root, "retry-worker")),
            new() { Convert = new(ReplaceOriginals: true, OutputDirectory: initial) },
            new OutputPublisher(Path.Combine(root, "retry-records"), new NoRecycle(), io: io), access))
        {
            vm.ConversionRequested += (_, _) => throw new InvalidOperationException("PDF dark mode must use its direct command.");
            vm.QuickBatchStarted += (_, rows) =>
            {
                rows[0].PropertyChanged += (_, change) =>
                {
                    if (change.PropertyName == nameof(FileRow.Result) && rows[0].Result.State == OperationState.Succeeded)
                        vm.Settings = new() { Convert = new(ReplaceOriginals: true, OutputDirectory: retry) };
                };
            };
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf-dark", [first, second]));
            await vm.WaitForIdleAsync();
            check(access.Count == 1 && vm.Rows.Count == 2 && io.Failed &&
                vm.Rows[0].Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                vm.Rows[1].Result.State == OperationState.Failed && vm.CanRetry,
                "PDF dark failure: one admitted batch keeps the first copy and isolates a publication move failure");
            var completedPath = vm.Rows[0].OutputPath;
            var completedHash = Hash(completedPath);
            check(Path.GetDirectoryName(completedPath) == initial &&
                vm.Rows[1].Settings.Preferences.OutputDirectory == initial &&
                !File.Exists(Path.Combine(initial, "Second - Dark Mode.pdf")),
                "PDF dark failure: settings remain fixed throughout the admitted batch");
            io.Fail = false;
            vm.RetryFailed();
            await vm.WaitForIdleAsync();
            check(access.Count == 2 && vm.Rows.Count == 3 && vm.DisplayRows.Count() == 2 &&
                vm.Rows[^1].Path == second && vm.Rows[^1].Action == "pdf-dark" &&
                vm.Rows[^1].Settings.Preferences.OutputDirectory == retry &&
                vm.Rows[^1].Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                Path.GetDirectoryName(vm.Rows[^1].OutputPath) == retry,
                "PDF dark failure: retry admits only the failed PDF with its action and new immutable settings");
            check(Hash(completedPath) == completedHash &&
                Directory.GetFiles(initial, "First - Dark Mode*.pdf").Length == 1 &&
                Hash(first) == firstHash && Hash(second) == secondHash,
                "PDF dark failure: retry neither repeats a completed copy nor changes either original");
        }

        var slow = Path.Combine(root, "Slow.pdf");
        WritePdf(slow, pages: 128, rectanglesPerPage: 8000);
        var slowHash = Hash(slow);
        var records = Path.Combine(root, "cancel-records");
        var workerScratch = Path.Combine(root, "cancel-worker");
        var publisher = new OutputPublisher(records, new NoRecycle());
        await using var worker = new WorkerClient(workerExecutable, workerScratch);
        var source = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), slow), default);
        check(source.Facts.PageCount == 128 && source.Facts.ChangedPages == 128,
            "PDF dark cancellation: bounded authored fixture enters the real native conversion path");
        var reservation = await publisher.ReserveAsync(new(source.ItemId, slow, "pdf", new("convert", new()), PdfDark: true));
        using var cancellation = new CancellationTokenSource();
        var operation = worker.ConvertPdfDarkAsync(new(source, reservation.TemporaryPath), cancellation.Token);
        var workerId = worker.ProcessId ?? throw new InvalidOperationException("Dark-mode worker did not start.");
        Process? native = null;
        try
        {
            var expectedNative = Path.Combine(Path.GetDirectoryName(workerExecutable)!, "pdf-dark", "ContextSuite.PdfDark.exe");
            var watch = Stopwatch.StartNew();
            while (!operation.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(30))
            {
                native = PdfFailureContracts.FindChild(workerId, expectedNative);
                if (native is not null && !native.HasExited) break;
                native?.Dispose();
                native = null;
                await Task.Delay(2);
            }
            if (native is null || native.HasExited || operation.IsCompleted)
                throw new InvalidOperationException("Did not observe the live owned PDF dark-mode native child.");
            check(native.Id != workerId && !operation.IsCompleted && !File.Exists(reservation.Record.OutputPath),
                "PDF dark cancellation: live owned native child is observed before cancellation");
            cancellation.Cancel();
            try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); throw new InvalidOperationException("Cancelled PDF dark mode succeeded."); }
            catch (OperationCanceledException) { check(true, "PDF dark cancellation: native-phase cancellation remains cancellation"); }
            await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            check(native.HasExited && worker.ProcessId is null,
                "PDF dark cancellation: owned native child and worker exit after cancellation");
            var abandoned = await publisher.AbandonAsync(reservation, true);
            check(abandoned.Outcome == PublicationOutcome.Cancelled &&
                !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                Hash(slow) == slowHash && !Directory.EnumerateFiles(records).Any() &&
                (!Directory.Exists(workerScratch) || !Directory.EnumerateDirectories(workerScratch).Any()),
                "PDF dark cancellation: no partial output, reservation or source change remains");
        }
        finally
        {
            cancellation.Cancel();
            try { await operation; } catch (Exception) { }
            if (!reservation.Finished) await publisher.AbandonAsync(reservation, true);
            native?.Dispose();
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WritePdf(string path, int pages, int rectanglesPerPage, string? extraContent = null)
    {
        using var file = File.Create(path);
        var offsets = new List<long> { 0 };
        Write("%PDF-1.4\n");
        var fontId = 3 + pages * 2;
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {pages} /Kids [{string.Join(' ', Enumerable.Range(0, pages).Select(index => $"{3 + index * 2} 0 R"))}] >>");
        for (var index = 0; index < pages; index++)
        {
            var pageId = 3 + index * 2;
            var contentId = pageId + 1;
            Object(pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 300] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>");
            var content = new StringBuilder("1 1 1 rg\n0 0 420 300 re f\n0 0 0 rg\nBT /F1 24 Tf 24 246 Td (Searchable reading text) Tj ET\n");
            if (extraContent is not null) content.Append(extraContent);
            for (var rectangle = 0; rectangle < rectanglesPerPage; rectangle++)
                content.Append("24 24 1 1 re f\n");
            var bytes = Encoding.ASCII.GetBytes(content.ToString());
            offsets.Add(file.Position);
            Write($"{contentId} 0 obj\n<< /Length {bytes.Length} >>\nstream\n");
            file.Write(bytes);
            Write("endstream\nendobj\n");
        }
        Object(fontId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var xref = file.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        void Write(string value) { file.Write(Encoding.ASCII.GetBytes(value)); }
        void Object(int id, string body) { offsets.Add(file.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
    }

    private sealed class FailSecondMove : PublicationIo
    {
        public bool Fail { get; set; } = true;
        public bool Failed { get; private set; }
        public override void Move(string source, string destination)
        {
            if (Fail && Path.GetFileName(destination).StartsWith("Second - Dark Mode", StringComparison.Ordinal))
            {
                Failed = true;
                throw new IOException("Authored publication move failure.");
            }
            base.Move(source, destination);
        }
    }

    private sealed class Access : IOperationAccess
    {
        public int Count { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) =>
            Task.FromResult(new OperationAccessStatus(true, "Allowed for disposable tests"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDark confirmed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Count++;
            return Task.FromResult(new OperationAdmission(new(true, "Allowed for disposable tests"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new NotSupportedException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF dark-mode failures must never recycle originals.");
    }
}
