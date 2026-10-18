using System.Security.Cryptography;

namespace ContextSuite.Core.Audio;

// A reference selects observable encoding properties. It does not identify an
// encoder build, recover its quality setting or establish game compatibility.
public sealed record WemReference(string Path, string Sha256, long FileBytes,
    WemCodec Codec, int SampleRate, int Channels)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path) || !System.IO.Path.IsPathFullyQualified(Path) || Path.Length > 32700 ||
            Path.IndexOfAny(['\0', '\r', '\n']) >= 0 || Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) ||
            FileBytes is <= 0 or > AudioFileSource.MaximumFileBytes || Codec is not (WemCodec.Pcm16 or WemCodec.Vorbis) ||
            SampleRate is not (44100 or 48000) || Channels is not (1 or 2))
            throw new InvalidDataException("Select a supported PCM16 or Vorbis WEM reference at 44.1 or 48 kHz, mono or stereo.");
    }

    public static async Task<WemReference> InspectAsync(string path, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
            throw new ArgumentException("Select the original WEM file.", nameof(path));
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = await WemHeader.ReadAsync(file, token);
        if (header.BigEndian || !header.IsHeaderLayoutRecognized || header.HasUnrecognizedChunks ||
            header.Codec == WemCodec.Pcm16 && header.BitDepth != 16)
            throw new NotSupportedException("This original WEM has an unsupported codec or metadata layout.");
        file.Position = 0;
        var reference = new WemReference(path, Convert.ToHexString(await SHA256.HashDataAsync(file, token)),
            file.Length, header.Codec, header.SampleRate, header.Channels);
        reference.Validate();
        return reference;
    }
}

public sealed record WemEncodingOptions(WwiseInstallation Installation, WemCodec Codec, WemReference? Reference = null)
{
    public void Validate()
    {
        if (Installation is null || Installation.ProductVersion is null || Installation.ProductVersion.Major < 1 ||
            Installation.ExecutableSha256 is not { Length: 64 } || !Installation.ExecutableSha256.All(char.IsAsciiHexDigit) ||
            Codec is not (WemCodec.Pcm16 or WemCodec.Vorbis))
            throw new InvalidDataException("Select an inspected Wwise installation and a supported WEM codec.");
        if (Installation.ConsolePath != WwiseInstallation.ResolveConsolePath(Installation.ConsolePath))
            throw new InvalidDataException("The Wwise executable path is not canonical.");
        Reference?.Validate();
        if (Reference is not null && Reference.Codec != Codec)
            throw new InvalidDataException("The selected codec does not match the original WEM.");
    }

    internal AudioConversionPlan CreatePlan(AudioProbeFacts source)
    {
        Validate();
        if (source.Container is not ("wav" or "mp3") || source.Streams.Length != 1 || !source.Tags.IsEmpty)
            throw new NotSupportedException("WEM encoding requires supported WAV or MP3 audio without unpreserved metadata.");
        var audio = source.Streams[0];
        var pcm = source.Container == "wav" && audio.Codec == "pcm_s16le" && audio.SampleBits == 16;
        var floating = source.Container == "wav" && audio.Codec == "pcm_f32le" && audio.SampleBits == 32;
        var mp3 = source.Container == "mp3" && audio.Codec == "mp3";
        if (audio.Kind != "audio" || audio.AttachedPicture || !audio.Tags.IsEmpty ||
            !(pcm || floating || mp3) || audio.SampleRate is not (44100 or 48000) || audio.Channels is not (1 or 2))
            throw new NotSupportedException("WEM encoding accepts PCM16 or float32 WAV and MP3 at 44.1 or 48 kHz, mono or stereo.");
        if (Reference is not null && (Reference.SampleRate != audio.SampleRate || Reference.Channels != audio.Channels))
            throw new NotSupportedException("The audio rate or channel count differs from the original WEM. Prepare matching audio or choose the codec in Advanced.");
        var consent = AudioConversionConsent.None;
        var notices = new List<string>();
        if (!pcm)
        {
            consent |= AudioConversionConsent.PrecisionReduction;
            notices.Add("WEM conversion prepares this decoded audio at 16-bit integer precision. The original is kept unless replacement was explicitly selected.");
        }
        if (mp3 && Codec == WemCodec.Vorbis)
        {
            consent |= AudioConversionConsent.LossyTranscoding;
            notices.Add("MP3 already has quality loss. Encoding it as Vorbis can reduce quality further.");
        }
        if (Reference is not null)
            notices.Add("Matches the original WEM's codec, sample rate and channels. Encoder settings and game compatibility are not inferred.");
        return new(AudioFormat.Wem, AudioConversionPlan.CurrentPolicy, ".wem", Codec == WemCodec.Pcm16 ? "wwise_pcm16" : "wwise_vorbis",
            audio.SampleRate.Value, audio.Channels.Value, Codec == WemCodec.Pcm16 ? 16 : null, false,
            pcm && Codec == WemCodec.Pcm16, consent, [.. notices]);
    }
}
