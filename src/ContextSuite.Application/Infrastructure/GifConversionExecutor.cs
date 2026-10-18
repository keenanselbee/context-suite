using ContextSuite.Core.Gif;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed class GifConversionExecutor(WorkerClient worker, OutputPublisher publisher)
{
    internal async Task ExecuteAdmittedAsync(ConfirmedGifConversion confirmed, OperationAdmission admission,
        IReadOnlyDictionary<Guid, FileRow> rows, Action<FileRow>? changed, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("GIF admission belongs to another batch.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        long outputBytes = confirmed.Plan.Sources.Sum(source => rows[source.ItemId].GifFrames.Values
            .Where(frame => frame.Publication?.IsCommitted == true).Sum(frame => frame.Publication!.OutputBytes));
        foreach (var source in confirmed.Plan.Sources)
        {
            var row = rows[source.ItemId];
            GifOutputFolder? folder = null;
            try
            {
                if (!admission.IsAllowed) throw new InvalidOperationException(admission.Status.Message);
                budget.Token.ThrowIfCancellationRequested();
                if (source.FrameCount > 1)
                {
                    if (row.GifFolder is { } saved)
                    {
                        try { folder = GifOutputFolder.Open(saved); }
                        catch (InvalidDataException error) { row.BlockGifResume(error.Message); throw; }
                    }
                    else folder = GifOutputFolder.Create(
                        confirmed.Plan.Settings.Preferences.OutputDirectory ?? Path.GetDirectoryName(source.Path)!, source.Path);
                    row.GifFolder = folder.Identity;
                }
                try { await row.VerifyCommittedGifFramesAsync(budget.Token); }
                catch (InvalidDataException error) { row.BlockGifResume(error.Message); throw; }
                var settings = folder is null ? confirmed.Plan.Settings : confirmed.Plan.Settings with
                { Preferences = confirmed.Plan.Settings.Preferences with { OutputDirectory = folder.Identity.Path } };
                foreach (var index in Enumerable.Range(0, source.FrameCount))
                {
                    if (row.GifFrames[index].Publication?.IsCommitted == true) continue;
                    budget.Token.ThrowIfCancellationRequested();
                    OutputReservation? reservation = null;
                    try
                    {
                        row.ApplyGifFrame(index, new(source.Path, OperationState.Running, "Decoding and validating PNG frame"));
                        changed?.Invoke(row);
                        var id = Guid.NewGuid();
                        reservation = await publisher.ReserveAsync(new(id, source.Path, "png", settings,
                            FrameNumber: source.FrameCount > 1 ? index + 1 : null), budget.Token);
                        if (reservation.Record.Source.Sha256 != source.Sha256 || reservation.Record.Source.Length != source.FileBytes)
                            throw new InvalidDataException("The GIF changed after planning. Start a new Convert command.");
                        var result = await worker.ConvertGifAsync(new(source, index, id, reservation.TemporaryPath), budget.Token);
                        if (outputBytes + result.OutputBytes > GifConversionPlan.MaximumOutputBytes)
                            throw new InvalidDataException("GIF frame output exceeds the 2 GiB batch limit. Completed frames were kept.");
                        var published = await publisher.PublishAsync(reservation, result.Validation, budget.Token);
                        if (published.IsCommitted) outputBytes += published.OutputBytes;
                        row.ApplyGifFrame(index, published.ToFileResult() with { EngineIdentity = result.EngineIdentity },
                            result.Validation.Sha256);
                    }
                    catch (Exception error) when (Expected(error))
                    {
                        var result = reservation is null ? Failure(source.Path, error) :
                            (await publisher.AbandonAsync(reservation, token.IsCancellationRequested)).ToFileResult();
                        if (result.Publication?.IsCommitted != true) result = result with
                        { State = Failure(source.Path, error).State, Message = Failure(source.Path, error).Message };
                        row.ApplyGifFrame(index, result);
                        // GIF decoding is sequential. Stop this source on a frame failure;
                        // retry replays its compositor while skipping committed outputs.
                        throw;
                    }
                    finally { changed?.Invoke(row); }
                }
            }
            catch (Exception error) when (Expected(error))
            {
                foreach (var pending in row.GifFrames.Where(frame => frame.Value.State is OperationState.Pending or OperationState.Running).Select(frame => frame.Key).ToArray())
                    row.ApplyGifFrame(pending, Failure(source.Path, error));
                if (row.GifResumeBlocked) row.BlockGifResume(error.Message);
                changed?.Invoke(row);
            }
            finally
            {
                await worker.ReleaseGifAsync();
                folder?.Dispose();
            }
        }

        FileResult Failure(string path, Exception error) => new(path, token.IsCancellationRequested ? OperationState.Cancelled :
            error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput } ? OperationState.Unsupported : OperationState.Failed,
            token.IsCancellationRequested ? "GIF extraction cancelled. Completed PNG frames were kept." : budget.IsCancellationRequested ?
                "GIF extraction reached the ten-minute limit. Completed PNG frames were kept." :
                !admission.IsAllowed ? admission.Status.Message : error is InvalidDataException ? error.Message :
                "This GIF frame could not be saved. Original and completed PNG frames were kept; retry unfinished frames.");
    }

    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException;
}
