using System.Collections.Immutable;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private async Task ConvertPdfDarkBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken token)
    {
        var pdfRows = rows.Where(row => string.Equals(Path.GetExtension(row.Path), ".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (pdfRows.Length > 128 || pdfRows.Select(row => Path.GetFullPath(row.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pdfRows.Length)
        {
            foreach (var row in rows)
                row.ApplyResult(new(row.Path, OperationState.Unsupported,
                    pdfRows.Contains(row) ? "Select at most 128 distinct PDFs for dark mode. Originals kept." :
                    "PDF (Dark mode) accepts PDF files only. Original kept."));
            return;
        }
        var sources = new List<PdfDarkSource>();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(row.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported, "PDF (Dark mode) accepts PDF files only. Original kept."));
                continue;
            }
            if (!worker.HasPdfDarkConverter)
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported, "The PDF dark-mode engine is unavailable. Original kept."));
                continue;
            }
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking PDF for dark-mode conversion"));
            try
            {
                sources.Add(await worker.ProbePdfDarkAsync(new(row.ItemId, row.Path), token));
                if (sources.Sum(source => source.FileBytes) > 2L * 1024 * 1024 * 1024)
                {
                    foreach (var pdfRow in pdfRows)
                        pdfRow.ApplyResult(new(pdfRow.Path, OperationState.Unsupported,
                            "This PDF selection exceeds the 2 GB dark-mode batch limit. Select fewer files. Originals kept."));
                    return;
                }
            }
            catch (MediaWorkerException error) when (PdfDarkFailures.IsUnsupported(error.Failure))
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported,
                    error.Message));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                row.ApplyResult(new(row.Path, OperationState.Failed,
                    "PDF dark-mode inspection could not finish. Check the source file and retry. Original kept."));
            }
        }
        if (sources.Count == 0) return;
        token.ThrowIfCancellationRequested();
        ConfirmedPdfDark confirmed;
        try { confirmed = new PdfDarkPlan(request.RequestId, rows[0].Settings, sources.ToImmutableArray()).Confirm(); }
        catch (InvalidDataException)
        {
            foreach (var source in sources) rows.Single(row => row.ItemId == source.ItemId).ApplyResult(new(source.Path,
                OperationState.Unsupported, "Select at most 128 distinct PDFs totaling 2 GB for dark mode. Originals kept."));
            return;
        }
        var byId = rows.ToDictionary(row => row.ItemId);
        token.ThrowIfCancellationRequested();
        await new PdfDarkExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (source, result) =>
        {
            byId[source.ItemId].ApplyResult(result);
            Summary = $"Converting to dark mode: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} files finished.";
        }, token);
    }
}
