using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed class AudioConversionExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<AudioBatchExecution> ExecuteAsync(ConfirmedAudioConversion confirmed,
        Action<AudioConversionItem, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<AudioBatchExecution> ExecuteAdmittedAsync(ConfirmedAudioConversion confirmed, OperationAdmission admission,
        Action<AudioConversionItem, FileResult>? report, CancellationToken token)
    {
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Audio admission belongs to another batch.");
        var results = new List<FileResult>();
        foreach (var item in confirmed.Plan.Items)
        {
            FileResult result;
            if (item.BlockReason is not null || item.Encoding is null)
                result = new(item.Source.Path, OperationState.Unsupported, item.BlockReason ?? "This audio cannot use the selected format.");
            else if (item.Encoding.AlreadyTarget)
                result = new(item.Source.Path, OperationState.Unchanged, "Already the selected audio format; original kept.");
            else if (!admission.IsAllowed) result = new(item.Source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(item.Source.Path, OperationState.Cancelled, "Cancelled before conversion.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    var plan = confirmed.Plan;
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Preparing safe output"));
                    reservation = await publisher.ReserveAsync(new(item.Source.ItemId, item.Source.Path, item.Encoding.Extension, plan.Settings,
                        plan.ReplaceOriginal, plan.ReplaceOriginal), token);
                    if (reservation.Record.Source.Sha256 != item.Source.Sha256 || reservation.Record.Source.Length != item.Source.FileBytes)
                        throw new InvalidDataException("Audio source changed after planning.");
                    report?.Invoke(item, new(item.Source.Path, OperationState.Running, "Converting and verifying audio"));
                    var converted = await worker.ConvertAudioAsync(new(item.Source, reservation.TemporaryPath, plan.Target, confirmed.AcceptedConsent), token);
                    result = (await publisher.PublishAsync(reservation, converted.Validation, token)).ToFileResult() with { EngineIdentity = converted.EngineIdentity };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Cancelled, "Cancelled") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(item.Source.Path, OperationState.Failed, "Audio output could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                    {
                        var unsupported = error is NotSupportedException or MediaWorkerException { Failure: ImageFailure.UnsupportedInput };
                        result = result with { State = unsupported ? OperationState.Unsupported : OperationState.Failed,
                            Message = unsupported ? "This audio has information or a feature that conversion cannot preserve yet. The original was kept." :
                                "Audio conversion failed safely. Check the source and output folder, then try again." };
                    }
                }
            }
            results.Add(result); report?.Invoke(item, result);
        }
        return new(admission, results);
    }
}
