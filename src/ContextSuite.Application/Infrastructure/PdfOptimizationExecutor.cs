using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed record PdfBatchExecution(OperationAdmission Admission, IReadOnlyList<FileResult> Results);

internal sealed class PdfOptimizationExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<PdfBatchExecution> ExecuteAsync(ConfirmedPdfOptimization confirmed,
        Action<PdfOptimizationItem, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitOptimizationAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<PdfBatchExecution> ExecuteAdmittedAsync(ConfirmedPdfOptimization confirmed, OperationAdmission admission,
        Action<PdfOptimizationItem, FileResult>? report, CancellationToken token)
    {
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("PDF admission belongs to another batch.");
        var results = new List<FileResult>();
        foreach (var item in confirmed.Plan.Items)
        {
            FileResult result;
            if (!item.CanExecute) result = new(item.Source.Path, OperationState.Unsupported, item.BlockReason!);
            else if (!admission.IsAllowed) result = new(item.Source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(item.Source.Path, OperationState.Cancelled, "Cancelled before optimization.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Preparing safe output"));
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path, "pdf", confirmed.Plan.Settings,
                        false, false), token);
                    if (reservation.Record.Source.Sha256 != item.Source.Sha256 || reservation.Record.Source.Length != item.Source.FileBytes)
                        throw new InvalidDataException("PDF source changed after planning.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Optimizing and verifying PDF"));
                    var optimized = await worker.OptimizePdfAsync(new(item.Source, reservation.TemporaryPath), token);
                    result = (await publisher.PublishAsync(reservation, optimized.Validation, token)).ToFileResult() with { EngineIdentity = optimized.EngineIdentity };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Cancelled, "Cancelled") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Failed, "PDF output could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                    {
                        var unsupported = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput };
                        result = result with { State = unsupported ? OperationState.Unsupported : OperationState.Failed,
                            Message = unsupported ? "This PDF cannot be safely optimized. The original was kept." :
                                "PDF optimization failed safely. Check the source/output folder and retry." };
                    }
                }
            }
            results.Add(result);
            report?.Invoke(item, result);
        }
        return new(admission, results);
    }
}
