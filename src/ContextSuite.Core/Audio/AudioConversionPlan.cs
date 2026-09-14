using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public enum AudioFormat { Wave, Flac, Mp3, M4a, Vorbis, Opus }

[Flags]
public enum AudioConversionConsent { None = 0, LossyTranscoding = 1, Resampling = 2, PrecisionReduction = 4 }

// Fixed policy, not a shipping capability declaration. Engine, metadata and
// decoded-output validation must pass before application-owned publication.
public sealed record AudioConversionPlan(AudioFormat Target, string Policy, string Extension,
    string OutputCodec, int SampleRate, int Channels, int? SampleBits,
    bool AlreadyTarget, bool RequiresExactSamples, AudioConversionConsent RequiredConsent,
    ImmutableArray<string> Notices)
{
    public const string CurrentPolicy = "audio-fixed-1";

    // Only for native FLAC recompression with separately verified raw metadata
    // preservation. This does not authorize artwork omission during conversion.
    public static AudioConversionPlan CreateFlacOptimization(AudioProbeFacts source)
    {
        source.Validate();
        if (source.Container != "flac" || source.Streams.Count(stream => stream.Kind == "audio") != 1 ||
            source.Streams.Any(stream => stream.Kind != "audio" && !stream.AttachedPicture))
            throw new NotSupportedException("FLAC optimization requires one audio stream and preserved attached pictures only.");
        var audio = source with { Streams = source.Streams.Where(stream => stream.Kind == "audio").ToImmutableArray() };
        return Create(audio, AudioFormat.Flac) with { AlreadyTarget = false, Policy = FlacOptimizationPlan.Policy };
    }

    public static AudioConversionPlan Create(AudioProbeFacts source, AudioFormat target)
    {
        source.Validate();
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (source.Streams.Count(stream => stream.Kind == "audio") != 1 || source.Streams.Any(stream => stream.Kind != "audio" && !stream.AttachedPicture))
            throw new NotSupportedException("Additional streams or artwork need a preservation plan before conversion.");
        var audio = source.Streams.Single(stream => stream.Kind == "audio");
        if (audio.SampleRate is not (>= 8000 and <= 192000) || audio.Channels is not (>= 1 and <= 8))
            throw new NotSupportedException("The sample rate or channel count is outside the current audio policy.");
        if (audio.Channels > 2 && audio.ChannelLayout is not ("2.1" or "quad" or "4.0" or "5.0" or "5.0(side)" or "5.1" or "5.1(side)" or "6.1" or "7.1"))
            throw new NotSupportedException("The channel layout must be known before multichannel conversion.");
        var sourceFormat = SourceFormat(source.Container, audio.Codec);
        if (source.Streams.Any(stream => stream.AttachedPicture) && sourceFormat != target &&
            !(sourceFormat == AudioFormat.Flac && target is AudioFormat.Mp3 or AudioFormat.Vorbis or AudioFormat.Opus) &&
            !(sourceFormat is AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Vorbis or AudioFormat.Opus && target is AudioFormat.Mp3 or AudioFormat.Flac or AudioFormat.Vorbis or AudioFormat.Opus))
            throw new NotSupportedException("Artwork needs a preservation handler for these formats. Originals were kept.");
        var sourceBits = audio.SampleBits ?? audio.Codec switch
        { "pcm_u8" => 8, "pcm_s16le" => 16, "pcm_s24le" => 24, "pcm_s32le" or "pcm_f32le" => 32, "pcm_f64le" => 64, _ => (int?)null };
        var floating = audio.Codec is "pcm_f32le" or "pcm_f64le";
        var lossy = sourceFormat is AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Vorbis or AudioFormat.Opus;
        var targetLossy = target is not (AudioFormat.Wave or AudioFormat.Flac);
        if (!lossy && !floating && sourceBits is not (8 or 16 or 24 or 32))
            throw new NotSupportedException("The source precision must be known and supported before conversion.");
        if (target == AudioFormat.Mp3 && audio.Channels > 2)
            throw new NotSupportedException("MP3 supports mono or stereo. Choose a format that preserves these channels.");
        var rate = audio.SampleRate.Value;
        var consent = AudioConversionConsent.None;
        var notices = ImmutableArray.CreateBuilder<string>();
        var already = sourceFormat == target;
        if (!already && audio.Channels > 2)
        {
            var layoutSupported = target switch
            {
                AudioFormat.M4a => audio.ChannelLayout is "2.1" or "quad" or "4.0" or "5.0" or "5.1" or "7.1",
                AudioFormat.Vorbis or AudioFormat.Opus => audio.ChannelLayout is "quad" or "5.0" or "5.1" or "6.1" or "7.1",
                _ => true
            };
            if (!layoutSupported)
            {
                var targetName = target switch { AudioFormat.M4a => "M4A (AAC)", AudioFormat.Vorbis => "Ogg Vorbis", _ => "Opus" };
                throw new NotSupportedException($"This {targetName} preset cannot preserve the {audio.ChannelLayout} speaker layout. Choose WAV or FLAC.");
            }
        }
        if (lossy && targetLossy && !already)
        {
            consent |= AudioConversionConsent.LossyTranscoding;
            notices.Add("This audio is already compressed with quality loss. Converting it again can reduce quality further.");
        }
        if (lossy && !targetLossy)
            notices.Add("A lossless output keeps decoded audio; it cannot restore quality lost in the original recording.");
        if (target == AudioFormat.Opus && rate != 48000)
        {
            rate = 48000;
            consent |= AudioConversionConsent.Resampling;
            notices.Add($"Opus output uses 48,000 Hz decoded audio. This resamples the {audio.SampleRate:N0} Hz recording.");
        }
        if (target == AudioFormat.Mp3 && rate is not (8000 or 11025 or 12000 or 16000 or 22050 or 24000 or 32000 or 44100 or 48000))
            throw new NotSupportedException("MP3 cannot retain this sample rate. Choose another format.");
        if (target == AudioFormat.M4a && rate is not (8000 or 11025 or 12000 or 16000 or 22050 or 24000 or 32000 or 44100 or 48000 or 64000 or 88200 or 96000))
            throw new NotSupportedException("AAC cannot retain this sample rate. Choose another format.");
        int? bits = target switch
        {
            AudioFormat.Wave => floating ? audio.Codec == "pcm_f64le" ? 64 : 32 : lossy ? 32 : sourceBits,
            AudioFormat.Flac => already ? sourceBits : floating || lossy ? 24 : Math.Max(16, sourceBits!.Value),
            _ => null
        };
        if (target == AudioFormat.Flac && sourceBits == 8 && !already)
            notices.Add("This encoder stores 8-bit audio in 16-bit FLAC samples without changing the decoded sound or adding recorded detail.");
        if (target == AudioFormat.Flac && (floating || lossy))
        {
            consent |= AudioConversionConsent.PrecisionReduction;
            notices.Add("FLAC stores this decoded audio at 24-bit integer precision. A floating-point WAV preserves its decoded precision.");
        }
        var codec = target switch
        {
            AudioFormat.Wave => floating ? audio.Codec : lossy ? "pcm_f32le" : bits switch
                { 8 => "pcm_u8", 16 => "pcm_s16le", 24 => "pcm_s24le", 32 => "pcm_s32le", _ => throw new InvalidDataException("Invalid WAVE precision.") },
            AudioFormat.Flac => "flac", AudioFormat.Mp3 => "mp3", AudioFormat.M4a => "aac",
            AudioFormat.Vorbis => "vorbis", AudioFormat.Opus => "opus", _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var extension = target switch { AudioFormat.Wave => ".wav", AudioFormat.Flac => ".flac", AudioFormat.Mp3 => ".mp3",
            AudioFormat.M4a => ".m4a", AudioFormat.Vorbis => ".ogg", AudioFormat.Opus => ".opus", _ => throw new ArgumentOutOfRangeException(nameof(target)) };
        return new(target, CurrentPolicy, extension, codec, rate, audio.Channels.Value, bits, already,
            !targetLossy && consent == AudioConversionConsent.None, consent, notices.ToImmutable());
    }

    public void RequireConsent(AudioConversionConsent accepted)
    {
        const AudioConversionConsent known = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        if ((accepted & ~known) != 0 || (accepted & RequiredConsent) != RequiredConsent)
            throw new InvalidOperationException("The audio conversion needs acknowledgement of its quality changes.");
    }

    private static AudioFormat SourceFormat(string container, string codec) => (container, codec) switch
    {
        ("wav", "pcm_u8" or "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" or "pcm_f64le") => AudioFormat.Wave,
        ("flac", "flac") => AudioFormat.Flac,
        ("mp3", "mp3") => AudioFormat.Mp3,
        ("mov,mp4,m4a,3gp,3g2,mj2", "aac") => AudioFormat.M4a,
        ("ogg", "vorbis") => AudioFormat.Vorbis,
        ("ogg", "opus") => AudioFormat.Opus,
        _ => throw new NotSupportedException("This container and audio codec have no verified conversion policy yet.")
    };
}
