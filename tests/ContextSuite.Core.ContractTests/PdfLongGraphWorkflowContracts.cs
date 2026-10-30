using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfLongGraphWorkflowContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var sources = new List<PdfFileSource>(); var hashes = new Dictionary<string, string>(); var timestamps = new Dictionary<string, DateTime>();
        var records = Path.Combine(scratch, "records");
        var clock = new Clock();
        var access = new Access(new OperationAdmissionService());
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        foreach (var count in new[] { 48, 128, 512 })
        {
            var path = Path.Combine(scratch, $"Outline {count}.pdf");
            var bytes = Create(count);
            check(bytes.Length < PdfFileSource.MaximumFileBytes, $"Long PDF graph: authored {count}-page fixture stays below the unchanged input limit");
            await File.WriteAllBytesAsync(path, bytes);
            hashes[path] = Convert.ToHexString(SHA256.HashData(bytes)); timestamps[path] = File.GetLastWriteTimeUtc(path);
            var source = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), path), default);
            check(source.Facts.PageCount == count && source.Facts.OutlineCount == count,
                $"Long PDF graph: independent worker probe reports {count} pages and linked bookmarks");
            sources.Add(source);
        }
        var workerId = worker.ProcessId;
        check(access.Admissions == 0 && !File.Exists(Path.Combine(scratch, "trial", "trial.json")), "Long PDF graph: complete planning probes leave trial unstarted");
        var confirmed = PdfOptimizationPlan.Create(Guid.NewGuid(), sources, new("optimize", new(ReplaceOriginals: true))).Confirm();
        var executor = new PdfOptimizationExecutor(worker, publisher, access);
        var completed = await executor.ExecuteAsync(confirmed, (_, row) =>
        { if (row.State == OperationState.Succeeded) clock.Now = clock.Now.AddDays(8); }, default);
        check(access.Admissions == 1 && completed.Admission.IsAllowed && completed.Results.Count == sources.Count &&
            completed.Results.All(row => row.Publication?.Outcome == PublicationOutcome.CopyCreated),
            "Long PDF graph: one admission publishes all linked-graph copies after clock changes despite overwrite setting");
        var pairs = new List<object>();
        var repeatAccess = new Access(new OperationAdmissionService());
        var repeatExecutor = new PdfOptimizationExecutor(worker, publisher, repeatAccess);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index]; var result = completed.Results[index]; var outputPath = result.Publication!.OutputPath!;
            var original = await File.ReadAllBytesAsync(source.Path); var output = await File.ReadAllBytesAsync(outputPath);
            var outputSource = await worker.ProbePdfFileAsync(new(Guid.NewGuid(), outputPath), default);
            check(output.Length < original.Length && outputSource.Facts.PageCount == source.Facts.PageCount &&
                outputSource.Facts.OutlineCount == source.Facts.OutlineCount,
                $"Long PDF graph: {source.Facts.PageCount}-page smaller output retains page and bookmark counts");
            check(Convert.ToHexString(SHA256.HashData(original)) == hashes[source.Path] && File.GetLastWriteTimeUtc(source.Path) == timestamps[source.Path],
                "Long PDF graph: original bytes and timestamp are unchanged");
            var fileCount = Directory.GetFiles(scratch, "*.pdf").Length;
            var repeat = await repeatExecutor.ExecuteAsync(PdfOptimizationPlan.Create(Guid.NewGuid(), [outputSource], new("optimize", new())).Confirm(), null, default);
            check(repeat.Results.Single().State == OperationState.Unchanged && Directory.GetFiles(scratch, "*.pdf").Length == fileCount,
                "Long PDF graph: repeated optimization creates no duplicate");
            pairs.Add(new { Pages = source.Facts.PageCount, Source = source.Path, Output = outputPath, SourceBytes = original.Length, OutputBytes = output.Length,
                SourceSha256 = hashes[source.Path], OutputSha256 = Convert.ToHexString(SHA256.HashData(output)), result.EngineIdentity });
        }
        check(worker.ProcessId == workerId && Directory.GetFiles(records).Length == 0 && !Directory.GetFiles(scratch, ".context-suite-*.tmp").Any(),
            "Long PDF graph: sequential worker and publication cleanup survive all completed/repeat operations");
        await File.WriteAllTextAsync(Path.Combine(scratch, "pdf-long-graph-manifest.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            Scope = "Independently authored ASCII PDF fixtures; retain source/output pairs for independent all-page text and pixel comparison.",
            Policy = PdfOptimizationPlan.Policy,
            Pairs = pairs
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] Create(int count)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R /PageMode /UseOutlines >>",
            "<< /Type /Pages /Count " + count + " /Kids [" + string.Join(" ", Enumerable.Range(0, count).Select(index => (5 + index * 3) + " 0 R")) + "] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Type /Outlines /Count {count} /First 7 0 R /Last {7 + (count - 1) * 3} 0 R >>"
        };
        for (var index = 0; index < count; index++)
        {
            var page = 5 + index * 3; var outline = page + 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {page + 1} 0 R >>");
            var content = "BT /F1 12 Tf 72 720 Td (Authored long graph page " + (index + 1) + " of " + count + ") Tj ET\n" +
                string.Concat(Enumerable.Repeat("q 1 0 0 1 0 0 cm Q\n", 128));
            objects.Add("<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream");
            objects.Add($"<< /Title (Authored bookmark {index + 1}) /Parent 4 0 R /Dest [{page} 0 R /Fit]" +
                (index > 0 ? $" /Prev {outline - 3} 0 R" : "") + (index + 1 < count ? $" /Next {outline + 3} 0 R" : "") + " >>");
        }
        objects.Add("<< /AuthoredNote (Unreferenced ordinary information must survive optimization) >>");
        using var output = new MemoryStream(); var offsets = new List<long> { 0 };
        Write("%PDF-1.7\n%Authored ASCII compatibility fixture\n");
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position); Write((index + 1) + " 0 obj\n" + objects[index] + "\nendobj\n");
        }
        var xref = output.Position;
        Write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] >>\nstartxref\n" + xref + "\n%%EOF\n");
        return output.ToArray();

        void Write(string text) { output.Write(Encoding.ASCII.GetBytes(text)); }
    }
    private sealed class Access(IOperationAccess inner) : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => inner.ReadAccessAsync(token);
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected PNG admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken token)
        { Admissions++; return inner.AdmitOptimizationAsync(confirmed, token); }
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF optimization must publish copies without recycling originals.");
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => 0;
    }
}
