using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private async Task ConvertAudioBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken token)
    {
        var target = request.Action switch
        {
            "wav" => AudioFormat.Wave, "flac" => AudioFormat.Flac, "mp3" => AudioFormat.Mp3,
            "m4a" => AudioFormat.M4a, "vorbis" => AudioFormat.Vorbis, "opus" => AudioFormat.Opus,
            "wem" => AudioFormat.Wem,
            _ => throw new InvalidDataException("Unknown audio target.")
        };
        WemEncodingOptions? wemOptions = null;
        if (target == AudioFormat.Wem)
        {
            if (!worker.HasWemDecoder)
            {
                foreach (var row in rows) row.ApplyResult(new(row.Path, OperationState.Unsupported, "WEM conversion is unavailable in this build. The original was kept."));
                return;
            }
            wemOptions = WemEncodingRequested is null ? null : await WemEncodingRequested(token);
            if (wemOptions is null)
            {
                foreach (var row in rows) row.ApplyResult(new(row.Path, OperationState.Cancelled, "WEM conversion cancelled before setup was complete."));
                return;
            }
            wemOptions.Validate();
        }
        var sources = new List<AudioFileSource>();
        for (var i = 0; i < rows.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var row = rows[i];
            var wem = string.Equals(Path.GetExtension(row.Path), ".wem", StringComparison.OrdinalIgnoreCase);
            var mp3ToWem = target == AudioFormat.Wem && string.Equals(Path.GetExtension(row.Path), ".mp3", StringComparison.OrdinalIgnoreCase);
            if ((wem || target == AudioFormat.Wem ? !worker.HasWemDecoder : !worker.HasAudioConverter) ||
                mp3ToWem && !worker.HasAudioConverter)
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported,
                    wem ? "WEM decoding is unavailable in this build. The original was kept." :
                    mp3ToWem ? "MP3 decoding is unavailable in this build. The original was kept." : "Audio conversion is unavailable in this build."));
                continue;
            }
            Summary = $"Reading audio {i + 1} of {rows.Length}.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking audio conversion"));
            try
            {
                if (VideoAudioSelection.IsVideoPath(row.Path))
                {
                    var source = await worker.ProbeVideoAudioAsync(new(row.ItemId, row.Path), token);
                    VideoAudioSelection? selected;
                    if (row.VideoAudioSource is { } previous)
                    {
                        if (source.Sha256 != previous.Sha256 || source.FileBytes != previous.FileBytes)
                        {
                            row.VideoAudioResumeBlocked = true;
                            row.ApplyResult(new(row.Path, OperationState.Failed, "The video changed. Start a new Convert command to choose its audio track."));
                            continue;
                        }
                        selected = previous.Video ?? throw new InvalidDataException("The saved video track selection is missing.");
                    }
                    else
                    {
                        var tracks = new VideoAudioTrackViewModel(source, target);
                        if (tracks.Tracks.Count == 0)
                        {
                            row.ApplyResult(new(row.Path, OperationState.Unsupported, "This video has no audio track. The original was kept."));
                            continue;
                        }
                        if (tracks.Tracks.All(track => track.BlockReason is not null))
                        {
                            row.ApplyResult(new(row.Path, OperationState.Unsupported, "None of this video's audio tracks supports the selected output yet. Try another audio format."));
                            continue;
                        }
                        selected = tracks.Tracks.Count == 1 ? tracks.Tracks[0].Selection :
                            VideoAudioTrackRequested is null ? null : await VideoAudioTrackRequested(tracks, token);
                        if (selected is null)
                        {
                            row.ApplyResult(new(row.Path, OperationState.Cancelled, "Audio extraction cancelled before choosing a track."));
                            continue;
                        }
                    }
                    source = source with { Video = selected };
                    source.Validate();
                    row.VideoAudioSource = source;
                    sources.Add(source);
                }
                else sources.Add(await worker.ProbeAudioFileAsync(new(row.ItemId, row.Path), target, token));
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
        var plan = AudioConversionBatch.Create(request.RequestId, sources, target, settings, replace, wemOptions);
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
            foreach (var row in rows.Where(row => row.Result.State is OperationState.Pending or OperationState.Running))
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
