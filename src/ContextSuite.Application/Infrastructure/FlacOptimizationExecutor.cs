using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed record AudioBatchExecution(OperationAdmission Admission, IReadOnlyList<FileResult> Results);

internal sealed class FlacOptimizationExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<AudioBatchExecution> ExecuteAsync(ConfirmedFlacOptimization confirmed,
        Action<FlacOptimizationItem, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitOptimizationAsync(confirmed, token);
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Audio admission belongs to another batch.");
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
                    var plan = confirmed.Plan;
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Preparing safe output"));
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path, "flac", plan.Settings,
                        plan.ReplaceOriginal, plan.ReplaceOriginal), token);
                    if (reservation.Record.Source.Sha256 != item.Source.Sha256 || reservation.Record.Source.Length != item.Source.FileBytes)
                        throw new InvalidDataException("Audio source changed after planning.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Optimizing and verifying lossless FLAC"));
                    var optimized = await worker.OptimizeFlacAsync(new(item.Source, reservation.TemporaryPath), token);
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
                    result = reservation is null ? new(item.Source.Path, OperationState.Failed, "Audio output could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with { State = OperationState.Failed, Message = "FLAC optimization failed safely. Check the source/output folder and retry." };
                }
            }
            results.Add(result);
            report?.Invoke(item, result);
        }
        return new(admission, results);
    }
}
