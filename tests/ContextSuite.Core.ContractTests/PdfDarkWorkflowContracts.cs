using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class PdfDarkWorkflowContracts
{
    internal static async Task RunDocumentAsync(string root, string workerExecutable, string document,
        Action<bool, string> check, int? expectedRecoveredImages = null, int? expectedRecoveredCoordinates = null)
    {
        if (expectedRecoveredImages.HasValue != expectedRecoveredCoordinates.HasValue)
            throw new InvalidDataException("Supply both expected PDF dark-mode recovery counts or neither.");
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root))
            throw new IOException("Use a fresh absolute PDF dark-mode review directory.");
        var originalHash = Hash(document);
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Document.pdf");
        File.Copy(document, sourcePath, overwrite: false);
        var access = new Access();
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        await using var worker = new WorkerClient(workerExecutable, Path.Combine(root, "worker-scratch"));
        var source = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), sourcePath), default);
        check(source.Facts.ChangedPages > 0, "Actual worker admits the supplied document for recoloring");
        if (expectedRecoveredImages.HasValue)
            check(source.Facts.RecoveredImages == expectedRecoveredImages.Value &&
                source.Facts.RecoveredCoordinates == expectedRecoveredCoordinates!.Value,
                $"Actual worker reports expected source recovery counts (images {expectedRecoveredImages}, coordinates {expectedRecoveredCoordinates})");
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var plan = new PdfDarkPlan(Guid.NewGuid(), settings, [source]).Confirm();
        var results = new List<FileResult>();
        await new PdfDarkExecutor(worker, publisher, access).ExecuteAsync(plan, (_, result) =>
        { if (result.State != OperationState.Running) results.Add(result); }, default);
        var result = results.Single();
        check(result.State == OperationState.Succeeded && access.Count == 1,
            "Actual adapter independently validates glyphs and publishes under one admission");
        check(result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
            result.Publication.OutputPath == Path.Combine(root, "Document - Dark Mode.pdf"),
            "Supplied-document conversion publishes a mandatory dark-mode copy");
        if (expectedRecoveredImages.HasValue)
        {
            var message = result.Publication!.Message;
            var recoveredImages = expectedRecoveredImages.Value > 0;
            var recoveredCoordinates = expectedRecoveredCoordinates!.Value > 0;
            var retainedImages = source.Facts.PreservedImages > 0;
            check(result.Publication.MetadataWarning == (recoveredImages || recoveredCoordinates || retainedImages) &&
                message.Contains("Recovered inconsistent image dimensions using the JPEG and mask data.", StringComparison.Ordinal) == recoveredImages &&
                message.Contains("Recovered malformed drawing coordinates using the validated zero interpretation.", StringComparison.Ordinal) == recoveredCoordinates &&
                message.Contains("Some images retain their original colors.", StringComparison.Ordinal) == retainedImages,
                "Published PDF warning identifies only the recovery and retained-image conditions reported by the worker");
        }
        check(Hash(document) == originalHash && Hash(sourcePath) == originalHash,
            "Supplied original and test copy remain byte-identical");
        var output = result.Publication!.OutputPath!;
        var outputHash = Hash(output);
        var repeated = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), output), default);
        check(repeated.Facts.ChangedPages == 0 && repeated.Facts.PageCount == source.Facts.PageCount &&
            repeated.Facts.RecoveredImages == 0 && repeated.Facts.RecoveredCoordinates == 0,
            "Actual worker identifies converted pages as dark without further source recovery");
        var repeatResults = new List<FileResult>();
        var beforeRepeat = Directory.GetFiles(root, "*.pdf").Length;
        await new PdfDarkExecutor(worker, publisher, access).ExecuteAsync(
            new PdfDarkPlan(Guid.NewGuid(), settings, [repeated]).Confirm(), (_, item) =>
            { if (item.State != OperationState.Running) repeatResults.Add(item); }, default);
        check(repeatResults.Single().State == OperationState.Unchanged &&
            Directory.GetFiles(root, "*.pdf").Length == beforeRepeat && Hash(output) == outputHash &&
            Hash(document) == originalHash && Hash(sourcePath) == originalHash,
            "Repeating the real document action changes neither output nor originals and creates no duplicate PDF");
        Console.WriteLine("Output: " + result.Publication!.OutputPath);
        Console.WriteLine($"Pages: {source.Facts.PageCount}; recolored images: {source.Facts.RecoloredImages}; preserved images: {source.Facts.PreservedImages}; recovered images: {source.Facts.RecoveredImages}; recovered coordinates: {source.Facts.RecoveredCoordinates}");
    }

    internal static async Task RunAsync(string root, string workerExecutable, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh PDF dark-mode workflow directory.");
        Directory.CreateDirectory(root);
        var light = Path.Combine(root, "Reading.pdf");
        var dark = Path.Combine(root, "Night.pdf");
        var mixed = Path.Combine(root, "Mixed.pdf");
        var broken = Path.Combine(root, "Damaged.pdf");
        var annotated = Path.Combine(root, "Annotated.pdf");
        await File.WriteAllBytesAsync(light, Fixture([false]));
        await File.WriteAllBytesAsync(dark, Fixture([true]));
        await File.WriteAllBytesAsync(mixed, Fixture([false, true]));
        await File.WriteAllBytesAsync(annotated, Fixture([false], annotation: true));
        await File.WriteAllTextAsync(broken, "%PDF-1.7\ninvalid structure");
        var originals = new[] { light, dark, mixed, broken, annotated }.ToDictionary(path => path, Hash);
        await using var worker = new WorkerClient(workerExecutable, Path.Combine(root, "worker-scratch"));
        check(worker.HasPdfDarkConverter, "PDF dark worker includes its optional pinned engine");
        var source = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), light), default);
        check(source.Facts == new PdfDarkFacts(1, 1, 0, 0, 0), "Native probe identifies a light text/vector PDF");
        var night = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), dark), default);
        check(night.Facts.ChangedPages == 0 && night.Facts.AlreadyDarkPages == 1, "Native probe identifies an already-dark PDF");
        var mixedSource = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), mixed), default);
        check(mixedSource.Facts.ChangedPages == 1 && mixedSource.Facts.AlreadyDarkPages == 1,
            "Native probe keeps a mixed document's dark page unchanged");
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), replacementVerified: true);
        var access = new Access();
        var plan = new PdfDarkPlan(Guid.NewGuid(), settings, [source, night, mixedSource]).Confirm();
        var results = new List<FileResult>();
        await new PdfDarkExecutor(worker, publisher, access).ExecuteAsync(plan, (_, result) =>
        { if (result.State != OperationState.Running) results.Add(result); }, default);
        check(access.Count == 1 && results.Count == 3 && results[0].State == OperationState.Succeeded &&
            results[1].State == OperationState.Unchanged && results[2].State == OperationState.Succeeded,
            "Native dark-mode batch converts supported sources and retains already-dark files under one admission");
        check(results.Where(result => result.State == OperationState.Succeeded).All(result =>
            result.Publication?.Outcome == PublicationOutcome.CopyCreated && result.Publication.OutputPath!.EndsWith(" - Dark Mode.pdf")),
            "Native conversion publishes validated copies even with overwrite enabled");
        var output = results[0].Publication!.OutputPath!;
        var outputProbe = await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), output), default);
        check(outputProbe.Facts.ChangedPages == 0, "Native dark-mode output is recognized as already dark on a repeat request");
        await Refusal(broken, ImageFailure.InvalidInput, "Malformed PDF is rejected without publication");
        await Refusal(annotated, ImageFailure.UnsupportedInput, "PDF annotations are refused without flattening or dropping them");

        var tampered = source with { Facts = source.Facts with { PreservedImages = 1 } };
        var reservation = await publisher.ReserveAsync(new(source.ItemId, source.Path, "pdf", settings, PdfDark: true));
        try
        {
            var rejected = false;
            try { await worker.ConvertPdfDarkAsync(new(tampered, reservation.TemporaryPath), default); }
            catch (MediaWorkerException error) when (error.Failure == ImageFailure.ValidationFailed) { rejected = true; }
            check(rejected, "Worker revalidates planned dark-mode facts instead of trusting the caller");
        }
        finally { await publisher.AbandonAsync(reservation, false); }

        using var cancellation = new CancellationTokenSource();
        var cancelled = new List<FileResult>();
        await new PdfDarkExecutor(worker, publisher, access).ExecuteAsync(
            new PdfDarkPlan(Guid.NewGuid(), settings, [source]).Confirm(), (_, result) =>
            {
                if (result.State == OperationState.Running) cancellation.Cancel();
                else cancelled.Add(result);
            }, cancellation.Token);
        check(cancelled.Single().State == OperationState.Cancelled &&
            !Directory.GetFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
            "Cancellation before output work leaves no reservation or partial PDF");

        var vmAccess = new Access();
        await using (var vm = new MainViewModel(new WorkerClient(workerExecutable, Path.Combine(root, "vm-worker")),
            new(), new OutputPublisher(Path.Combine(root, "vm-records"), new NoRecycle()), vmAccess))
        {
            var request = new OperationRequest(Guid.NewGuid(), "convert", "pdf-dark", [light, broken]);
            check(vm.Admit(request).Accepted, "Native dark-mode direct action accepts one Explorer batch");
            await vm.WaitForIdleAsync();
            check(vm.Rows[0].Result.State == OperationState.Succeeded && vm.Rows[1].Result.State == OperationState.Unsupported &&
                vmAccess.Count == 1, "Native direct action isolates a damaged file and publishes the supported PDF once");
        }
        check(originals.All(entry => Hash(entry.Key) == entry.Value), "Every native workflow original remains byte-identical");

        async Task Refusal(string path, ImageFailure expected, string message)
        {
            var rejected = false;
            try { await worker.ProbePdfDarkAsync(new(Guid.NewGuid(), path), default); }
            catch (MediaWorkerException error) when (error.Failure == expected) { rejected = true; }
            check(rejected, message);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // Independently authored minimal PDFs; no reference or customer content is redistributed.
    private static byte[] Fixture(bool[] dark, bool annotation = false)
    {
        var font = 3 + dark.Length * 2;
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {dark.Length} /Kids [{string.Join(' ', Enumerable.Range(0, dark.Length).Select(index => $"{3 + index * 2} 0 R"))}] >>" };
        for (var index = 0; index < dark.Length; index++)
        {
            var content = (dark[index] ? "0.070588 0.07451 0.078431 rg\n" : "1 1 1 rg\n") + "0 0 420 300 re f\n" +
                (dark[index] ? "0.956863 0.921569 0.847059 rg\n" : "0 0 0 rg\n") +
                "BT /F1 24 Tf 24 246 Td (Searchable reading text) Tj ET\n" +
                (dark[index] ? "0.956863 0.921569 0.847059 RG\n" : "0.1 0.3 0.7 RG\n") +
                "3 w 24 140 m 350 140 l S\n";
            var extra = annotation ? $" /Annots [{font + 1} 0 R]" : "";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 300] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {4 + index * 2} 0 R{extra} >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        if (annotation)
            objects.Add("<< /Type /Annot /Subtype /Text /Rect [20 20 40 40] /Contents (Retain this note) >>");
        var document = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var item in objects)
        {
            offsets.Add(document.Length);
            document.Append($"{offsets.Count - 1} 0 obj\n{item}\nendobj\n");
        }
        var xref = document.Length;
        document.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) document.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        document.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(document.ToString());
    }

    private sealed class Access : IOperationAccess
    {
        public int Count { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) =>
            Task.FromResult(new OperationAccessStatus(true, "Allowed for disposable tests"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDark confirmed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Count++;
            return Task.FromResult(new OperationAdmission(new(true, "Allowed for disposable tests"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new NotSupportedException();
        public Task<OperationAdmission> AdmitOptimizationAsync(ContextSuite.Core.Images.ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Dark-mode conversion cannot recycle originals.");
    }
}
