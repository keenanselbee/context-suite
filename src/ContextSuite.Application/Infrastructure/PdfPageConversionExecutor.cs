using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal sealed record PdfPageExecution(Guid SourceId, int PageIndex, FileResult Result);
internal sealed record PdfPageBatchExecution(OperationAdmission Admission, IReadOnlyList<PdfPageExecution> Pages);

// Each page has its own existing publication journal. Completed copies survive
// cancellation/failure; no multi-file rollback or source replacement is implied.
internal sealed class PdfPageConversionExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<PdfPageBatchExecution> ExecuteAsync(ConfirmedPdfPageConversion confirmed,
        Action<PdfRasterSource, int, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<PdfPageBatchExecution> ExecuteAdmittedAsync(ConfirmedPdfPageConversion confirmed, OperationAdmission admission,
        Action<PdfRasterSource, int, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("PDF page admission belongs to another batch.");
        var results = new List<PdfPageExecution>();
        long outputBytes = 0;
        foreach (var source in confirmed.Plan.Sources)
        {
            var failedSource = false;
            foreach (var page in source.Document.Pages)
            {
                FileResult result;
                if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
                else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Page not converted after cancellation.");
                else if (failedSource) result = new(source.Path, OperationState.Failed, "Page not converted because an earlier page failed. Completed copies were kept.");
                else
                {
                    OutputReservation? reservation = null;
                    try
                    {
                        report?.Invoke(source, page.Index, new(source.Path, OperationState.Running, "Preparing page copy"));
                        var id = Guid.NewGuid();
                        reservation = await publisher.ReserveAsync(new(id, source.Path, "png", confirmed.Plan.Settings,
                            PageNumber: page.Index + 1), token);
                        if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                            throw new InvalidDataException("PDF changed after planning.");
                        report?.Invoke(source, page.Index, new(source.Path, OperationState.Running, "Rendering and validating page"));
                        var rendered = await worker.RenderPdfPageAsync(new(source, page.Index, id, reservation.TemporaryPath), token);
                        if (outputBytes + rendered.OutputBytes > 2L * 1024 * 1024 * 1024)
                            throw new InvalidDataException("PDF page batch exceeds its output budget.");
                        result = (await publisher.PublishAsync(reservation, rendered.Validation, token)).ToFileResult() with { EngineIdentity = rendered.EngineIdentity };
                        if (result.Publication?.IsCommitted == true) outputBytes += rendered.OutputBytes;
                    }
                    catch (OperationCanceledException)
                    {
                        result = reservation is null ? new(source.Path, OperationState.Cancelled, "Page conversion cancelled.") :
                            (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                        InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                    {
                        result = reservation is null ? new(source.Path, OperationState.Failed, "Page output could not be prepared.") :
                            (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                        if (result.Publication?.IsCommitted != true)
                            result = result with { State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                                Message = "Page conversion failed. The original PDF and completed page copies were kept." };
                    }
                }
                if (result.State is OperationState.Failed or OperationState.Unsupported) failedSource = true;
                results.Add(new(source.ItemId, page.Index, result));
                report?.Invoke(source, page.Index, result);
            }
        }
        return new(admission, results);
    }
}
