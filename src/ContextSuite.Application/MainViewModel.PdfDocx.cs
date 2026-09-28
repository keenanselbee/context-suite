using System.Collections.Immutable;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private async Task ConvertPdfDocxBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken token)
    {
        var sources = new List<PdfDocxSource>();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (!worker.HasPdfDocxConverter || !string.Equals(Path.GetExtension(row.Path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported, "Word conversion requires a supported PDF and the PDF-to-Word engine."));
                continue;
            }
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking PDF for editable Word conversion"));
            try
            {
                var source = await worker.ProbePdfDocxAsync(new(row.ItemId, row.Path), token);
                if (source.NeedsOcr || source.TextCharacters == 0)
                    row.ApplyResult(new(row.Path, OperationState.Unsupported,
                        "This PDF has no usable text on one or more content pages. Scanned pages need OCR before Word conversion. Original kept."));
                else sources.Add(source);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported,
                    "This PDF could not be read for Word conversion. Protected, damaged or oversized PDFs are not supported. Original kept."));
            }
        }
        if (sources.Count == 0) return;
        ConfirmedPdfDocx confirmed;
        try { confirmed = new PdfDocxPlan(request.RequestId, rows[0].Settings, sources.ToImmutableArray()).Confirm(); }
        catch (InvalidDataException)
        {
            foreach (var source in sources) rows.Single(row => row.ItemId == source.ItemId).ApplyResult(new(source.Path,
                OperationState.Unsupported, "Select at most 128 distinct PDFs for Word conversion. Originals kept."));
            return;
        }
        var byId = rows.ToDictionary(row => row.ItemId);
        await new PdfDocxExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (source, result) =>
        {
            byId[source.ItemId].ApplyResult(result);
            Summary = $"Converting to Word: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} files finished. Layout may differ from the PDF.";
        }, token);
    }
}
