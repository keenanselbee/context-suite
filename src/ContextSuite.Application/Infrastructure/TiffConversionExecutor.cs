using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Tiff;

namespace ContextSuite.Application.Infrastructure;

internal sealed record TiffPageExecution(Guid SourceId, int PageIndex, FileResult Result);
internal sealed record TiffBatchExecution(OperationAdmission Admission, IReadOnlyList<TiffPageExecution> Pages);

internal sealed class TiffConversionExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<TiffBatchExecution> ExecuteAsync(ConfirmedTiffConversion confirmed,
        Action<TiffSourceFacts, int, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<TiffBatchExecution> ExecuteAdmittedAsync(ConfirmedTiffConversion confirmed, OperationAdmission admission,
        Action<TiffSourceFacts, int, FileResult>? report, CancellationToken token,
        IReadOnlyDictionary<(Guid SourceId, int PageIndex), long>? completedOutputs = null)
    {
        const long maximumOutputBytes = 2L * 1024 * 1024 * 1024;
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("TIFF admission belongs to another batch.");
        var sources = confirmed.Plan.Sources.ToDictionary(source => source.ItemId);
        if (completedOutputs?.Any(output => !sources.TryGetValue(output.Key.SourceId, out var source) ||
            output.Key.PageIndex < (confirmed.Plan.Options.Target == TiffTarget.Pdf ? -1 : 0) ||
            output.Key.PageIndex >= source.Pages.Length || confirmed.Plan.Options.Target == TiffTarget.Pdf && output.Key.PageIndex != -1 ||
            output.Value <= 0) == true)
            throw new InvalidDataException("Completed TIFF pages do not belong to this selection.");
        var results = new List<TiffPageExecution>();
        long outputBytes = 0;
        if (completedOutputs is not null)
            foreach (var bytes in completedOutputs.Values)
            {
                if (bytes > maximumOutputBytes - outputBytes)
                    throw new InvalidDataException("Completed TIFF outputs exceed the batch output budget.");
                outputBytes += bytes;
            }
        foreach (var source in confirmed.Plan.Sources)
        {
            var pdf = confirmed.Plan.Options.Target == TiffTarget.Pdf;
            foreach (var index in pdf ? new[] { -1 } : Enumerable.Range(0, source.Pages.Length))
            {
                if (completedOutputs?.ContainsKey((source.ItemId, index)) == true) continue;
                FileResult result;
                var unsupported = pdf ? source.Pages.FirstOrDefault(page => page.GetUnsupportedReason(confirmed.Plan.Options.Target) is not null) :
                    source.Pages[index].GetUnsupportedReason(confirmed.Plan.Options.Target) is not null ? source.Pages[index] : null;
                if (unsupported is not null)
                    result = new(source.Path, OperationState.Unsupported,
                        $"Page {unsupported.Index + 1}: {unsupported.GetUnsupportedReason(confirmed.Plan.Options.Target)} Original and completed copies kept.");
                else if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
                else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Page not converted after cancellation.");
                else
                {
                    OutputReservation? reservation = null;
                    try
                    {
                        if (outputBytes == maximumOutputBytes)
                            throw new InvalidDataException("TIFF batch exceeds its output budget.");
                        report?.Invoke(source, index, new(source.Path, OperationState.Running, "Preparing TIFF output copy"));
                        var id = Guid.NewGuid();
                        reservation = await publisher.ReserveAsync(new(id, source.Path, confirmed.Plan.Options.Extension,
                            confirmed.Plan.Settings, PageNumber: pdf ? null : index + 1), token);
                        if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                            throw new InvalidDataException("TIFF changed after planning.");
                        var converted = await worker.ConvertTiffAsync(new(source, confirmed.Plan.Options, index, id, reservation.TemporaryPath), token);
                        if (converted.OutputBytes > maximumOutputBytes - outputBytes)
                            throw new InvalidDataException("TIFF batch exceeds its output budget.");
                        result = (await publisher.PublishAsync(reservation, converted.Validation, token)).ToFileResult() with
                        { EngineIdentity = converted.EngineIdentity };
                        if (result.Publication?.IsCommitted == true) outputBytes += converted.OutputBytes;
                    }
                    catch (OperationCanceledException)
                    {
                        result = reservation is null ? new(source.Path, OperationState.Cancelled, "TIFF conversion cancelled.") :
                            (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                        InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                    {
                        result = reservation is null ? new(source.Path, OperationState.Failed, "TIFF output could not be prepared.") :
                            (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                        if (result.Publication?.IsCommitted != true)
                            result = result with { State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                                Message = "TIFF conversion failed. The original and completed copies were kept." };
                    }
                }
                results.Add(new(source.ItemId, index, result));
                report?.Invoke(source, index, result);
            }
        }
        return new(admission, results);
    }
}
