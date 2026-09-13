using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal sealed record ImagePdfExecution(OperationAdmission Admission, FileResult Result);

internal sealed class ImagePdfExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<ImagePdfExecution> ExecuteAsync(ConfirmedImagePdf confirmed, Action<FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm(true);
        var source = confirmed.Plan.Pages[0].Source.Path;
        if (!worker.HasImagePdfConverter)
        {
            var unavailable = new OperationAdmission(new(false, "PDF conversion is unavailable in this build."), confirmed.Plan.BatchId);
            var result = new FileResult(source, OperationState.Unsupported, unavailable.Status.Message);
            report?.Invoke(result);
            return new(unavailable, result);
        }
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<ImagePdfExecution> ExecuteAdmittedAsync(ConfirmedImagePdf confirmed, OperationAdmission admission,
        Action<FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm(true);
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Combined PDF admission belongs to another batch.");
        var path = confirmed.Plan.Pages[0].Source.Path;
        FileResult result;
        if (!admission.IsAllowed) result = new(path, OperationState.Failed, admission.Status.Message);
        else
        {
            OutputReservation? reservation = null;
            try
            {
                report?.Invoke(new(path, OperationState.Running, "Preparing PDF copy"));
                var id = Guid.NewGuid();
                reservation = await publisher.ReserveImagePdfAsync(id, confirmed, token);
                report?.Invoke(new(path, OperationState.Running, "Creating and validating PDF"));
                var converted = await worker.ConvertImagesToPdfAsync(new(confirmed.Plan, id, reservation.TemporaryPath, true), token);
                result = (await publisher.PublishAsync(reservation, converted.Validation, token)).ToFileResult() with { EngineIdentity = converted.EngineIdentity };
            }
            catch (OperationCanceledException)
            {
                result = reservation is null ? new(path, OperationState.Cancelled, "PDF conversion cancelled. Originals kept.") :
                    (await publisher.AbandonAsync(reservation, true)).ToFileResult();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                result = reservation is null ? new(path, OperationState.Failed, "PDF output could not be prepared. Originals kept.") :
                    (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                if (result.Publication?.IsCommitted != true)
                    result = result with { State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                        Message = error is MediaWorkerException { Failure: ImageFailure.ResourceLimit }
                            ? "PDF conversion reached a processing limit. Select fewer or smaller images and try again. All originals were kept."
                            : "PDF conversion failed. No images were omitted and all originals were kept." };
            }
        }
        report?.Invoke(result);
        return new(admission, result);
    }
}
