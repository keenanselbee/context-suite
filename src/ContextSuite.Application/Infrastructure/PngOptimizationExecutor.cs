using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed class PngOptimizationExecutor(WorkerClient worker, OutputPublisher publisher, LocalTrialStore trial)
{
    public async Task<ImageBatchExecution> ExecuteAsync(ConfirmedPngOptimization confirmed,
        Action<PngOptimizationItem, FileResult>? report, CancellationToken cancellationToken)
    {
        var admission = await trial.AdmitAsync(confirmed, cancellationToken);
        var results = new List<FileResult>();
        foreach (var item in confirmed.Plan.Items)
        {
            FileResult result;
            if (!item.CanExecute) result = new(item.Source.Path, OperationState.Unsupported, item.BlockReason!);
            else if (!admission.IsAllowed) result = new(item.Source.Path, OperationState.Failed, admission.Status.Message);
            else if (cancellationToken.IsCancellationRequested) result = new(item.Source.Path, OperationState.Cancelled, "Cancelled before optimization.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Preparing safe output"));
                    var plan = confirmed.Plan;
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path, "png", plan.Settings,
                        plan.ReplaceOriginal, plan.ReplaceOriginal), cancellationToken);
                    if (reservation.Record.Source.Sha256 != item.Source.Sha256)
                        throw new InvalidDataException("Source changed after planning.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, $"Optimizing and verifying {plan.Preset} policy"));
                    var optimized = await worker.OptimizeAsync(new(item.Source, reservation.TemporaryPath, plan.SelectedPolicy), cancellationToken);
                    result = (await publisher.PublishAsync(reservation, optimized.Validation, cancellationToken)).ToFileResult();
                    result = result with { EngineIdentity = optimized.EngineIdentity,
                        Message = result.Message + $" Requested: {plan.Preset}. Used: {optimized.OptimizationMethod}. {optimized.OptimizationReason}" };
                    if (result.Publication is { IsCommitted: true } publication)
                        result = result with { Message = result.Message + $" {publication.SourceBytes:N0} → {publication.OutputBytes:N0} bytes; saved {publication.SourceBytes - publication.OutputBytes:N0} bytes ({100.0 * (publication.SourceBytes - publication.OutputBytes) / publication.SourceBytes:F1}%)." };
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
