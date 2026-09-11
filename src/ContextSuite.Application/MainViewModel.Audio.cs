using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private async Task ConvertAudioBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken token)
    {
        if (!worker.HasAudioConverter)
        {
            foreach (var row in rows) row.ApplyResult(new(row.Path, OperationState.Unsupported, "Audio conversion is unavailable in this build."));
            return;
        }
        var target = request.Action switch
        {
            "wav" => AudioFormat.Wave, "flac" => AudioFormat.Flac, "mp3" => AudioFormat.Mp3,
            "m4a" => AudioFormat.M4a, "vorbis" => AudioFormat.Vorbis, "opus" => AudioFormat.Opus,
            _ => throw new InvalidDataException("Unknown audio target.")
        };
        var sources = new List<AudioFileSource>();
        for (var i = 0; i < rows.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var row = rows[i];
            Summary = $"Reading audio {i + 1} of {rows.Length}.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking audio conversion"));
            try
            {
                sources.Add(await worker.ProbeAudioFileAsync(new(row.ItemId, row.Path), target, token));
                row.ApplyResult(new(row.Path, OperationState.Pending, "Preparing audio conversion"));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                var unsupported = error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput };
                row.ApplyResult(new(row.Path, unsupported ? OperationState.Unsupported : OperationState.Failed,
                    unsupported ? "This file has audio information or a feature that conversion cannot preserve yet. The original was kept." :
                        "Audio could not be read. Check that the file is supported and available, then try again."));
            }
        }
        if (sources.Count == 0) return;
        var settings = rows[0].Settings;
        var replace = settings.Preferences.ReplaceOriginals && settings.Preferences.OutputDirectory is null && PublicationSupport.ReplacementAvailable;
        var plan = AudioConversionBatch.Create(request.RequestId, sources, target, settings, replace);
        var byId = rows.ToDictionary(row => row.ItemId);
        foreach (var item in plan.Items)
        {
            if (item.BlockReason is not null || item.Encoding is null)
                byId[item.Source.ItemId].ApplyResult(new(item.Source.Path, OperationState.Unsupported, item.BlockReason ?? "This audio cannot use the selected format."));
            else if (item.Encoding.AlreadyTarget)
                byId[item.Source.ItemId].ApplyResult(new(item.Source.Path, OperationState.Unchanged, "Already the selected audio format; original kept."));
        }
        if (!plan.HasExecutableItems) return;
        ConfirmedAudioConversion? confirmed;
        if (plan.RequiredConsent == AudioConversionConsent.None) confirmed = plan.Confirm(0, replace, PublicationSupport.ReplacementAvailable);
        else
        {
            using var decision = new AudioConversionViewModel(plan, trial!, PublicationSupport.ReplacementAvailable);
            confirmed = AudioConversionRequested is null ? null : await AudioConversionRequested(decision, token);
        }
        if (confirmed is null)
        {
            foreach (var row in rows.Where(row => row.Result.State == OperationState.Pending))
                row.ApplyResult(new(row.Path, OperationState.Cancelled, "Audio conversion cancelled before confirmation."));
            return;
        }
        token.ThrowIfCancellationRequested();
        await new AudioConversionExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (item, result) =>
        {
            byId[item.Source.ItemId].ApplyResult(result);
            Summary = $"Converting audio: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} finished.";
        }, token);
    }
}
