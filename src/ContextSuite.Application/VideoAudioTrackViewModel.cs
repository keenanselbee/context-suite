using ContextSuite.Core.Audio;

namespace ContextSuite.Application;

internal sealed record VideoAudioTrackOption(VideoAudioSelection Selection, string Label, string? BlockReason);

internal sealed class VideoAudioTrackViewModel
{
    public string FileName { get; }
    public IReadOnlyList<VideoAudioTrackOption> Tracks { get; }

    internal VideoAudioTrackViewModel(AudioFileSource source, AudioFormat target)
    {
        source.Validate();
        FileName = Path.GetFileName(source.Path);
        Tracks = source.Facts.Streams.Where(stream => stream.Kind == "audio").Select((stream, ordinal) =>
        {
            var selection = new VideoAudioSelection(stream.Index);
            string? reason = null;
            try { AudioConversionPlan.Create(source.Facts, target, video: selection); }
            catch (NotSupportedException error) { reason = error.Message; }
            var name = stream.Tags.GetValueOrDefault("title") ?? stream.Tags.GetValueOrDefault("handler_name");
            var language = stream.Tags.GetValueOrDefault("language");
            var label = $"Track {ordinal + 1}" + (string.IsNullOrWhiteSpace(name) ? "" : " - " + name) +
                " | " + (string.IsNullOrWhiteSpace(language) || language == "und" ? "Language unspecified" : language) +
                " | " + (stream.Channels == 1 ? "Mono" : stream.Channels == 2 ? "Stereo" :
                    stream.Channels is { } channels ? $"{channels} channels ({stream.ChannelLayout ?? "layout unknown"})" : "Channels unknown") +
                " | " + stream.Codec.ToUpperInvariant();
            return new VideoAudioTrackOption(selection, label, reason);
        }).ToArray();
    }
}
