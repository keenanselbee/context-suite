using System.Globalization;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Cross-check packet timing, codec priming and decoded frame extents before
// the shared audio encoder is allowed to make an extraction candidate.
internal sealed record VideoAudioInventory(long DecodedFrames, long PresentedFrames, long FirstPacketPts,
    long LastPacketEnd, long SkipSamples, long DiscardPadding, int PacketCount)
{
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    private const int MaximumEntries = 65536;

    internal static VideoAudioInventory Parse(byte[] packetJson, byte[] frameJson, AudioStreamFacts stream)
    {
        if (packetJson.Length > MaximumJsonBytes || frameJson.Length > MaximumJsonBytes)
            throw new InvalidDataException("The selected audio timeline exceeds its JSON budget.");
        if (stream.SampleRate is not { } rate || stream.TimeBase is not { } timeBase)
            throw new NotSupportedException("The selected track needs an exact sample clock and time base.");
        var parts = timeBase.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator) ||
            !long.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) ||
            numerator <= 0 || denominator <= 0)
            throw new InvalidDataException("The selected track time base is invalid.");
        var codecFrame = stream.Codec switch
        { "aac" => 1024, "mp3" => 1152, "opus" => 960, "vorbis" => 2048, "flac" => 4608, "pcm_s16le" => 1,
            _ => throw new NotSupportedException("The selected track codec has no reviewed sample extent.") };
        var samplesPerTick = (decimal)numerator * rate / denominator;
        if (samplesPerTick > 256m)
            throw new NotSupportedException("The selected track timestamp clock is too coarse to establish continuity.");
        using var packetDocument = JsonDocument.Parse(packetJson, new JsonDocumentOptions { MaxDepth = 16 });
        var packets = packetDocument.RootElement.GetProperty("packets");
        if (packets.ValueKind != JsonValueKind.Array || packets.GetArrayLength() is < 1 or > MaximumEntries)
            throw new InvalidDataException("The selected audio packet count exceeds its bound.");
        long first = 0, end = 0, duration = 0, skip = 0, discard = 0;
        var index = 0;
        foreach (var packet in packets.EnumerateArray())
        {
            if (Number(packet, "stream_index") != stream.Index) throw new InvalidDataException("Video audio packet changed stream.");
            var pts = Number(packet, "pts"); var dts = Number(packet, "dts"); var ticks = Number(packet, "duration");
            if (pts != dts || ticks <= 0) throw new NotSupportedException("Video audio packet order or duration needs a handler.");
            if (index == 0) first = pts;
            else if (Math.Abs(checked(pts - end)) > 1)
                throw new NotSupportedException("The selected audio track has a timestamp gap or overlap.");
            end = checked(pts + ticks);
            duration = checked(duration + ticks);
            if (packet.TryGetProperty("side_data_list", out var sideData))
            {
                if (sideData.ValueKind != JsonValueKind.Array || sideData.GetArrayLength() > 2)
                    throw new NotSupportedException("Video audio packet side data needs a handler.");
                foreach (var side in sideData.EnumerateArray())
                {
                    if (side.GetProperty("side_data_type").GetString() != "Skip Samples")
                        throw new NotSupportedException("Video audio packet side data needs a handler.");
                    var leading = Number(side, "skip_samples"); var trailing = Number(side, "discard_padding");
                    if (leading < 0 || trailing < 0 || leading > codecFrame || trailing > codecFrame ||
                        leading > 0 && index != 0 || trailing > 0 && index != packets.GetArrayLength() - 1)
                        throw new NotSupportedException("Video audio priming or terminal padding needs a handler.");
                    skip = checked(skip + leading); discard = checked(discard + trailing);
                }
            }
            index++;
        }

        using var frameDocument = JsonDocument.Parse(frameJson, new JsonDocumentOptions { MaxDepth = 12 });
        var frames = frameDocument.RootElement.GetProperty("frames");
        if (frames.ValueKind != JsonValueKind.Array || frames.GetArrayLength() is < 1 or > MaximumEntries)
            throw new InvalidDataException("The selected decoded frame count exceeds its bound.");
        long decoded = 0, frameFirst = 0, frameEnd = 0;
        var frameIndex = 0;
        foreach (var frame in frames.EnumerateArray())
        {
            if (Number(frame, "stream_index") != stream.Index) throw new InvalidDataException("Video audio frame changed stream.");
            var samples = Number(frame, "nb_samples");
            var pts = Number(frame, "pts"); var ticks = Number(frame, "duration");
            if (samples is < 1 or > 8192 || ticks <= 0)
                throw new NotSupportedException("Video audio frame size or duration needs a handler.");
            if (frameIndex == 0) frameFirst = pts;
            else if (Math.Abs((decimal)pts - frameEnd) > 1)
                throw new NotSupportedException("Decoded video audio has a timestamp gap or overlap.");
            // Matroska rounds each timestamp to its container clock. Compare
            // against cumulative decoded samples, not a sum of individually
            // rounded frame durations, whose error grows on long recordings.
            if (Math.Abs(((decimal)pts - frameFirst) * samplesPerTick - decoded) > samplesPerTick + 1)
                throw new NotSupportedException("Decoded audio timestamps do not follow the selected sample clock.");
            frameEnd = checked(pts + ticks);
            decoded = checked(decoded + samples);
            frameIndex++;
        }
        if (Math.Abs(((decimal)frameFirst - first) * samplesPerTick - skip) > samplesPerTick + 1)
            throw new NotSupportedException("Decoded frame timing disagrees with selected packet priming or continuity.");
        // The final packet duration already excludes terminal discard padding
        // in reviewed FFprobe timelines; adding or subtracting it here would
        // invent samples. Its explicit declaration is still bounded above.
        var tickSamples = (decimal)(checked(end - first)) * samplesPerTick - skip;
        var tickTolerance = codecFrame + samplesPerTick;
        if (tickSamples <= 0 || Math.Abs((decimal)decoded - tickSamples) > tickTolerance)
            throw new InvalidDataException("Decoded audio extent disagrees with the selected packet timeline.");
        long presented = decoded;
        if (stream.DurationTicks is { } declared)
        {
            var exact = (decimal)declared * numerator * rate / denominator;
            if (exact <= 0 || exact > long.MaxValue || Math.Abs(exact - decimal.Round(exact)) > .001m)
                throw new NotSupportedException("The selected track duration is not sample aligned.");
            presented = checked((long)decimal.Round(exact));
            if (presented > decoded || decoded - presented > codecFrame ||
                Math.Abs((decimal)presented - tickSamples) > tickTolerance)
                throw new InvalidDataException("The selected audio duration conflicts with decoded priming or padding.");
        }
        if (presented <= 0 || stream.Channels is not > 0 ||
            presented > AudioEncodingAdapter.MaximumDecodedBytes / (stream.Channels.Value * 8L))
            throw new InvalidDataException("The selected audio exceeds its decoded extent budget.");
        return new(decoded, presented, first, end, skip, discard, packets.GetArrayLength());
    }

    private static long Number(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !long.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException("Video audio packet or frame is missing an integer declaration.");
        return number;
    }
}
