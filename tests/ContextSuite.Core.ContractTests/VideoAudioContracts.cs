using System.Collections.Immutable;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;
using ContextSuite.Application;

namespace ContextSuite.Core.ContractTests;

internal static class VideoAudioContracts
{
    internal static void Models(Action<bool, string> check)
    {
        var tags = ImmutableDictionary<string, string>.Empty;
        var audio = new AudioStreamFacts(2, "audio", "aac", 48000, 6, null, null, 2m, "5.1", tags, StartSeconds: 0.125m);
        var facts = new AudioProbeFacts("mov,mp4,m4a,3gp,3g2,mj2", 2.125m,
            [new(0, "video", "h264", null, null, null, null, 2.125m, null, tags), audio], tags);
        var selected = new VideoAudioSelection(2);
        var wave = AudioConversionPlan.Create(facts, AudioFormat.Wave, video: selected);
        check(wave.Channels == 6 && wave.RequiresExactSamples && !wave.AlreadyTarget && wave.OutputCodec == "pcm_f32le",
            "video audio plan preserves selected surround channels and decoded precision in WAV");
        var mp3 = AudioConversionPlan.Create(facts, AudioFormat.Mp3, video: selected);
        check(mp3.Channels == 2 && mp3.RequiredConsent == (AudioConversionConsent.ChannelReduction | AudioConversionConsent.LossyTranscoding),
            "video audio plan requires stereo reduction and lossy-transcoding consent for surround MP3");
        Reject(() => mp3.RequireConsent(AudioConversionConsent.LossyTranscoding), "channel reduction cannot hide behind lossy consent");
        Reject(() => AudioConversionPlan.Create(facts, AudioFormat.Wave), "video needs an explicit selected audio track");
        Reject(() => new VideoAudioSelection(0).Validate(facts), "video pixels cannot be selected as audio");
        Reject(() => new VideoAudioSelection(1).Validate(facts), "filtered-list position cannot replace absolute stream index");
        foreach (var target in new[] { AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus, AudioFormat.Wem })
            Reject(() => AudioConversionPlan.Create(facts, target, video: selected), "unapproved extraction target " + target);
        var pcm = facts with { Streams = [facts.Streams[0], audio with { Codec = "pcm_s16le", SampleBits = 16, Channels = 2, ChannelLayout = "stereo" }] };
        var pcmWave = AudioConversionPlan.Create(pcm, AudioFormat.Wave, video: selected);
        check(!pcmWave.AlreadyTarget && pcmWave.RequiresExactSamples && pcmWave.SampleBits == 16,
            "PCM in MOV still extracts a new exact WAV file");
        var compressed = facts with { Streams = [facts.Streams[0], audio with { Codec = "mp3", Channels = 2, ChannelLayout = "stereo" }] };
        var compressedMp3 = AudioConversionPlan.Create(compressed, AudioFormat.Mp3, video: selected);
        check(!compressedMp3.AlreadyTarget && compressedMp3.RequiredConsent.HasFlag(AudioConversionConsent.LossyTranscoding),
            "MP3 inside video is an extraction with explicit re-encoding consent");
        var chaptered = pcm with { Chapters = [new(0, 0, 1, tags)] };
        var chapterPlan = AudioConversionPlan.Create(chaptered, AudioFormat.Wave, video: selected);
        check(chapterPlan.RequiresExactSamples && chapterPlan.RequiredConsent == AudioConversionConsent.MetadataOmission,
            "chapter omission requires consent while exact audio comparison remains mandatory");
        Reject(() => chapterPlan.RequireConsent(AudioConversionConsent.None), "chapter omission is never silent");
        var unsupported = facts with { Streams = [audio with { Codec = "dts" }] };
        Reject(() => AudioConversionPlan.Create(unsupported, AudioFormat.Wave, video: selected), "unverified audio codec");
        var source = new AudioFileSource(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "model.mp4"), new string('A', 64), 100,
            facts, Video: selected);
        var batch = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Wave, new("convert", new(ReplaceOriginals: true)), true);
        check(!batch.ReplaceOriginal && batch.Confirm(0, false, true).Plan.Items[0].Source.Video == selected,
            "video extraction always preserves originals and keeps the immutable stream selection");
        Reject(() => (batch with { ReplaceOriginal = true }).Confirm(0, true, true), "forged video overwrite plan");
        Reject(() => (source with { Path = Path.ChangeExtension(source.Path, ".webm") }).Validate(), "extension and container disagreement");
        Reject(() => (source with { FileBytes = AudioFileSource.MaximumFileBytes + 1 }).Validate(), "video input above the 512 MiB file budget");
        var noSelection = AudioConversionBatch.Create(Guid.NewGuid(), [source with { Video = null }], AudioFormat.Wave, new("convert", new()));
        check(!noSelection.HasExecutableItems, "unselected video cannot enter an executable batch");
        var choices = new VideoAudioTrackViewModel(source with { Facts = facts with
        {
            Streams = [facts.Streams[0], audio with { Tags = tags.Add("language", "eng").Add("title", "Commentary") },
                audio with { Index = 7, Codec = "dts", Tags = tags.Add("language", "fra") }]
        } }, AudioFormat.Wave);
        check(choices.Tracks.Count == 2 && choices.Tracks[0].Selection.StreamIndex == 2 &&
            choices.Tracks[1].Selection.StreamIndex == 7 && choices.Tracks[0].Label.Contains("Commentary") &&
            choices.Tracks[0].Label.Contains("eng") && choices.Tracks[1].BlockReason is not null,
            "video audio picker labels tracks and retains absolute identity without hiding unsupported tracks");
        var probe = new WorkerCommand(1, Guid.NewGuid(), "video-audio-probe", AudioFile: new(source.ItemId, source.Path));
        probe.Validate();
        Reject(() => (probe with { AudioTarget = AudioFormat.Mp3 }).Validate(), "target on inventory-only probe");
        (probe with { AudioFile = new(source.ItemId, Path.ChangeExtension(source.Path, ".MKV")) }).Validate();
        check(true, "MKV probe admitted by filename");
        Reject(() => (probe with { AudioFile = new(source.ItemId, Path.ChangeExtension(source.Path, ".m4v")) }).Validate(), "unapproved M4V input");
        Reject(() => (probe with { GifProbe = new(source.ItemId, source.Path) }).Validate(), "mixed video/GIF probe payload");

        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            { check(true, "video audio plan refuses " + label); return; }
            throw new InvalidOperationException("Video audio plan accepted " + label);
        }
    }
}
