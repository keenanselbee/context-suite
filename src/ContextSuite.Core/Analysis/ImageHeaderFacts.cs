using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Analysis;

// Declarations from the existing bounded prefix only; never decode or follow metadata offsets.
internal static class ImageHeaderFacts
{
    public static void Add(string format, ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        var parsed = ImmutableArray.CreateBuilder<AnalysisFact>();
        try
        {
            switch (format)
            {
                case "jpeg": Jpeg(bytes, parsed); break;
                case "gif": Gif(bytes, fileBytes, parsed); break;
                case "bmp": Bitmap(bytes, fileBytes, parsed); break;
                case "webp": WebP(bytes, fileBytes, parsed); break;
            }
            facts.AddRange(parsed);
        }
        catch (InvalidDataException)
        {
            warnings.Add("The image header is incomplete, unsupported or inconsistent within the inspected bytes. Image dimensions are unavailable.");
        }
        foreach (var (id, label) in new[] { ("transparency", "Transparency"), ("animation", "Animation"),
                     ("orientation", "Display orientation"), ("color-profile", "Color profile") })
            facts.Add(new("image." + id, "Image", label, Availability: FactAvailability.Unavailable));
    }

    private static void Dimensions(ImmutableArray<AnalysisFact>.Builder facts, long width, long height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException();
        facts.Add(new("image.width", "Image", "Declared width", Integer: width));
        facts.Add(new("image.height", "Image", "Declared height", Integer: height));
    }

    private static void Gif(ReadOnlySpan<byte> bytes, long fileBytes, ImmutableArray<AnalysisFact>.Builder facts)
    {
        if (bytes.Length < 13) throw new InvalidDataException();
        var entries = (bytes[10] & 0x80) != 0 ? 2 << (bytes[10] & 7) : 0;
        if (13L + 3 * entries > fileBytes) throw new InvalidDataException();
        Dimensions(facts, BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]));
        facts.Add(new("gif.version", "Image", "GIF version", Text: Encoding.ASCII.GetString(bytes.Slice(3, 3))));
        facts.Add(new("gif.global-colors", "Image", "Declared global palette entries", Integer: entries));
    }

    private static void Bitmap(ReadOnlySpan<byte> bytes, long fileBytes, ImmutableArray<AnalysisFact>.Builder facts)
    {
        if (bytes.Length < 26) throw new InvalidDataException();
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]);
        var pixels = BinaryPrimitives.ReadUInt32LittleEndian(bytes[10..]);
        if (size is not (12 or 40 or 108 or 124) || 14L + size > bytes.Length || pixels < 14L + size || pixels > fileBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[6..]) != 0) throw new InvalidDataException();
        if (size == 12)
        {
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]) != 1 || bits is not (1 or 4 or 8 or 24)) throw new InvalidDataException();
            Dimensions(facts, BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]));
            facts.Add(new("bmp.bits", "Image", "Declared bits per pixel", Integer: bits));
        }
        else
        {
            var width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]);
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]);
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes[30..]);
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) != 1 || height == int.MinValue ||
                (bits is not (1 or 4 or 8 or 16 or 24 or 32) && !(bits == 0 && compression is 4 or 5)) ||
                (height < 0 && compression is not (0 or 3))) throw new InvalidDataException();
            Dimensions(facts, width, Math.Abs(height));
            facts.Add(new("bmp.bits", "Image", "Declared bits per pixel", Integer: bits));
            facts.Add(new("bmp.compression", "Image", "Raw bitmap compression", Integer: compression));
            if (compression is 0 or 3)
                facts.Add(new("bmp.row-order", "Image", "Declared row order", Text: height < 0 ? "Top down" : "Bottom up"));
        }
        facts.Add(new("bmp.header-size", "Image", "Bitmap header bytes", Integer: size));
    }

    private static void Jpeg(ReadOnlySpan<byte> bytes, ImmutableArray<AnalysisFact>.Builder facts)
    {
        var offset = 2;
        for (var records = 0; records < 256 && offset < bytes.Length; records++)
        {
            if (bytes[offset++] != 0xff) throw new InvalidDataException();
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            // Stop before entropy-coded scans. Do not search compressed samples for markers.
            if (marker is 0xda or 0xd9 or 0xd8 or 0x00 or >= 0xd0 and <= 0xd7) break;
            if (marker == 0x01) continue;
            if (offset > bytes.Length - 2) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || length > bytes.Length - offset) break;
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3)
            {
                if (length < 8) break;
                var precision = bytes[offset + 2];
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]);
                var components = bytes[offset + 7];
                if (components == 0 || length != 8 + 3 * components || width == 0 ||
                    (marker == 0xc0 ? precision != 8 : marker == 0xc3 ? precision is < 2 or > 16 : precision is not (8 or 12))) break;
                if (height == 0)
                {
                    facts.Add(new("image.width", "Image", "Declared width", Integer: width));
                    facts.Add(new("image.height", "Image", "Declared height (deferred)", Availability: FactAvailability.Unavailable));
                }
                else Dimensions(facts, width, height);
                facts.Add(new("jpeg.precision", "Image", "Declared sample precision", Integer: precision));
                facts.Add(new("jpeg.components", "Image", "Declared frame components", Integer: components));
                facts.Add(new("jpeg.frame", "Image", "Frame coding", Text: marker switch
                { 0xc0 => "Baseline DCT", 0xc1 => "Extended sequential DCT", 0xc2 => "Progressive DCT", _ => "Lossless" }));
                return;
            }
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xcc)) break;
            offset += length;
        }
        throw new InvalidDataException();
    }

    private static void WebP(ReadOnlySpan<byte> bytes, long fileBytes, ImmutableArray<AnalysisFact>.Builder facts)
    {
        if (bytes.Length < 20) throw new InvalidDataException();
        var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        if (end > fileBytes || end < 20 || 20L + size + (size & 1) > end) throw new InvalidDataException();
        var tag = bytes.Slice(12, 4);
        var data = bytes[20..];
        if (tag.SequenceEqual("VP8X"u8) && size >= 10 && data.Length >= 10)
        {
            var width = 1L + data[4] + (data[5] << 8) + (data[6] << 16);
            var height = 1L + data[7] + (data[8] << 8) + (data[9] << 16);
            if (width * height > uint.MaxValue) throw new InvalidDataException();
            Dimensions(facts, width, height);
            facts.Add(new("webp.alpha-flag", "Image", "Declared alpha feature", Boolean: (data[0] & 0x10) != 0));
            facts.Add(new("webp.animation-flag", "Image", "Declared animation feature", Boolean: (data[0] & 2) != 0));
        }
        else if (tag.SequenceEqual("VP8L"u8) && size >= 5 && data.Length >= 5 && data[0] == 0x2f)
        {
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
            if ((packed >> 29) != 0) throw new InvalidDataException();
            Dimensions(facts, (packed & 0x3fff) + 1, ((packed >> 14) & 0x3fff) + 1);
            facts.Add(new("webp.alpha-flag", "Image", "Declared alpha feature", Boolean: (packed & 0x10000000) != 0));
        }
        else if (tag.SequenceEqual("VP8 "u8) && size >= 10 && data.Length >= 10 && (data[0] & 1) == 0 &&
                 data.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
            Dimensions(facts, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3fff,
                BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3fff);
        else throw new InvalidDataException();
        facts.Add(new("webp.header", "Image", "First image header", Text: Encoding.ASCII.GetString(tag).TrimEnd()));
    }
}
