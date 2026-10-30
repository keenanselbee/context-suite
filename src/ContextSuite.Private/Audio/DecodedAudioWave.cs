using System.Buffers.Binary;

namespace ContextSuite.Private.Audio;

// A bounded, lossless transport for validated float64 samples. The caller
// streams the existing reference behind this header; no full-file allocation.
internal static class DecodedAudioWave
{
    internal static byte[] Header(long bytes, int rate, int channels, string? layout)
    {
        if (channels is < 1 or > 8 || rate is < 8000 or > 192000 || bytes <= 0 ||
            bytes > AudioEncodingAdapter.MaximumDecodedBytes || bytes % (channels * 8) != 0)
            throw new InvalidDataException("Invalid decoded audio extent or format.");
        uint mask = channels switch
        {
            1 => 4, 2 => 3,
            _ => layout switch
            {
                "2.1" => 0xb, "quad" => 0x33, "4.0" => 0x107,
                "5.0" => 0x37, "5.0(side)" => 0x607,
                "5.1" => 0x3f, "5.1(side)" => 0x60f,
                "6.1" => 0x70f, "7.1" => 0x63f,
                _ => throw new NotSupportedException("Decoded audio needs a known channel layout.")
            }
        };
        if (System.Numerics.BitOperations.PopCount(mask) != channels)
            throw new InvalidDataException("Decoded audio layout disagrees with its channels.");
        var result = new byte[80];
        "RIFF"u8.CopyTo(result); Write32(4, checked((uint)bytes + 72));
        "WAVEfmt "u8.CopyTo(result.AsSpan(8)); Write32(16, 40);
        Write16(20, 0xfffe); Write16(22, (ushort)channels);
        Write32(24, (uint)rate); Write32(28, (uint)(rate * channels * 8));
        Write16(32, (ushort)(channels * 8)); Write16(34, 64); Write16(36, 22); Write16(38, 64);
        Write32(40, mask); new Guid("00000003-0000-0010-8000-00aa00389b71").TryWriteBytes(result.AsSpan(44, 16));
        "fact"u8.CopyTo(result.AsSpan(60)); Write32(64, 4); Write32(68, checked((uint)(bytes / (channels * 8))));
        "data"u8.CopyTo(result.AsSpan(72)); Write32(76, (uint)bytes);
        return result;

        void Write16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), value);
        void Write32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
    }
}
