using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Svg;

namespace ContextSuite.Application.Infrastructure;

internal sealed record SvgBatchExecution(OperationAdmission Admission, IReadOnlyList<FileResult> Results);

internal sealed class SvgConversionExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access)
{
    public async Task<SvgBatchExecution> ExecuteAsync(ConfirmedSvgConversion confirmed,
        Action<SvgSourceFacts, FileResult>? report, CancellationToken token)
    {
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<SvgBatchExecution> ExecuteAdmittedAsync(ConfirmedSvgConversion confirmed, OperationAdmission admission,
        Action<SvgSourceFacts, FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("SVG admission belongs to another batch.");
        var results = new List<FileResult>();
        foreach (var source in confirmed.Plan.Sources)
        {
            FileResult result;
            if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
            else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "SVG conversion cancelled before this file.");
            else
            {
                OutputReservation? reservation = null;
                try
                {
                    report?.Invoke(source, new(source.Path, OperationState.Running, "Preparing image copy"));
                    var id = Guid.NewGuid();
                    reservation = await publisher.ReserveAsync(new(id, source.Path, confirmed.Plan.Options.Extension, confirmed.Plan.Settings), token);
                    if (!string.Equals(reservation.Record.Source.Sha256, source.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        reservation.Record.Source.Length != source.FileBytes)
                        throw new InvalidDataException("SVG changed after the size choice. Start a new Convert command.");
                    var converted = await worker.ConvertSvgAsync(new(source, confirmed.Plan.Options, id, reservation.TemporaryPath), token);
                    var size = confirmed.Plan.Options.GetSize(source);
                    if (converted.SourceId != source.ItemId || converted.Width != size.Width || converted.Height != size.Height ||
                        converted.Format != confirmed.Plan.Options.Target ||
                        converted.OutputBytes is < 1 or > SvgWork.MaximumOutputBytes ||
                        !string.Equals(converted.SourceSha256, source.Sha256, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(converted.FontFingerprint, source.FontFingerprint, StringComparison.OrdinalIgnoreCase) ||
                        converted.Validation.ItemId != id || !converted.Validation.MatchesPlan ||
                        string.IsNullOrWhiteSpace(converted.EngineIdentity))
                        throw new InvalidDataException("SVG rendering changed after planning. Start a new Convert command.");
                    result = (await publisher.PublishAsync(reservation, converted.Validation, token)).ToFileResult() with
                    { EngineIdentity = converted.EngineIdentity };
                }
                catch (OperationCanceledException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Cancelled, "SVG conversion cancelled.") :
                        (await publisher.AbandonAsync(reservation, true)).ToFileResult();
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
                {
                    result = reservation is null ? new(source.Path, OperationState.Failed, "Image copy could not be prepared.") :
                        (await publisher.AbandonAsync(reservation, false)).ToFileResult();
                    if (result.Publication?.IsCommitted != true)
                        result = result with
                        {
                            State = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
                            Message = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
                                ? "This SVG uses a feature that cannot be rendered safely. The original was kept."
                                : error is MediaWorkerException { Failure: ImageFailure.ResourceLimit }
                                    ? "This SVG exceeds the rendering limit. Choose a smaller width or simpler artwork. The original was kept."
                                    : "SVG rendering failed or its source or fonts changed. Start a new Convert command if it changed; the original was kept."
                        };
                }
            }
            results.Add(result);
            report?.Invoke(source, result);
        }
        return new(admission, results);
    }
}
