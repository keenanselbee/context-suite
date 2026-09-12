using System.Collections.Immutable;
using ContextSuite.Core.Audio;

internal static class AudioConversionContracts
{
    public static void Run(Action<bool, string> check)
    {
        var empty = ImmutableDictionary<string, string>.Empty;
        AudioProbeFacts Facts(string container, string codec, int? bits = null, int rate = 48000, int channels = 2, string? layout = null) =>
            new(container, null, [new(0, "audio", codec, rate, channels, bits, null, null, layout, empty)], empty);
        var wave = Facts("wav", "pcm_s16le");
        var flac = AudioConversionPlan.Create(wave, AudioFormat.Flac);
        check(flac.OutputCodec == "flac" && flac.SampleBits == 16 && flac.RequiresExactSamples && flac.RequiredConsent == AudioConversionConsent.None,
            "audio plan: integer WAVE to FLAC retains precision and requires exact samples");
        var wide = AudioConversionPlan.Create(Facts("flac", "flac", 32), AudioFormat.Wave);
        var narrow = AudioConversionPlan.Create(Facts("wav", "pcm_u8"), AudioFormat.Flac);
        check(narrow.SampleBits == 16 && narrow.RequiresExactSamples && narrow.Notices.Length == 1,
            "audio plan: 8-bit source uses lossless 16-bit FLAC storage with a precise explanation");
        check(wide.OutputCodec == "pcm_s32le" && wide.SampleBits == 32, "audio plan: PCM32 avoids lossy float32 conversion");
        foreach (var codec in new[] { "pcm_f32le", "pcm_f64le" })
        {
            var reduced = AudioConversionPlan.Create(Facts("wav", codec), AudioFormat.Flac);
            check(reduced.SampleBits == 24 && reduced.RequiredConsent == AudioConversionConsent.PrecisionReduction && !reduced.RequiresExactSamples,
                "audio plan: floating input to FLAC requires precision consent " + codec);
            Reject(() => reduced.RequireConsent(AudioConversionConsent.None), "precision acknowledgement cannot be omitted");
        }
        foreach (var target in Enum.GetValues<AudioFormat>())
        {
            var plan = AudioConversionPlan.Create(wave, target);
            check(plan.SampleRate == 48000 && plan.Channels == 2 && plan.Policy == AudioConversionPlan.CurrentPolicy &&
                plan.RequiredConsent == AudioConversionConsent.None, "audio plan: ordinary target retains rate/channels " + target);
        }
        var mp3 = Facts("mp3", "mp3");
        var decoded = AudioConversionPlan.Create(mp3, AudioFormat.Wave);
        check(decoded.OutputCodec == "pcm_f32le" && decoded.RequiresExactSamples && decoded.Notices.Any(text => text.Contains("cannot restore")),
            "audio plan: lossless output preserves decoded audio without restoration claim");
        var transcode = AudioConversionPlan.Create(mp3, AudioFormat.M4a);
        check(transcode.RequiredConsent == AudioConversionConsent.LossyTranscoding, "audio plan: lossy transcoding requires consent");
        Reject(() => transcode.RequireConsent(AudioConversionConsent.PrecisionReduction), "unrelated consent cannot authorize transcoding");
        transcode.RequireConsent(AudioConversionConsent.LossyTranscoding);
        var opus = AudioConversionPlan.Create(Facts("mp3", "mp3", rate: 44100), AudioFormat.Opus);
        check(opus.SampleRate == 48000 && opus.RequiredConsent == (AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling),
            "audio plan: 44.1 kHz Opus explicitly requires both changes");
        Reject(() => opus.RequireConsent(AudioConversionConsent.LossyTranscoding), "lossy consent cannot authorize resampling");
        var speech = AudioConversionPlan.Create(Facts("wav", "pcm_s16le", rate: 8000, channels: 1), AudioFormat.Opus);
        check(speech.SampleRate == 48000 && speech.RequiredConsent == AudioConversionConsent.Resampling,
            "audio plan: low-rate Opus also declares the changed decoded sample rate");
        check(AudioConversionPlan.Create(mp3, AudioFormat.Mp3).AlreadyTarget, "audio plan: same-format conversion does not re-encode MP3");
        Reject(() => AudioConversionPlan.Create(Facts("wav", "pcm_s16le", channels: 6, layout: "5.1"), AudioFormat.Mp3), "no implicit multichannel downmix");
        var surround = AudioConversionPlan.Create(Facts("wav", "pcm_s24le", channels: 6, layout: "5.1"), AudioFormat.Flac);
        check(surround.Channels == 6 && surround.SampleBits == 24, "audio plan: known surround layout retains channels and precision");
        foreach (var target in new[] { AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
        foreach (var layout in new[] { "5.0(side)", "5.1(side)" })
        {
            var side = Facts("wav", "pcm_s16le", channels: layout.StartsWith("5.0") ? 5 : 6, layout: layout);
            Reject(() => AudioConversionPlan.Create(side, target), "side speakers cannot be silently relabeled for " + target + " " + layout);
            check(AudioConversionPlan.Create(side, AudioFormat.Flac).RequiresExactSamples,
                "audio plan: side layout remains available through exact FLAC " + target + " " + layout);
        }
        foreach (var target in new[] { AudioFormat.Vorbis, AudioFormat.Opus })
        foreach (var layout in new[] { "2.1", "4.0" })
            Reject(() => AudioConversionPlan.Create(Facts("wav", "pcm_s16le", channels: layout == "2.1" ? 3 : 4, layout: layout), target),
                "unsupported Ogg speaker layout is refused before encoding " + target + " " + layout);
        Reject(() => AudioConversionPlan.Create(Facts("wav", "pcm_s16le", channels: 7, layout: "6.1"), AudioFormat.M4a),
            "AAC preset cannot relabel 6.1 side speakers");
        foreach (var layout in new[] { "5.0", "6.1" })
            check(AudioConversionPlan.Create(Facts("wav", "pcm_s16le", channels: layout == "5.0" ? 5 : 7, layout: layout), AudioFormat.Opus).Channels == (layout == "5.0" ? 5 : 7),
                "audio plan: corrected Opus mapping retains supported surround " + layout);
        Reject(() => AudioConversionPlan.Create(Facts("wav", "pcm_s16le", channels: 6), AudioFormat.Flac), "unknown surround layout is not guessed");
        Reject(() => AudioConversionPlan.Create(Facts("mov,mp4,m4a,3gp,3g2,mj2", "alac"), AudioFormat.Mp3), "container recognition does not admit an unverified codec");
        Reject(() => AudioConversionPlan.Create(Facts("wav", "aac"), AudioFormat.Mp3), "unsupported codec/container pairing is rejected");
        Reject(() => AudioConversionPlan.Create(Facts("flac", "flac"), AudioFormat.Wave), "unknown lossless precision is not guessed");
        Reject(() => AudioConversionPlan.Create(Facts("wav", "pcm_s16le", rate: 96000), AudioFormat.Mp3), "no hidden MP3 resampling");
        Reject(() => AudioConversionPlan.Create(wave with { Streams = wave.Streams.Add(new(1, "video", "mjpeg", null, null, null, null, null, null, empty)) }, AudioFormat.Flac), "artwork cannot be dropped through audio stream selection");
        Reject(() => AudioConversionPlan.Create(wave, (AudioFormat)999), "unknown audio target rejected");
        Reject(() => flac.RequireConsent((AudioConversionConsent)128), "unknown consent flags rejected");
        void Reject(Action action, string name)
        {
            try { action(); check(false, "audio plan: " + name); }
            catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException or InvalidDataException)
            { check(true, "audio plan: " + name); }
        }
    }
}
