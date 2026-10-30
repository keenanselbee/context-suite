using System.Buffers.Binary;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Admit only uncompressed PCM16 and a single ordinary forward loop. Other
// chunks may carry metadata that the external-source conversion would omit.
internal sealed record WwiseWaveInput(int SampleRate, int Channels, long Frames,
    long DataOffset, long DataBytes, WemLoop? Loop, bool Floating)
{
    internal static async Task<WwiseWaveInput> ReadAsync(Stream source, CancellationToken token,
        bool allowGeneratedEncoderMarker = false)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 44 or > 64L * 1024 * 1024)
            throw new InvalidDataException("Wwise WAV input exceeds its supported file budget.");
        var position = source.Position;
        try
        {
            source.Position = 0;
            var head = new byte[12]; await source.ReadExactlyAsync(head, token);
            if (!head.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !head.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
                BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)) != source.Length - 8)
                throw new InvalidDataException("Wwise WAV requires a complete RIFF/WAVE file.");
            byte[]? fmt = null, smpl = null;
            uint? factFrames = null;
            long dataOffset = -1, dataBytes = -1;
            var generatedEncoderSeen = false;
            for (var chunks = 0; source.Position < source.Length && chunks < 7; chunks++)
            {
                if (source.Length - source.Position < 8) throw new InvalidDataException("WAV chunk header is truncated.");
                var chunk = new byte[8]; await source.ReadExactlyAsync(chunk, token);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
                var begin = source.Position;
                var end = begin + size;
                if (end + (size & 1) > source.Length) throw new InvalidDataException("WAV chunk exceeds its RIFF extent.");
                if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8))
                {
                    if (fmt is not null || dataBytes >= 0 || size is not (16 or 18 or 40))
                        throw new NotSupportedException("Wwise WAV requires one supported PCM16 or float32 format chunk.");
                    fmt = new byte[size]; await source.ReadExactlyAsync(fmt, token);
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
                {
                    if (fmt is null || dataBytes >= 0 || size == 0)
                        throw new InvalidDataException("WAV requires one nonempty data chunk after its format.");
                    dataOffset = begin; dataBytes = size;
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("smpl"u8))
                {
                    if (smpl is not null || size != 60)
                        throw new NotSupportedException("Wwise WAV supports one ordinary forward loop only.");
                    smpl = new byte[60]; await source.ReadExactlyAsync(smpl, token);
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("fact"u8))
                {
                    if (factFrames is not null || fmt is null || dataBytes >= 0 || size != 4)
                        throw new NotSupportedException("Wwise float WAV requires one ordinary fact sample count.");
                    var count = new byte[4]; await source.ReadExactlyAsync(count, token);
                    factFrames = BinaryPrimitives.ReadUInt32LittleEndian(count);
                }
                else if (chunk.AsSpan(0, 4).SequenceEqual("LIST"u8) && allowGeneratedEncoderMarker)
                {
                    if (generatedEncoderSeen || fmt is null || dataBytes >= 0 || size is < 18 or > 64)
                        throw new NotSupportedException("Generated WAV contains unexpected metadata.");
                    var marker = new byte[size]; await source.ReadExactlyAsync(marker, token);
                    if (!marker.AsSpan(0, 4).SequenceEqual("INFO"u8) ||
                        !marker.AsSpan(4, 4).SequenceEqual("ISFT"u8))
                        throw new NotSupportedException("Generated WAV contains unpreserved metadata.");
                    var textBytes = BinaryPrimitives.ReadUInt32LittleEndian(marker.AsSpan(8));
                    if (textBytes < 5 || 12 + textBytes + (textBytes & 1) != size ||
                        !marker.AsSpan(12, 4).SequenceEqual("Lavf"u8) || marker[11 + (int)textBytes] != 0 ||
                        (textBytes & 1) != 0 && marker[^1] != 0)
                        throw new NotSupportedException("Generated WAV encoder marker is unexpected.");
                    for (var index = 16; index < 11 + (int)textBytes; index++)
                        if (marker[index] is not (>= (byte)'0' and <= (byte)'9' or (byte)'.'))
                            throw new NotSupportedException("Generated WAV encoder marker is unexpected.");
                    generatedEncoderSeen = true;
                }
                else throw new NotSupportedException("WAV metadata needs a preservation policy before WEM encoding.");
                source.Position = end + (size & 1);
            }
            if (source.Position != source.Length || fmt is null || dataBytes <= 0)
                throw new InvalidDataException("WAV format or sample extent is incomplete.");
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(4));
            var block = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(12));
            var floating = tag is 3 or 0xfffe;
            var width = floating ? 4 : 2;
            if (tag is not (1 or 3 or 0xfffe) || channels is not (1 or 2) || rate is not (44100 or 48000) ||
                BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14)) != width * 8 || block != channels * width ||
                BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(8)) != (long)rate * block || dataBytes % block != 0)
                throw new NotSupportedException("Wwise WAV requires PCM16 or float32 mono/stereo at 44.1 or 48 kHz.");
            if (tag == 1 && (fmt.Length != 16 || factFrames is not null) ||
                tag == 3 && (fmt.Length is not (16 or 18) || fmt.Length == 18 && BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(16)) != 0) ||
                tag == 0xfffe && (fmt.Length != 40 || BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(16)) != 22 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(18)) != 32 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(fmt.AsSpan(20)) != (channels == 1 ? 4u : 3u) ||
                    !fmt.AsSpan(24, 16).SequenceEqual(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray())))
                throw new NotSupportedException("Wwise float WAV format extension or channel mask is unsupported.");
            var frames = dataBytes / block;
            if (factFrames is not null && factFrames != frames)
                throw new InvalidDataException("WAV fact sample count disagrees with its data.");
            WemLoop? loop = null;
            if (smpl is not null)
            {
                uint Value(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(smpl.AsSpan(offset));
                if (Value(0) != 0 || Value(4) != 0 || Value(8) is not 0 && Value(8) != (uint)Math.Round(1_000_000_000d / rate) ||
                    Value(12) is not (0 or 60) || Value(16) != 0 || Value(20) != 0 || Value(24) != 0 ||
                    Value(28) != 1 || Value(32) != 0 || Value(36) != 0 || Value(40) != 0 ||
                    Value(52) != 0 || Value(56) != 0)
                    throw new NotSupportedException("Wwise WAV loop metadata has unsupported pitch, cues or loop type.");
                var start = Value(44); var exclusiveEnd = (long)Value(48) + 1;
                if (start >= exclusiveEnd || exclusiveEnd > frames)
                    throw new InvalidDataException("WAV forward loop is outside its sample extent.");
                loop = new(start, exclusiveEnd);
            }
            if (floating)
            {
                source.Position = dataOffset;
                var samples = new byte[65536];
                for (long remaining = dataBytes; remaining > 0;)
                {
                    var take = (int)Math.Min(remaining, samples.Length);
                    await source.ReadExactlyAsync(samples.AsMemory(0, take), token);
                    for (var offset = 0; offset < take; offset += 4)
                    {
                        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(samples.AsSpan(offset)));
                        if (!float.IsFinite(value) || value is < -1f or > 1f)
                            throw new NotSupportedException("Wwise float WAV contains a nonfinite or out-of-range sample.");
                    }
                    remaining -= take;
                }
            }
            return new((int)rate, channels, frames, dataOffset, dataBytes, loop, floating);
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Wwise WAV input is truncated.", error); }
        finally { source.Position = position; }
    }
}
