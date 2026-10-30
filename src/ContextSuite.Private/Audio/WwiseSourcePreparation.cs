using System.Buffers.Binary;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Writes a canonical PCM16 RIFF input for Wwise. The source parser has already
// refused metadata that cannot be represented here. An admitted +1.0 maps to
// the nearest PCM16 endpoint, +32767; no out-of-range source is saturated.
internal static class WwiseSourcePreparation
{
    internal static async Task<WwiseWaveInput> NormalizeAsync(FileStream source, WwiseWaveInput input,
        string destination, CancellationToken token)
    {
        if (!input.Floating) throw new ArgumentException("Float32 WAV is required.", nameof(input));
        var pcmBytes = checked(input.Frames * input.Channels * 2);
        var riffBytes = checked(36 + pcmBytes + (input.Loop is null ? 0 : 68));
        if (riffBytes + 8 > 64L * 1024 * 1024 || riffBytes > uint.MaxValue)
            throw new NotSupportedException("Prepared PCM16 WAV exceeds the Wwise input budget.");
        await using var writer = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 65536, FileOptions.Asynchronous);
        var header = new byte[44];
        "RIFF"u8.CopyTo(header); Write32(header, 4, checked((uint)riffBytes));
        "WAVEfmt "u8.CopyTo(header.AsSpan(8)); Write32(header, 16, 16);
        Write16(header, 20, 1); Write16(header, 22, checked((ushort)input.Channels));
        Write32(header, 24, checked((uint)input.SampleRate));
        Write32(header, 28, checked((uint)(input.SampleRate * input.Channels * 2)));
        Write16(header, 32, checked((ushort)(input.Channels * 2))); Write16(header, 34, 16);
        "data"u8.CopyTo(header.AsSpan(36)); Write32(header, 40, checked((uint)pcmBytes));
        await writer.WriteAsync(header, token);
        source.Position = input.DataOffset;
        var original = new byte[65536]; var normalized = new byte[32768];
        var samplesPerBuffer = normalized.Length / 2;
        for (long remaining = input.Frames * input.Channels; remaining > 0;)
        {
            var samples = (int)Math.Min(remaining, samplesPerBuffer);
            await source.ReadExactlyAsync(original.AsMemory(0, samples * 4), token);
            for (var sample = 0; sample < samples; sample++)
            {
                var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(sample * 4)));
                if (!float.IsFinite(value) || value is < -1f or > 1f)
                    throw new NotSupportedException("Wwise float WAV contains a nonfinite or out-of-range sample.");
                var pcm = (short)Math.Clamp(Math.Round(value * 32768d), -32768d, 32767d);
                Write16(normalized, sample * 2, unchecked((ushort)pcm));
            }
            await writer.WriteAsync(normalized.AsMemory(0, samples * 2), token);
            remaining -= samples;
        }
        if (input.Loop is { } loop)
        {
            var chunk = new byte[68]; "smpl"u8.CopyTo(chunk); Write32(chunk, 4, 60);
            Write32(chunk, 16, checked((uint)Math.Round(1_000_000_000d / input.SampleRate)));
            Write32(chunk, 20, 60); Write32(chunk, 36, 1);
            Write32(chunk, 52, checked((uint)loop.StartSample));
            Write32(chunk, 56, checked((uint)(loop.EndSampleExclusive - 1)));
            await writer.WriteAsync(chunk, token);
        }
        await writer.FlushAsync(token);
        writer.Flush(true);
        await writer.DisposeAsync();
        using var prepared = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.SequentialScan);
        var result = await WwiseWaveInput.ReadAsync(prepared, token);
        if (result.Floating || result.SampleRate != input.SampleRate || result.Channels != input.Channels ||
            result.Frames != input.Frames || result.Loop != input.Loop)
            throw new InvalidDataException("Prepared PCM16 WAV changed the planned sample framing or loop.");
        return result;
    }

    private static void Write16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], value);
    private static void Write32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
