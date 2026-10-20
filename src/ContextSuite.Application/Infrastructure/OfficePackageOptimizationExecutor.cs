using ContextSuite.Core.Office;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed class OfficePackageOptimizationExecutor(WorkerClient worker, OutputPublisher publisher)
{
    internal async Task<ImageBatchExecution> ExecuteAdmittedAsync(ConfirmedOfficePackageOptimization confirmed,
        OperationAdmission admission, Action<OfficePackageSource, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Optimization admission belongs to another batch.");
        var results = new List<FileResult>();
        foreach (var source in confirmed.Plan.Sources)
        {
            FileResult result;
            if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Cancelled before optimization.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(source, new(source.Path, OperationState.Running, "Preparing safe output"));
                    reservation = await publisher.ReserveAsync(new(source.ItemId, source.Path, source.Format,
                        confirmed.Plan.Settings, false, false), token);
                    if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                        throw new InvalidDataException("Source changed after planning.");
                    report?.Invoke(source, new(source.Path, OperationState.Running, "Recompressing and verifying document"));
                    var optimized = await worker.OptimizeOfficePackageAsync(new(source, reservation.TemporaryPath), token);
                    result = (await publisher.PublishAsync(reservation, optimized.Validation, token)).ToFileResult()
                        with { EngineIdentity = optimized.EngineIdentity };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Cancelled, "Cancelled") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Failed, "Output could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with
                        {
                            State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
                                ? "This document package cannot be safely optimized. The original was kept."
                                : "Document optimization failed safely. Check the source/output folder and retry."
                        };
                }
            }
            results.Add(result);
            report?.Invoke(source, result);
        }
        return new(admission, results);
    }
}
