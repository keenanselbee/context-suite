using System.Buffers.Binary;
using System.Text;

namespace ContextSuite.Core.Audio;

internal static class M4aTags
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UnicodeEncoding Utf16 = new(true, false, true);
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["\u00a9nam"] = "title", ["\u00a9ART"] = "artist", ["aART"] = "album_artist", ["\u00a9alb"] = "album",
        ["\u00a9cmt"] = "comment", ["\u00a9day"] = "date", ["\u00a9gen"] = "genre", ["cprt"] = "copyright",
        ["\u00a9wrt"] = "composer", ["\u00a9too"] = "encoder", ["\u00a9grp"] = "grouping", ["desc"] = "description"
    };
    public static List<AudioComment> Read(ReadOnlyMemory<byte> userData, M4aBoxes boxes)
    {
        var meta = M4aBoxes.One(boxes.Read(userData, "meta"), "meta"); M4aBoxes.FullBox(meta.Span, 4);
        var children = boxes.Read(meta[4..], "hdlr", "ilst");
        M4aMetadata.Handler(M4aBoxes.One(children, "hdlr").Span, "mdir"u8);
        var comments = new List<AudioComment>(); var textBytes = 0;
        foreach (var item in boxes.Read(M4aBoxes.One(children, "ilst")))
        {
            if (!Names.TryGetValue(item.Type, out var name) && item.Type is not ("trkn" or "disk"))
                throw new NotSupportedException("M4A metadata needs a preservation handler: " + item.Type);
            var data = M4aBoxes.One(boxes.Read(item.Data, "data"), "data").Span;
            if (data.Length < 8 || M4aBoxes.Number(data, 4) != 0) throw new NotSupportedException("M4A localized metadata needs a mapping.");
            var type = M4aBoxes.Number(data, 0); var value = data[8..];
            textBytes += value.Length;
            if (textBytes > 256 * 1024 || comments.Count >= 128) throw new InvalidDataException("M4A metadata exceeds its text budget.");
            string text;
            if (item.Type is "trkn" or "disk")
            {
                if (type != 0 || value.Length is not (6 or 8) || BinaryPrimitives.ReadUInt16BigEndian(value) != 0 ||
                    (value.Length == 8 && BinaryPrimitives.ReadUInt16BigEndian(value[6..]) != 0))
                    throw new InvalidDataException("M4A track/disc metadata has an invalid extent or reserved value.");
                var current = BinaryPrimitives.ReadUInt16BigEndian(value[2..]); var total = BinaryPrimitives.ReadUInt16BigEndian(value[4..]);
                if (current == 0 || (total != 0 && total < current)) throw new NotSupportedException("M4A track/disc numbering needs a mapping.");
                name = item.Type == "trkn" ? "track" : "disc";
                text = total == 0 ? current.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{current}/{total}";
            }
            else
            {
                try { text = type switch { 1 => Utf8.GetString(value), 2 => Utf16.GetString(value), _ => throw new NotSupportedException("M4A metadata data type needs a handler.") }; }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid M4A metadata text.", ex); }
                if (text.Contains('\0') || text.Length > 4096) throw new InvalidDataException("M4A metadata contains NUL or exceeds the value budget.");
            }
            comments.Add(new(name!, text));
        }
        return comments;
    }
}
