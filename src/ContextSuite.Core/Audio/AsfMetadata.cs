using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Audio;

public sealed record AsfMetadataInventory(string Codec, int SampleRate, int Channels, int SampleBits,
    ulong Packets, uint PacketBytes, ImmutableDictionary<string, string> Tags,
    ImmutableArray<FlacMetadataBlock> Pictures, ImmutableArray<string> UnsupportedObjects)
{
    public void RequireConversionSupport()
    {
        if (!UnsupportedObjects.IsEmpty)
            throw new NotSupportedException("WMA information needs a preservation handler: " + string.Join(", ", UnsupportedObjects));
    }
}

// Complete ASF object inventory, not packet decoding. Only one local audio
// stream is admitted; scripts, alternate streams and protection are never run.
public static partial class AsfMetadata
{
    public const int MaximumHeaderBytes = 16 * 1024 * 1024;
    public const int MaximumObjects = 4096;
    private static readonly Guid Header = new("75b22630-668e-11cf-a6d9-00aa0062ce6c");
    private static readonly Guid Data = new("75b22636-668e-11cf-a6d9-00aa0062ce6c");
    private static readonly Guid FileProperties = new("8cabdca1-a947-11cf-8ee4-00c00c205365");
    private static readonly Guid StreamProperties = new("b7dc0791-a9b7-11cf-8ee6-00c00c205365");
    private static readonly Guid Extension = new("5fbf03b5-a92e-11cf-8ee3-00c00c205365");
    private static readonly Guid Content = new("75b22633-668e-11cf-a6d9-00aa0062ce6c");
    private static readonly Guid ExtendedContent = new("d2d0a440-e307-11d2-97f0-00a0c95ea850");
    private static readonly Guid Metadata = new("c5f8cbea-5baf-4877-8467-aa8c44fa4cca");
    private static readonly Guid Library = new("44231c94-9498-49d1-a141-1d134e457054");
    private static readonly Guid CodecList = new("86d15240-311d-11d0-a3a4-00a0c90348f6");
    private static readonly Guid Padding = new("1806d474-cadf-4509-a4ba-9aabcb96aae8");
    private static readonly Guid SimpleIndex = new("33000890-e5b1-11cf-89f4-00a0c90349cb");
    private static readonly Guid Audio = new("f8699e40-5b4d-11cf-a8fd-00805f5c442b");
    private static readonly Guid NoCorrection = new("20fb5700-5b55-11cf-a8fd-00805f5c442b");
    private static readonly Guid Spread = new("bfc3cd50-618f-11cf-8bb2-00aa00b4e220");
    private static readonly Guid[] Protected = [new("2211b3fb-bd23-11d2-b4b7-00a0c955fc6e"),
        new("298ae614-2622-4c17-b935-dae07ee9289c"), new("43058533-6981-49e6-9b74-ad12cb86d58c")];

    public static async Task<AsfMetadataInventory> ReadAsync(Stream source, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Length is < 30 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("ASF inventory requires a bounded seekable input.");
        var position = source.Position;
        try
        {
            token.ThrowIfCancellationRequested(); source.Position = 0;
            var prefix = new byte[30]; await source.ReadExactlyAsync(prefix, token);
            var size = U64(prefix, 16); var count = U32(prefix, 24);
            if (new Guid(prefix.AsSpan(0, 16)) != Header || size is < 30 or > MaximumHeaderBytes || size > (ulong)source.Length ||
                count is 0 or > MaximumObjects || prefix[28] != 1 || prefix[29] != 2)
                throw new InvalidDataException("ASF header extent, object count or reserved fields are invalid.");
            var bytes = new byte[(int)size - 30]; await source.ReadExactlyAsync(bytes, token);
            var state = new Inventory(token);
            if (state.ReadObjects(bytes, false) != count) throw new InvalidDataException("ASF header object count disagrees with its extent.");
            state.RequireHeader(source.Length);
            var dataSeen = false; var indexSeen = false;
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested(); state.CountObject();
                if (source.Length - source.Position < 24) throw new InvalidDataException("ASF root object header is truncated.");
                var start = source.Position;
                await source.ReadExactlyAsync(prefix.AsMemory(0, 24), token);
                var id = new Guid(prefix.AsSpan(0, 16)); var extent = U64(prefix, 16);
                if (extent < 24 || extent > (ulong)(source.Length - start)) throw new InvalidDataException("ASF root object exceeds its file extent.");
                RefuseProtection(id);
                if (id == Data && !dataSeen)
                {
                    if (extent < 50 || indexSeen) throw new InvalidDataException("ASF data object framing is invalid.");
                    var dataHeader = new byte[26]; await source.ReadExactlyAsync(dataHeader, token);
                    if (new Guid(dataHeader.AsSpan(0, 16)) != state.FileId || U64(dataHeader, 16) != state.Packets ||
                        dataHeader[24] != 1 || dataHeader[25] != 1 ||
                        extent - 50 != checked(state.Packets * state.PacketBytes))
                        throw new InvalidDataException("ASF data identity, packet count or extent disagrees with its header.");
                    dataSeen = true;
                }
                else if (id == SimpleIndex && dataSeen && !indexSeen)
                {
                    if (extent > MaximumHeaderBytes || extent < 56) throw new InvalidDataException("ASF simple index exceeds its bounds.");
                    var index = new byte[(int)extent - 24]; await source.ReadExactlyAsync(index, token);
                    if (new Guid(index.AsSpan(0, 16)) != state.FileId || U64(index, 16) == 0 ||
                        U32(index, 24) == 0 || U32(index, 24) > state.Packets ||
                        32L + 6L * U32(index, 28) != index.Length)
                        throw new InvalidDataException("ASF simple index identity or extent is invalid.");
                    for (var offset = 32; offset < index.Length; offset += 6)
                        if (U16(index, offset + 4) > U32(index, 24) ||
                            (ulong)U32(index, offset) + U16(index, offset + 4) > state.Packets)
                            throw new InvalidDataException("ASF index points beyond its packet extent.");
                    indexSeen = true;
                }
                else throw new NotSupportedException("An additional ASF root object needs a preservation handler.");
                source.Position = start + (long)extent;
            }
            if (!dataSeen) throw new InvalidDataException("ASF data object is missing.");
            return new(state.Codec!, state.Rate, state.Channels, state.Bits, state.Packets, state.PacketBytes,
                AudioCommentConversion.Read(state.Tags, false), state.Pictures.ToImmutable(),
                state.Unsupported.Order(StringComparer.Ordinal).ToImmutableArray());
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("ASF object data is truncated.", error); }
        finally { source.Position = position; }
    }

    private sealed partial class Inventory(CancellationToken token)
    {
        internal Guid FileId;
        internal ulong FileBytes, Packets;
        internal uint PacketBytes;
        internal int Rate, Channels, Bits;
        internal ushort StreamNumber, FormatTag;
        internal string? Codec;
        internal readonly List<AudioComment> Tags = [];
        internal readonly ImmutableArray<FlacMetadataBlock>.Builder Pictures = ImmutableArray.CreateBuilder<FlacMetadataBlock>();
        internal readonly HashSet<string> Unsupported = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> _seen = [];
        private readonly List<ushort> _metadataStreams = [];
        private int _objects, _textBytes, _pictureBytes, _descriptors;
        private ushort? _listedCodec;

        internal void CountObject()
        {
            token.ThrowIfCancellationRequested();
            if (++_objects > MaximumObjects) throw new InvalidDataException("ASF object count exceeds its budget.");
        }

        internal int ReadObjects(ReadOnlySpan<byte> bytes, bool extension)
        {
            var offset = 0; var count = 0;
            while (offset < bytes.Length)
            {
                CountObject(); count++;
                if (bytes.Length - offset < 24) throw new InvalidDataException("ASF child object header is truncated.");
                var id = new Guid(bytes.Slice(offset, 16)); var size = U64(bytes, offset + 16);
                if (size < 24 || size > (ulong)(bytes.Length - offset)) throw new InvalidDataException("ASF child object crosses its parent extent.");
                var body = bytes.Slice(offset + 24, (int)size - 24); offset += (int)size;
                RefuseProtection(id);
                if (id != Padding && !_seen.Add(id)) throw new NotSupportedException("Repeated ASF objects require a preservation policy.");
                if (id == Padding) continue;
                if (extension)
                {
                    if (id == Metadata || id == Library) ReadDescriptors(body, id == Library, true);
                    else Unsupported.Add("header extension " + id);
                }
                else if (id == FileProperties) ReadFile(body);
                else if (id == StreamProperties) ReadStream(body);
                else if (id == Content) ReadContent(body);
                else if (id == ExtendedContent) ReadDescriptors(body, false, false);
                else if (id == CodecList) ReadCodecList(body);
                else if (id == Extension)
                {
                    if (body.Length < 22 || new Guid(body[..16]) != new Guid("abd3d211-a9ba-11cf-8ee6-00c00c205365") ||
                        U16(body, 16) != 6 || U32(body, 18) != body.Length - 22)
                        throw new InvalidDataException("ASF extension framing is invalid.");
                    ReadObjects(body[22..], true);
                }
                else Unsupported.Add("header " + id);
            }
            return count;
        }

        internal void RequireHeader(long fileBytes)
        {
            if (!_seen.Contains(FileProperties) || !_seen.Contains(StreamProperties) || !_seen.Contains(Extension) || Codec is null || FileBytes != (ulong)fileBytes)
                throw new InvalidDataException("ASF requires matching file properties, one audio stream and header extension.");
            if (_listedCodec is { } listed && listed != FormatTag || _metadataStreams.Any(number => number != 0 && number != StreamNumber))
                throw new InvalidDataException("ASF codec list or metadata stream references disagree with the audio stream.");
        }

        private void ReadFile(ReadOnlySpan<byte> body)
        {
            if (body.Length != 80) throw new InvalidDataException("ASF file-properties extent is unsupported.");
            FileId = new Guid(body[..16]); FileBytes = U64(body, 16); Packets = U64(body, 32);
            PacketBytes = U32(body, 68);
            if (U32(body, 64) != 2 || PacketBytes is 0 or > 1024 * 1024 || U32(body, 72) != PacketBytes ||
                Packets == 0 || Packets > (ulong)AudioFileSource.MaximumFileBytes / PacketBytes || U64(body, 56) > ulong.MaxValue / 10000 ||
                U64(body, 40) < U64(body, 56) * 10000)
                throw new NotSupportedException("ASF broadcast, packet sizing or timing needs a conversion policy.");
        }

        private void ReadStream(ReadOnlySpan<byte> body)
        {
            if (body.Length < 54) throw new InvalidDataException("ASF stream properties are truncated.");
            var flags = U16(body, 48);
            if ((flags & 0x8000) != 0) throw new NotSupportedException("Protected WMA cannot be converted. Originals were kept.");
            StreamNumber = (ushort)(flags & 127);
            var formatBytes = U32(body, 40); var correctionBytes = U32(body, 44);
            if (new Guid(body[..16]) != Audio || StreamNumber == 0 || (flags & 0x7f80) != 0 || U32(body, 50) != 0 || U64(body, 32) != 0)
                throw new NotSupportedException("ASF requires one ordinary audio stream without timing offsets.");
            if (formatBytes < 18 || 54L + formatBytes + correctionBytes != body.Length)
                throw new InvalidDataException("ASF audio format or error-correction extent is invalid.");
            var format = body.Slice(54, (int)formatBytes);
            FormatTag = U16(format, 0); Channels = U16(format, 2); var rate = U32(format, 4); Bits = U16(format, 14);
            if (Channels is < 1 or > 8 || rate is < 8000 or > 192000 || U16(format, 12) == 0 ||
                Bits is not (0 or 8 or 16 or 24 or 32) || 18 + U16(format, 16) != format.Length)
                throw new NotSupportedException("WMA channels, rate, precision or codec configuration is outside policy.");
            Rate = (int)rate;
            Codec = FormatTag switch { 0x160 => "wmav1", 0x161 => "wmav2", 0x162 => "wmapro", 0x163 => "wmalossless", 0x0a => "wmavoice",
                _ => throw new NotSupportedException("This ASF audio codec has no WMA conversion policy.") };
            var correction = new Guid(body.Slice(16, 16)); var bytes = body[(54 + (int)formatBytes)..];
            if (correction == NoCorrection && bytes.IsEmpty) return;
            if (correction != Spread || bytes.Length < 7 || bytes[0] == 0 || U16(bytes, 1) == 0 ||
                U16(bytes, 3) == 0 || U16(bytes, 1) % U16(bytes, 3) != 0 || 7 + U16(bytes, 5) != bytes.Length)
                throw new NotSupportedException("ASF error-correction framing needs a verified handler.");
        }

        private void ReadCodecList(ReadOnlySpan<byte> body)
        {
            if (body.Length < 20 || new Guid(body[..16]) != new Guid("86d15241-311d-11d0-a3a4-00a0c90348f6") || U32(body, 16) != 1)
                throw new NotSupportedException("ASF codec provenance does not describe one audio codec.");
            var cursor = new Cursor(body[20..]);
            if (cursor.Word() != 2) throw new NotSupportedException("ASF codec list includes non-audio content.");
            Text(cursor.Take(cursor.Word() * 2)); Text(cursor.Take(cursor.Word() * 2));
            if (cursor.Word() != 2) throw new NotSupportedException("ASF codec identifier needs a handler.");
            _listedCodec = cursor.Word(); cursor.RequireEnd();
        }
    }

    private static void RefuseProtection(Guid id)
    {
        if (Protected.Contains(id)) throw new NotSupportedException("Protected WMA cannot be converted. Originals were kept.");
    }
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static ulong U64(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
}
