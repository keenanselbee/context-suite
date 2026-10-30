using System.Buffers.Binary;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Only for a newly encoded, privately owned candidate from the pinned runtime.
// Its muxer loses trailing padding that spans two MPEG packets. Reconcile the
// duration declaration against the captured reference; never change audio data.
internal static class Mp3GaplessPadding
{
    internal static async Task ReconcileAsync(FileStream candidate, long referenceFrames, CancellationToken token)
    {
        if (referenceFrames is < 1 or >= 47 || !candidate.CanWrite)
            throw new ArgumentException("Short MP3 reconciliation requires a writable candidate and 1-46 reference frames.");
        var inventory = await Mp3Metadata.ReadAsync(candidate, token);
        candidate.Position = inventory.AudioOffset;
        var header = new byte[4];
        await candidate.ReadExactlyAsync(header, token);
        var bits = BinaryPrimitives.ReadUInt32BigEndian(header);
        var version = (int)((bits >> 19) & 3);
        var mono = ((bits >> 6) & 3) == 3;
        if ((bits & 0xffe10000) != 0xffe10000 || version == 1)
            throw new InvalidDataException("Unexpected freshly encoded MPEG header.");
        var offset = 4 + (version == 3 ? mono ? 17 : 32 : mono ? 9 : 17);
        int[] bitrates = version == 3 ? [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320] :
            [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        var frameBytes = (version == 3 ? 144 : 72) * bitrates[(int)((bits >> 12) & 15)] * 1000 / inventory.SampleRate + (int)((bits >> 9) & 1);
        if (frameBytes < offset + 156) throw new InvalidDataException("MP3 duration header exceeds its MPEG frame.");
        var prefix = new byte[Math.Max(192, offset + 156)];
        candidate.Position = inventory.AudioOffset;
        await candidate.ReadExactlyAsync(prefix.AsMemory(0, Math.Min(frameBytes, prefix.Length)), token);
        var xing = prefix.AsSpan(offset, 4);
        if ((!xing.SequenceEqual("Xing"u8) && !xing.SequenceEqual("Info"u8)) ||
            BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(offset + 4)) != 15 ||
            !prefix.AsSpan(offset + 120, 9).SequenceEqual("Lavc63.1."u8))
            throw new InvalidDataException("Unexpected pinned MP3 duration header.");
        var encodedFrames = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(offset + 8));
        var audioBytes = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(offset + 12));
        if (encodedFrames != inventory.AudioFrames - 1 || audioBytes != candidate.Length - inventory.AudioOffset)
            throw new InvalidDataException("MP3 duration header does not match its frame inventory.");
        var packed = (prefix[offset + 141] << 16) | (prefix[offset + 142] << 8) | prefix[offset + 143];
        var delay = packed >> 12;
        var padding = packed & 4095;
        var samples = (long)encodedFrames * (version == 3 ? 1152 : 576);
        var declared = samples - delay - padding;
        var checksumOffset = offset + 154;
        var storedCrc = BinaryPrimitives.ReadUInt16BigEndian(prefix.AsSpan(checksumOffset));
        var standardCrc = Crc(prefix.AsSpan(0, checksumOffset));
        // The pinned muxer checks a fixed 190-byte prefix even for shorter mono
        // side information, including zero allocation padding beyond a short
        // first frame. Recognize that layout before correcting it.
        var legacy = prefix.ToArray();
        legacy[checksumOffset] = 0; legacy[checksumOffset + 1] = 0;
        if (storedCrc != standardCrc && storedCrc != Crc(legacy.AsSpan(0, 190)))
            throw new InvalidDataException("Encoded MP3 duration-header checksum is invalid.");
        if (declared == referenceFrames) return;
        if (delay != 576 || declared != 47)
            throw new InvalidDataException("MP3 duration mismatch is outside the reviewed short-recording case.");
        var correctedPadding = samples - delay - referenceFrames;
        if (correctedPadding is < 0 or > 4095)
            throw new InvalidDataException("MP3 trailing padding cannot represent the reference duration.");
        var corrected = (delay << 12) | (int)correctedPadding;
        prefix[offset + 141] = (byte)(corrected >> 16);
        prefix[offset + 142] = (byte)(corrected >> 8);
        prefix[offset + 143] = (byte)corrected;
        BinaryPrimitives.WriteUInt16BigEndian(prefix.AsSpan(checksumOffset), Crc(prefix.AsSpan(0, checksumOffset)));
        candidate.Position = inventory.AudioOffset + offset + 141;
        await candidate.WriteAsync(prefix.AsMemory(offset + 141, 3), token);
        candidate.Position = inventory.AudioOffset + checksumOffset;
        await candidate.WriteAsync(prefix.AsMemory(checksumOffset, 2), token);
        await candidate.FlushAsync(token);
    }

    private static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xa001));
        }
        return crc;
    }
}
