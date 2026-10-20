using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed class LosslessImageOptimizationExecutor(WorkerClient worker, OutputPublisher publisher)
{
    internal async Task<ImageBatchExecution> ExecuteAdmittedAsync(ConfirmedLosslessImageOptimization confirmed,
        OperationAdmission admission, Action<LosslessImageOptimizationItem, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Optimization admission belongs to another batch.");
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
                    var extension = Path.GetExtension(item.Source.Path).TrimStart('.').ToLowerInvariant();
                    if (!(item.Source.Format == ImageFormat.Jpeg && extension is "jpg" or "jpeg" or "jpe" or "jfif") &&
                        !(item.Source.Format == ImageFormat.WebP && extension == "webp") &&
                        !(item.Source.Format == ImageFormat.Tga && extension == "tga"))
                        throw new InvalidDataException("Image filename does not match its format.");
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path, extension,
                        confirmed.Plan.Settings, false, false), token);
                    if (reservation.Record.Source.Sha256 != item.Source.Sha256 || reservation.Record.Source.Length != item.Source.FileBytes)
                        throw new InvalidDataException("Source changed after planning.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Optimizing and verifying image"));
                    var optimized = await worker.OptimizeLosslessImageAsync(new(item.Source, reservation.TemporaryPath,
                        LosslessImageOptimizationPlan.PolicyFor(item.Source.Format, confirmed.Plan.Preset)), token);
                    result = (await publisher.PublishAsync(reservation, optimized.Validation, token)).ToFileResult()
                        with { EngineIdentity = optimized.EngineIdentity };
                    result = result with { Message = result.Message +
                        $" Requested: {confirmed.Plan.Preset}. Used: {optimized.OptimizationMethod}. {optimized.OptimizationReason}" };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Cancelled, "Cancelled") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Failed, "Output could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with
                        {
                            State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException ? error.Message : "Optimization failed safely. Check the source/output folder and retry."
                        };
                }
            }
            results.Add(result);
            report?.Invoke(item, result);
        }
        return new(admission, results);
    }
}
