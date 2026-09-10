using System.Buffers.Binary;
using ContextSuite.Core.Audio;

internal static class FlacMetadataContracts
{
    public static void Run(Action<bool, string> check)
    {
        var comment = new byte[] { 1, 0, 0, 0, (byte)'A', 0, 0, 0, 0 };
        var picture = new byte[32];
        var cue = new byte[396];
        var sourceBytes = File((4, comment), (1, new byte[128]), (6, picture), (5, cue));
        var source = FlacMetadata.Parse(sourceBytes);
        check(source.AudioOffset == sourceBytes.Length && source.Channels == 2 && source.SampleRate == 48000 &&
            source.SampleBits == 16 && source.SampleFrames == 96000, "FLAC metadata: bounded native block inventory and stream declarations");
        var encoded = FlacMetadata.Parse(File((4, new byte[8])));
        var header = FlacMetadata.CreateRecompressionHeader(source, encoded);
        var output = FlacMetadata.Parse(header);
        FlacMetadata.RequirePreservedMetadata(source, output);
        check(output.Blocks.Length == 4 && output.Blocks[1].Data.AsSpan().SequenceEqual(comment) &&
            output.Blocks[2].Data.AsSpan().SequenceEqual(picture) && output.Blocks[3].Data.AsSpan().SequenceEqual(cue),
            "FLAC metadata: descriptive bytes and order survive while zero padding is omitted");
        check(header[4] == 0 && header[42] == 4 && (header[header.Length - 400] & 128) != 0,
            "FLAC metadata: exactly the final reconciled block has the last-block flag");
        Reject(() => FlacMetadata.RequirePreservedMetadata(source, encoded), check,
            "FLAC metadata: engine tag rewriting is caught independently of sample equality");
        foreach (var type in new byte[] { 2, 3, 7, 126 })
        {
            var guarded = FlacMetadata.Parse(File((type, type == 2 ? new byte[4] : [])));
            check(!FlacMetadata.RecompressionRestrictions(guarded).IsEmpty,
                "FLAC metadata: specialized preservation required for block " + type);
            try { FlacMetadata.CreateRecompressionHeader(guarded, encoded); check(false, "FLAC metadata: unsafe recompression rejected"); }
            catch (NotSupportedException) { check(true, "FLAC metadata: unsafe recompression rejected"); }
        }
        var mismatch = File(); mismatch[20] ^= 0x02;
        Reject(() => FlacMetadata.CreateRecompressionHeader(source, FlacMetadata.Parse(mismatch)), check,
            "FLAC metadata: changed channels/precision cannot be hidden by copying original tags");
        var checksum = File(); checksum[26] = 1;
        Reject(() => FlacMetadata.CreateRecompressionHeader(FlacMetadata.Parse(checksum), encoded), check,
            "FLAC metadata: known source checksum must match the new declaration");
        var newHeader = File(); newHeader[8] = 8; newHeader[10] = 8;
        output = FlacMetadata.Parse(FlacMetadata.CreateRecompressionHeader(source, FlacMetadata.Parse(newHeader)));
        check(output.Blocks[0].Data[0] == 8, "FLAC metadata: encoded block-size fields belong to new frames");
        foreach (var malformed in new[] { File((0, new byte[34])), File((4, new byte[8]), (4, new byte[8])),
            File((3, new byte[1])), File((2, new byte[3])), File((5, new byte[395])), File((6, new byte[31])),
            File((1, new byte[] { 1 })), File((127, [])) })
            Reject(() => FlacMetadata.Parse(malformed), check, "FLAC metadata: forbidden, duplicate or malformed block rejected");
        var tooMany = File(Enumerable.Range(0, FlacMetadata.MaximumBlocks).Select(_ => ((byte)1, Array.Empty<byte>())).ToArray());
        Reject(() => FlacMetadata.Parse(tooMany), check, "FLAC metadata: zero-sized blocks consume the record budget");
        for (var length = 0; length < sourceBytes.Length; length++)
        {
            try { FlacMetadata.Parse(sourceBytes.AsSpan(0, length)); throw new Exception("Truncated metadata was accepted."); }
            catch (InvalidDataException) { }
        }
        check(true, "FLAC metadata: every truncated prefix is rejected without out-of-range reads");
    }

    private static void Reject(Action action, Action<bool, string> check, string message)
    {
        try { action(); check(false, message); }
        catch (InvalidDataException) { check(true, message); }
    }

    private static byte[] File(params (byte Type, byte[] Data)[] additional)
    {
        var streamInfo = new byte[34]; streamInfo[0] = 16; streamInfo[2] = 16;
        BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10), (48000UL << 44) | (1UL << 41) | (15UL << 36) | 96000);
        var blocks = new[] { ((byte)0, streamInfo) }.Concat(additional).ToArray();
        using var stream = new MemoryStream(); stream.Write("fLaC"u8);
        for (var index = 0; index < blocks.Length; index++)
        {
            var (type, data) = blocks[index];
            stream.WriteByte((byte)(type | (index == blocks.Length - 1 ? 128 : 0)));
            stream.WriteByte((byte)(data.Length >> 16)); stream.WriteByte((byte)(data.Length >> 8)); stream.WriteByte((byte)data.Length);
            stream.Write(data);
        }
        return stream.ToArray();
    }
}
