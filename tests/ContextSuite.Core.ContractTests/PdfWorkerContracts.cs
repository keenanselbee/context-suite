using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class PdfWorkerContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var paths = new[] { "authored original ü.pdf", "encrypted.pdf", "owner-protected.pdf", "broken-xref.pdf", "optimized copy ü.pdf" }
            .Select(name => Path.Combine(fixtures, name)).ToArray();
        var hashes = paths.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        var unknown = Path.Combine(scratch, "unknown.bin"); await File.WriteAllBytesAsync(unknown, [0, 1, 2, 255]);
        var workerRoot = Path.Combine(scratch, "workers");
        await using (var worker = new WorkerClient(executable, workerRoot))
        await using (var view = new MainViewModel(worker, trial: new ForbiddenAccess()))
        {
            check(worker.HasPdfProbe, "PDF worker: isolated optional engine is present");
            view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [.. paths, unknown]));
            await view.WaitForIdleAsync();
            check(view.Rows.All(row => row.Result.State == OperationState.Succeeded), "PDF worker: mixed batch keeps usable read-only results");
            check(view.Rows[0].Analysis!.Facts.Single(fact => fact.Id == "document.pages").Integer == 2 &&
                view.Rows[0].Analysis!.Facts.Single(fact => fact.Id == "pdf.attachments").Integer == 1,
                "PDF worker: actual Analyze delivers pages and attachment facts");
            check(view.Rows[1].Analysis!.Facts.Single(fact => fact.Id == "document.encryption").Boolean == true &&
                view.Rows[1].Analysis!.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable,
                "PDF worker: password-required input has known encryption and unavailable pages");
            check(view.Rows[2].Analysis!.Facts.Single(fact => fact.Id == "document.pages").Integer == 2 &&
                view.Rows[2].Analysis!.Facts.Single(fact => fact.Id == "document.encryption").Boolean == true,
                "PDF worker: readable owner-protected encryption remains explicit");
            check(view.Rows[3].Analysis!.Warnings.Any(warning => warning.Contains("Deeper PDF")), "PDF worker: malformed PDF falls back to header");
            check(view.Rows[4].Analysis!.Facts.Single(fact => fact.Id == "document.pages").Integer == 2 && view.Rows[5].Analysis!.Identity.FormatId is null,
                "PDF worker: failure does not stop later valid or unknown files");
            check(view.Rows.All(row => row.Result.Publication is null && !row.HasOutput), "PDF worker: Analyze uses no paid admission or publication");
            check(!Directory.EnumerateFiles(workerRoot, "*.pdf", SearchOption.AllDirectories).Any(), "PDF worker: temporary snapshots removed before results return");
            await File.WriteAllTextAsync(Path.Combine(scratch, "analysis-results.txt"), string.Join("\n\n", view.Rows.Select(row => row.AnalysisSummary + "\n" + row.AnalysisDetails)));
        }
        check(!Directory.EnumerateDirectories(workerRoot).Any(), "PDF worker: shutdown removes owned worker scratch directory");
        check(paths.Select((path, index) => SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hashes[index])).All(value => value),
            "PDF worker: all generated original hashes preserved");
    }

    private sealed class ForbiddenAccess : IOperationAccess
    {
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Analyze accessed licensing.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted conversion.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken) => throw new InvalidOperationException("Analyze admitted optimization.");
    }
}
