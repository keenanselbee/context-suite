using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record M4aMetadataInventory(int SampleRate, int Channels, int PacketCount,
    ImmutableDictionary<string, string> Tags);

// One local AAC-LC track. Inventory never decodes samples or resolves references.
public static class M4aMetadata
{
    public const int MaximumMovieBytes = 16 * 1024 * 1024;

    public static async Task<M4aMetadataInventory> ReadAsync(Stream source, CancellationToken token)
    {
        if (!source.CanRead || !source.CanSeek || source.Length > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("M4A inventory needs a bounded seekable input.");
        var position = source.Position;
        try
        {
            source.Position = 0;
            byte[]? movie = null; var fileType = false; var count = 0;
            var media = new List<(long Start, long End)>(); var header = new byte[16];
            while (source.Position < source.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++count > M4aBoxes.MaximumBoxes || source.Length - source.Position < 8)
                    throw new InvalidDataException("M4A root atom count or extent is invalid.");
                var start = source.Position;
                await source.ReadExactlyAsync(header.AsMemory(0, 8), token);
                ulong size = BinaryPrimitives.ReadUInt32BigEndian(header); var headerSize = 8;
                var type = Encoding.Latin1.GetString(header, 4, 4);
                if (size == 1)
                {
                    if (source.Length - source.Position < 8) throw new InvalidDataException("Truncated extended M4A atom.");
                    await source.ReadExactlyAsync(header.AsMemory(8, 8), token);
                    size = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)); headerSize = 16;
                }
                if (size == 0 && type == "mdat") size = (ulong)(source.Length - start);
                if (size < (uint)headerSize || size > (ulong)(source.Length - start))
                    throw new InvalidDataException("M4A root atom crosses the file extent.");
                if (count == 1 && type != "ftyp") throw new NotSupportedException("M4A conversion requires an initial file-type atom.");
                var length = (long)size - headerSize;
                switch (type)
                {
                    case "ftyp":
                        if (fileType || length is < 8 or > 1024 || length % 4 != 0) throw new InvalidDataException("Invalid M4A file-type declaration.");
                        var brands = new byte[(int)length]; await source.ReadExactlyAsync(brands, token);
                        for (var i = 0; i < brands.Length; i += 4)
                        {
                            if (i == 4) continue; // Minor version is producer provenance.
                            if (Encoding.Latin1.GetString(brands, i, 4) is not ("M4A " or "isom" or "iso2" or "mp41" or "mp42"))
                                throw new NotSupportedException("This M4A brand needs a container policy.");
                        }
                        fileType = true; break;
                    case "moov":
                        if (movie is not null || length is 0 or > MaximumMovieBytes) throw new InvalidDataException("M4A movie count or byte budget is invalid.");
                        movie = new byte[(int)length]; await source.ReadExactlyAsync(movie, token); break;
                    case "mdat":
                        if (length == 0 || media.Count >= 16) throw new InvalidDataException("M4A media-data count or extent is invalid.");
                        media.Add((source.Position, start + (long)size)); break;
                    case "free": case "skip": break;
                    case "wide" when length == 0: break;
                    default: throw new NotSupportedException("M4A root atom needs a preservation handler: " + type);
                }
                source.Position = start + (long)size;
            }
            if (!fileType || movie is null || media.Count == 0) throw new InvalidDataException("M4A is missing file, movie or audio data.");
            token.ThrowIfCancellationRequested();
            var result = ReadMovie(movie, media, token);
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { source.Position = position; }
    }

    private static M4aMetadataInventory ReadMovie(ReadOnlyMemory<byte> movie, List<(long Start, long End)> media, CancellationToken token)
    {
        var boxes = new M4aBoxes(); var root = boxes.Read(movie, "mvhd", "trak", "udta");
        var mvhd = M4aBoxes.One(root, "mvhd").Span;
        Header(mvhd, 100);
        var movieScale = M4aBoxes.Number(mvhd, 12); var movieDuration = M4aBoxes.Number(mvhd, 16);
        if (movieScale == 0 || movieDuration == 0 || M4aBoxes.Number(mvhd, 20) != 65536 ||
            BinaryPrimitives.ReadUInt16BigEndian(mvhd[24..]) != 256 || !Identity(mvhd.Slice(36, 36)) ||
            mvhd.Slice(72, 24).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("M4A movie playback changes need a policy.");
        var track = boxes.Read(M4aBoxes.One(root, "trak"), "tkhd", "edts", "mdia");
        var tkhd = M4aBoxes.One(track, "tkhd").Span;
        if (tkhd.Length != 84 || tkhd[0] != 0 || tkhd[1] != 0 || tkhd[2] != 0 || tkhd[3] is not (3 or 7))
            throw new NotSupportedException("M4A track version, flags or extent needs a handler.");
        Timestamps(tkhd);
        if (M4aBoxes.Number(tkhd, 12) == 0 || M4aBoxes.Number(tkhd, 20) != movieDuration ||
            BinaryPrimitives.ReadUInt16BigEndian(tkhd[32..]) != 0 || BinaryPrimitives.ReadUInt16BigEndian(tkhd[36..]) != 256 ||
            !Identity(tkhd.Slice(40, 36)) || tkhd.Slice(76, 8).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("M4A track playback changes need a policy.");
        var mdia = boxes.Read(M4aBoxes.One(track, "mdia"), "mdhd", "hdlr", "minf");
        var mdhd = M4aBoxes.One(mdia, "mdhd").Span; Header(mdhd, 24);
        var scale = M4aBoxes.Number(mdhd, 12); var duration = M4aBoxes.Number(mdhd, 16);
        if (scale == 0 || duration == 0) throw new InvalidDataException("M4A media timing is empty.");
        Handler(M4aBoxes.One(mdia, "hdlr").Span, "soun"u8);
        var minf = boxes.Read(M4aBoxes.One(mdia, "minf"), "smhd", "dinf", "stbl");
        var sound = M4aBoxes.One(minf, "smhd").Span; M4aBoxes.FullBox(sound, 8);
        if (sound.Length != 8 || sound[4..].IndexOfAnyExcept((byte)0) >= 0) throw new NotSupportedException("M4A sound balance needs a policy.");
        LocalReference(boxes, M4aBoxes.One(minf, "dinf"));
        var table = boxes.Read(M4aBoxes.One(minf, "stbl"), "stsd", "stts", "stsc", "stsz", "stco", "co64", "sgpd", "sbgp");
        var stsd = M4aBoxes.One(table, "stsd"); M4aBoxes.FullBox(stsd.Span, 8);
        if (M4aBoxes.Number(stsd.Span, 4) != 1) throw new NotSupportedException("M4A requires one audio sample description.");
        var config = M4aAudioConfiguration.Read(M4aBoxes.One(boxes.Read(stsd[8..], "mp4a"), "mp4a"), boxes);
        if (scale != config.Rate) throw new NotSupportedException("M4A media clock differs from its AAC sample clock.");
        Edit(boxes, track, movieScale, movieDuration, scale, duration);
        var samples = M4aSampleTable.Validate(table, media, duration, token);
        var comments = new List<AudioComment>();
        var language = BinaryPrimitives.ReadUInt16BigEndian(mdhd[20..]);
        if (language != 0)
        {
            var letters = new[] { (language >> 10) & 31, (language >> 5) & 31, language & 31 };
            if ((language & 32768) != 0 || letters.Any(value => value is < 1 or > 26)) throw new NotSupportedException("M4A legacy language code needs a mapping.");
            comments.Add(new("language", new string(letters.Select(value => (char)(value + 96)).ToArray())));
        }
        var userData = Optional(root, "udta");
        if (userData is not null) comments.AddRange(M4aTags.Read(userData.Value, boxes));
        return new(config.Rate, config.Channels, samples, AudioCommentConversion.Read(comments, mapVorbisAliases: false));
    }

    private static void LocalReference(M4aBoxes boxes, ReadOnlyMemory<byte> memory)
    {
        var reference = M4aBoxes.One(boxes.Read(memory, "dref"), "dref"); M4aBoxes.FullBox(reference.Span, 8);
        if (M4aBoxes.Number(reference.Span, 4) != 1) throw new NotSupportedException("M4A requires one local data reference.");
        var url = M4aBoxes.One(boxes.Read(reference[8..], "url "), "url ").Span;
        if (!url.SequenceEqual(new byte[] { 0, 0, 0, 1 })) throw new NotSupportedException("External M4A data references are not followed.");
    }
    private static void Edit(M4aBoxes boxes, List<M4aBox> track, uint movieScale, uint movieDuration, uint scale, uint duration)
    {
        var edit = Optional(track, "edts");
        if (edit is null)
        {
            if ((ulong)movieDuration * scale != (ulong)duration * movieScale) throw new NotSupportedException("M4A timing without an edit needs a policy.");
            return;
        }
        var data = M4aBoxes.One(boxes.Read(edit.Value, "elst"), "elst").Span; M4aBoxes.FullBox(data, 20);
        if (data.Length != 20 || M4aBoxes.Number(data, 4) != 1 || M4aBoxes.Number(data, 8) != movieDuration ||
            BinaryPrimitives.ReadInt32BigEndian(data[12..]) < 0 || M4aBoxes.Number(data, 16) != 65536)
            throw new NotSupportedException("M4A complex edits or playback rates need a policy.");
        var start = M4aBoxes.Number(data, 12);
        if (start > duration || (ulong)movieDuration * scale > (ulong)(duration - start) * movieScale)
            throw new InvalidDataException("M4A edit exceeds the declared audio duration.");
    }
    internal static ReadOnlyMemory<byte>? Optional(List<M4aBox> boxes, string type)
    {
        var found = boxes.Where(box => box.Type == type).ToArray();
        if (found.Length > 1) throw new NotSupportedException("Repeated M4A " + type + " atoms need a policy.");
        if (found.Length == 0) return null;
        return found[0].Data;
    }
    internal static void Handler(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
    {
        M4aBoxes.FullBox(data, 24);
        if (!data.Slice(8, 4).SequenceEqual(type)) throw new NotSupportedException("This M4A media handler needs a policy.");
    }
    private static void Header(ReadOnlySpan<byte> data, int size)
    {
        M4aBoxes.FullBox(data, size);
        if (data.Length != size) throw new NotSupportedException("M4A version-zero header extent is unsupported.");
        Timestamps(data);
    }
    private static void Timestamps(ReadOnlySpan<byte> data)
    {
        if (data.Slice(4, 8).IndexOfAnyExcept((byte)0) >= 0) throw new NotSupportedException("M4A creation/modification timestamps need a preservation mapping.");
    }
    private static bool Identity(ReadOnlySpan<byte> matrix)
    {
        for (var i = 0; i < 9; i++)
            if (M4aBoxes.Number(matrix, i * 4) != (i is 0 or 4 ? 65536U : i == 8 ? 1073741824U : 0U)) return false;
        return true;
    }
}
