using System.Buffers.Binary;

namespace ContextSuite.Private.Audio;

// A finite-signal boundary for the pinned resampler, not added recording time.
// Reflect endpoint samples so its unchanged filter can start and drain; retain
// only the independently calculated original time interval before encoding.
internal static class ShortAudioResampling
{
    internal const int MaximumSourceFrames = 256;
    internal const int ExtensionFrames = 1024;
    internal const int MaximumNativeBytes = 1024 * 1024;

    internal static byte[] Wave(ReadOnlySpan<byte> pcm, int rate, int channels, string? layout, bool extend)
    {
        if (channels is < 1 or > 8 || rate is < 8000 or > 192000 || pcm.IsEmpty || pcm.Length % (channels * 8) != 0)
            throw new InvalidDataException("Invalid finite resampling samples.");
        var frameBytes = channels * 8;
        var frames = pcm.Length / frameBytes;
        if (frames > MaximumSourceFrames * (extend ? 1 : 6))
            throw new InvalidDataException("Finite resampling exceeds its sample bound.");
        uint mask = channels switch
        {
            1 => 4,
            2 => 3,
            _ => layout switch
            {
                "quad" => 0x33, "5.0" => 0x37, "5.1" => 0x3f,
                "6.1" => 0x70f, "7.1" => 0x63f,
                _ => throw new NotSupportedException("This finite resampling channel layout needs a reviewed mapping.")
            }
        };
        if (System.Numerics.BitOperations.PopCount(mask) != channels)
            throw new InvalidDataException("Finite resampling channel layout disagrees with its samples.");
        var totalFrames = frames + (extend ? ExtensionFrames : 0);
        var result = new byte[68 + totalFrames * frameBytes];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length - 8);
        "WAVEfmt "u8.CopyTo(result.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), 0xfffe);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(22), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), (uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), (uint)(rate * frameBytes));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32), (ushort)frameBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(34), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(36), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(38), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(40), mask);
        new Guid("00000003-0000-0010-8000-00aa00389b71").TryWriteBytes(result.AsSpan(44, 16));
        "data"u8.CopyTo(result.AsSpan(60));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(64), (uint)(totalFrames * frameBytes));
        pcm.CopyTo(result.AsSpan(68));
        var period = Math.Max(1, 2 * (frames - 1));
        for (var index = frames; index < totalFrames; index++)
        {
            var phase = index % period;
            var source = frames == 1 ? 0 : Math.Min(phase, period - phase);
            pcm.Slice(source * frameBytes, frameBytes).CopyTo(result.AsSpan(68 + index * frameBytes));
        }
        return result;
    }

    // CaptureAsync counts and validates the entire decoded stream. Only this
    // small prefix is retained; long recordings never accumulate in memory.
    internal sealed class Prefix(int channels) : MemoryStream(MaximumSourceFrames * channels * 8)
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
            base.WriteAsync(buffer[..Math.Min(buffer.Length, MaximumSourceFrames * channels * 8 - (int)Length)], token);
    }
}
