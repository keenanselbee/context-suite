using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record FlacFrame(long Sample, long Offset, int Samples, int Bytes);
public sealed record FlacSeekPoint(ulong Sample, ulong Offset, ushort Samples);

public static class FlacSeekTable
{
    public const int MaximumFrames = 131072;
    public const int MaximumPoints = 65536;

    public static ImmutableArray<FlacSeekPoint> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length % 18 != 0 || data.Length / 18 > MaximumPoints) throw new InvalidDataException("FLAC seek table exceeds its framing or point budget.");
        var points = ImmutableArray.CreateBuilder<FlacSeekPoint>();
        ulong previousSample = 0, previousOffset = 0;
        var placeholder = false;
        for (var index = 0; index < data.Length; index += 18)
        {
            var sample = BinaryPrimitives.ReadUInt64BigEndian(data[index..]);
            var offset = BinaryPrimitives.ReadUInt64BigEndian(data[(index + 8)..]);
            var samples = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 16)..]);
            if (sample == ulong.MaxValue) placeholder = true;
            else
            {
                if (placeholder || samples == 0 || (points.Count != 0 && (sample <= previousSample || offset <= previousOffset)))
                    throw new InvalidDataException("FLAC seek points must be unique, ordered frame references before placeholders.");
                previousSample = sample; previousOffset = offset;
            }
            points.Add(new(sample, offset, samples));
        }
        return points.ToImmutable();
    }

    public static void ValidateFrames(ImmutableArray<FlacFrame> frames, long audioBytes, long declaredSamples)
    {
        if (frames.IsDefaultOrEmpty || frames.Length > MaximumFrames || audioBytes <= 0 || declaredSamples < 0)
            throw new InvalidDataException("Invalid FLAC frame index limits.");
        long sample = 0, offset = 0;
        foreach (var frame in frames)
        {
            if (frame is null || frame.Sample != sample || frame.Offset != offset || frame.Samples is < 1 or > 65535 ||
                frame.Bytes < 8 || frame.Bytes > audioBytes - offset || sample > long.MaxValue - frame.Samples ||
                (frame.Samples < 16 && !ReferenceEquals(frame, frames[^1])))
                throw new InvalidDataException("FLAC frame index contains gaps, overlaps or invalid extents.");
            sample += frame.Samples; offset += frame.Bytes;
        }
        if (offset != audioBytes || (declaredSamples != 0 && sample != declaredSamples))
            throw new InvalidDataException("FLAC frame index does not cover the declared stream.");
    }

    public static void RequireMatches(ReadOnlySpan<byte> data, ImmutableArray<FlacFrame> frames)
    {
        RequireIndex(frames);
        foreach (var point in Read(data))
        {
            if (point.Sample == ulong.MaxValue) continue;
            var frame = frames[FindFrame(frames, point.Sample)];
            if ((ulong)frame.Sample != point.Sample || (ulong)frame.Offset != point.Offset || frame.Samples != point.Samples)
                throw new InvalidDataException("A FLAC seek point does not match its indexed audio frame.");
        }
    }

    public static byte[] Rebuild(ReadOnlySpan<byte> original, ImmutableArray<FlacFrame> frames)
    {
        RequireIndex(frames);
        var points = Read(original);
        var result = new byte[points.Length * 18];
        var count = 0;
        long previous = -1;
        foreach (var point in points)
        {
            if (point.Sample == ulong.MaxValue) continue;
            var frame = frames[FindFrame(frames, point.Sample)];
            if (frame.Sample == previous) continue;
            var offset = count++ * 18;
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(offset), (ulong)frame.Sample);
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(offset + 8), (ulong)frame.Offset);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset + 16), (ushort)frame.Samples);
            previous = frame.Sample;
        }
        // Coalesced points and original placeholders remain reserved table slots.
        for (; count < points.Length; count++) BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(count * 18), ulong.MaxValue);
        return result;
    }

    private static void RequireIndex(ImmutableArray<FlacFrame> frames)
    {
        if (frames.IsDefaultOrEmpty) throw new InvalidDataException("A complete FLAC frame index is required.");
        var last = frames[^1];
        if (last is null || last.Offset < 0 || last.Offset > long.MaxValue - last.Bytes)
            throw new InvalidDataException("Invalid final FLAC frame extent.");
        ValidateFrames(frames, last.Offset + last.Bytes, 0);
    }

    private static int FindFrame(ImmutableArray<FlacFrame> frames, ulong sample)
    {
        var last = frames[^1];
        if (sample >= (ulong)last.Sample + (uint)last.Samples) throw new InvalidDataException("FLAC seek point is outside the decoded stream.");
        var low = 0; var high = frames.Length - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if ((ulong)frames[middle].Sample <= sample) low = middle; else high = middle - 1;
        }
        return low;
    }
}
