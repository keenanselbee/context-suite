using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal sealed class PdfDarkExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task ExecuteAsync(ConfirmedPdfDark confirmed, Action<PdfDarkSource, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        var admission = await access.AdmitConversionAsync(confirmed, token);
        if (admission.BatchId != confirmed.Plan.BatchId)
            throw new InvalidDataException("PDF dark-mode admission belongs to another batch.");
        foreach (var source in confirmed.Plan.Sources)
        {
            FileResult result;
            if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Dark-mode conversion cancelled. Original kept.");
            else if (source.Facts.ChangedPages == 0) result = new(source.Path, OperationState.Unchanged, "Already uses the dark palette. Original kept.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(source, new(source.Path, OperationState.Running, "Preparing dark-mode PDF copy"));
                    reservation = await publisher.ReserveAsync(new(source.ItemId, source.Path, "pdf", confirmed.Plan.Settings,
                        PdfDark: true), token);
                    if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                        throw new InvalidDataException("PDF changed after dark-mode inspection.");
                    var converted = await worker.ConvertPdfDarkAsync(new(source, reservation.TemporaryPath), token);
                    var publication = await publisher.PublishAsync(reservation, converted.Validation, token);
                    if (publication.IsCommitted && converted.Facts.PreservedImages > 0)
                        publication = publication with { MetadataWarning = true,
                            Message = "Dark-mode PDF copy saved. Some images retain their original colors." };
                    if (publication.IsCommitted && converted.Facts.RecoveredImages > 0)
                        publication = publication with { MetadataWarning = true,
                            Message = "Dark-mode PDF copy saved. Recovered inconsistent image dimensions using the JPEG and mask data." +
                                (converted.Facts.PreservedImages > 0 ? " Some images retain their original colors." : "") };
                    if (publication.IsCommitted && converted.Facts.RecoveredCoordinates > 0)
                        publication = publication with { MetadataWarning = true,
                            Message = publication.Message + " Recovered malformed drawing coordinates using the validated zero interpretation." };
                    result = publication.ToFileResult() with { EngineIdentity = converted.EngineIdentity };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Cancelled, "Dark-mode conversion cancelled. Original kept.") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Failed, "Dark-mode PDF output could not be prepared. Original kept.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with { State = error is MediaWorkerException unsupported && PdfDarkFailures.IsUnsupported(unsupported.Failure)
                            ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException known && PdfDarkFailures.IsUnsupported(known.Failure)
                                ? known.Message
                                : "Dark-mode PDF conversion failed safely. Check the source/output folder and retry." };
                }
            }
            report?.Invoke(source, result);
        }
    }
}
