using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed record ImageBatchExecution(TrialAdmission Admission, IReadOnlyList<FileResult> Results);

// The only application path from confirmed image plans to final output publication.
// Access is admitted once, before any output reservation; an admitted batch can finish after expiry.
internal sealed class ImageBatchExecutor(WorkerClient worker, OutputPublisher publisher, LocalTrialStore trial)
{
    public async Task<ImageBatchExecution> ExecuteAsync(ConfirmedImageBatch confirmed,
        Action<ImageItemPlan, FileResult>? report, CancellationToken cancellationToken)
    {
        var admission = await trial.AdmitAsync(confirmed, cancellationToken);
        var results = new List<FileResult>();
        foreach (var item in confirmed.Plan.Items)
        {
            FileResult result;
            if (!item.CanExecute) result = new(item.Source.Path, OperationState.Unsupported, item.BlockReason!);
            else if (!admission.IsAllowed) result = new(item.Source.Path, OperationState.Failed, admission.Status.Message);
            else if (cancellationToken.IsCancellationRequested) result = new(item.Source.Path, OperationState.Cancelled, "Cancelled before conversion.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Preparing safe output"));
                    var plan = confirmed.Plan;
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path,
                        plan.Options.Extension, plan.Settings, plan.ReplaceOriginal, plan.ReplaceOriginal, Dds: plan.Options.OutputRepresentation), cancellationToken);
                    // Reservation independently fingerprints the source; both that fingerprint and the planner's
                    // digest must still match before the adapter is permitted to write its temporary output.
                    if (!string.Equals(reservation.Record.Source.Sha256, item.Source.Sha256, StringComparison.Ordinal))
                        throw new InvalidDataException("The source changed after planning. Select it again.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Encoding and validating output"));
                    var converted = await worker.ConvertAsync(new(item, plan.Options, reservation.TemporaryPath), cancellationToken);
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Publishing validated output"));
                    result = (await publisher.PublishAsync(reservation, converted.Validation, cancellationToken)).ToFileResult();
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Cancelled, "Cancelled") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Failed, "Output could not be prepared. Check the source and output folder.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    // Do not replace a committed publication's success or its cleanup/recovery warning.
                    if (result.Publication?.IsCommitted != true)
                        result = result with
                        {
                            State = error is MediaWorkerException { Failure: ContextSuite.Core.Images.ImageFailure.UnsupportedInput }
                                ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException ? error.Message : "Conversion failed safely. Review the source/output location and retry the selection."
                        };
                }
            }
            results.Add(result);
            report?.Invoke(item, result);
        }
        return new(admission, results);
    }
}
