using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal sealed class PdfDocxExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task ExecuteAsync(ConfirmedPdfDocx confirmed, Action<PdfDocxSource, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        var admission = await access.AdmitConversionAsync(confirmed, token);
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("PDF-to-Word admission belongs to another batch.");
        foreach (var source in confirmed.Plan.Sources)
        {
            FileResult result;
            if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Word conversion cancelled. Original kept.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(source, new(source.Path, OperationState.Running, "Creating an editable Word copy; layout may differ"));
                    var id = Guid.NewGuid();
                    reservation = await publisher.ReserveAsync(new(id, source.Path, "docx", confirmed.Plan.Settings), token);
                    if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                        throw new InvalidDataException("PDF changed after inspection.");
                    var converted = await worker.ConvertPdfDocxAsync(new(source, id, reservation.TemporaryPath), token);
                    result = (await publisher.PublishAsync(reservation, converted.Validation, token)).ToFileResult() with { EngineIdentity = converted.EngineIdentity };
                    if (result.State == OperationState.Succeeded && converted.FigureCount > 0)
                        result = result with { Message = "Word copy saved. Complex figures, their labels and stamp appearances remain images; other text is editable." };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Cancelled, "Word conversion cancelled. Original kept.") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Failed, "Word output could not be prepared. Original kept.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with
                        {
                            State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
                                ? "This PDF contains content that cannot yet be preserved in an editable Word copy. Original kept."
                                : "Word conversion could not be completed within its validation or processing limits. Original kept."
                        };
                }
            }
            report?.Invoke(source, result);
        }
    }
}
