using System.Buffers.Binary;
using System.Text;
using System.IO.Compression;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// Admission checks before invoking native decoders. This is not a replacement for semantic decode validation.
internal sealed record ImageContainer(ImageFormat Format, uint Width, uint Height, uint Depth, bool IsLossy, bool OtherMetadata,
    IReadOnlyDictionary<string, byte[]>? ExpectedProfiles = null, ImageResolution? Resolution = null)
{
    public const int MaximumFileBytes = 128 * 1024 * 1024;

    public static ImageContainer Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || bytes.Length > MaximumFileBytes) throw new InvalidDataException("Image size is unsupported.");
        ImageContainer info;
        if (bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) info = Png(bytes);
        else if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) info = Jpeg(bytes);
        else if (bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) info = WebP(bytes);
        else if (bytes[..2].SequenceEqual("BM"u8)) info = BitmapContainers.Bmp(bytes);
        else if (bytes.Length >= 18 && bytes[2] is 2 or 10) info = BitmapContainers.Tga(bytes);
        else throw new ImageFailureException(ImageFailure.UnsupportedInput);
        if (info.Width is 0 or > 16384 || info.Height is 0 or > 16384 || (ulong)info.Width * info.Height > 40_000_000)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        return info;
    }

    private static ImageContainer Png(ReadOnlySpan<byte> bytes)
    {
        uint width = 0, height = 0, depth = 0;
        var imageData = false;
        var icc = false;
        var customPrimaries = false;
        var other = false;
        var position = 8;
        var count = 0;
        var profiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ImageResolution? resolution = null;
        while (position <= bytes.Length - 12 && ++count <= 10000)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[position..]);
            if (length > bytes.Length - position - 12) throw new InvalidDataException("Truncated PNG chunk.");
            var type = Encoding.ASCII.GetString(bytes.Slice(position + 4, 4));
            var chunk = bytes.Slice(position + 8, (int)length);
            if (PngCrc(bytes.Slice(position + 4, (int)length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position + 8 + (int)length, 4)))
                throw new InvalidDataException("PNG chunk checksum is invalid.");
            if (count == 1 && type != "IHDR") throw new InvalidDataException("PNG is missing its header.");
            if (type == "IHDR")
            {
                if (count != 1 || length != 13) throw new InvalidDataException("Invalid PNG header.");
                width = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                height = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                depth = chunk[8];
                if (depth is not (1 or 2 or 4 or 8 or 16)) throw new InvalidDataException("Unsupported PNG precision.");
            }
            if (type is "acTL" or "fcTL" or "fdAT") throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (type is "cICP" or "mDCv" or "cLLi") throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (type == "pHYs")
            {
                if (length != 9 || chunk[8] > 1) throw new InvalidDataException("Invalid PNG physical resolution.");
                var x = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                var y = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                if (x == 0 || y == 0) throw new InvalidDataException("PNG physical resolution is undefined.");
                if (chunk[8] != 0 || x != y)
                {
                    var next = new ImageResolution(x, y, chunk[8] == 1 ? (ushort)3 : (ushort)1, chunk[8] == 1 ? 100u : 1u);
                    if (resolution is not null && resolution != next) throw new InvalidDataException("Conflicting PNG physical resolutions.");
                    resolution = next;
                }
            }
            if (type == "iCCP")
            {
                icc = true;
                var separator = chunk.IndexOf((byte)0);
                if (separator is < 1 or > 79 || separator + 2 >= chunk.Length || chunk[separator + 1] != 0)
                    throw new InvalidDataException("Invalid PNG ICC encoding.");
                AddProfile("icc", Inflate(chunk[(separator + 2)..], 4 * 1024 * 1024));
            }
            if (type == "eXIf")
            {
                var profile = chunk.StartsWith("Exif\0\0"u8) ? chunk.ToArray() : "Exif\0\0"u8.ToArray().Concat(chunk.ToArray()).ToArray();
                AddProfile("exif", profile);
            }
            if (type == "cHRM")
            {
                if (length != 32) throw new InvalidDataException("Invalid PNG chromaticity chunk.");
                uint[] srgb = [31270, 32900, 64000, 33000, 30000, 60000, 15000, 6000];
                for (var index = 0; index < srgb.Length; index++)
                    customPrimaries |= BinaryPrimitives.ReadUInt32BigEndian(chunk[(index * 4)..]) != srgb[index];
            }
            // XMP uses a standardized uncompressed iTXt keyword; other textual/unknown chunks
            // require explicit metadata removal until their preservation is implemented.
            var xmp = type == "iTXt" && chunk.StartsWith("XML:com.adobe.xmp\0\0\0\0\0"u8);
            if (xmp) AddProfile("xmp", chunk[22..].ToArray());
            var rawProfile = false;
            if (type is "zTXt" or "tEXt" && chunk.StartsWith("Raw profile type "u8))
            {
                var separator = chunk.IndexOf((byte)0);
                if (separator is < 1 or > 79) throw new InvalidDataException("Invalid PNG raw-profile keyword.");
                var name = Encoding.ASCII.GetString(chunk.Slice(17, separator - 17)).ToLowerInvariant();
                if (name is "icc" or "icm" or "exif" or "xmp")
                {
                    var data = chunk[(separator + 1)..];
                    if (type == "zTXt")
                    {
                        if (data.Length < 2 || data[0] != 0) throw new InvalidDataException("Invalid PNG raw-profile compression.");
                        data = Inflate(data[1..], 9 * 1024 * 1024);
                    }
                    AddProfile(name == "icm" ? "icc" : name, ParseRawProfile(data, name));
                    rawProfile = true;
                }
            }
            if (type is not ("IHDR" or "PLTE" or "IDAT" or "IEND") && char.IsUpper(type[0]))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (!xmp && !rawProfile && type is not ("IHDR" or "PLTE" or "IDAT" or "IEND" or "tRNS" or "iCCP" or "eXIf" or "sRGB" or "gAMA" or "cHRM" or "pHYs")) other = true;
            if (type == "IDAT") imageData = true;
            position += (int)length + 12;
            if (type == "IEND")
            {
                if (length != 0 || !imageData || position != bytes.Length) throw new InvalidDataException("Invalid PNG ending or trailing data.");
                if (customPrimaries && !icc) throw new ImageFailureException(ImageFailure.UnsupportedInput);
                return new(ImageFormat.Png, width, height, depth, false, other, profiles, resolution);
            }
        }
        throw new InvalidDataException("Incomplete PNG or too many chunks.");

        void AddProfile(string name, byte[] profile)
        {
            if (profile.Length is 0 or > 4 * 1024 * 1024) throw new InvalidDataException("PNG profile exceeds its size limit.");
            if (profiles.TryGetValue(name, out var existing) && !existing.SequenceEqual(profile))
                throw new InvalidDataException("Conflicting PNG profile copies cannot be preserved safely.");
            profiles[name] = profile;
            if (name == "icc") icc = true;
        }
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed, int maximum)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = zlib.Read(buffer)) != 0)
        {
            if (output.Length + read > maximum) throw new ImageFailureException(ImageFailure.ResourceLimit);
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static byte[] ParseRawProfile(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length > 9 * 1024 * 1024) throw new InvalidDataException("PNG raw profile exceeds its limit.");
        var tokens = Encoding.ASCII.GetString(data).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || tokens[0] != name || !int.TryParse(tokens[1], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var length) || length is < 1 or > 4 * 1024 * 1024)
            throw new InvalidDataException("Invalid PNG raw profile header.");
        var hex = string.Concat(tokens.Skip(2));
        if (hex.Length != length * 2) throw new InvalidDataException("PNG raw profile length does not match its contents.");
        try { return Convert.FromHexString(hex); }
        catch (FormatException error) { throw new InvalidDataException("PNG raw profile contains invalid hex data.", error); }
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var crc = (uint)index;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        return crc;
    }).ToArray();

    internal static uint PngCrc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 255];
        return ~crc;
    }

    private static ImageContainer Jpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes[^2] != 255 || bytes[^1] != 217) throw new InvalidDataException("Incomplete JPEG or trailing data.");
        uint width = 0, height = 0, depth = 0;
        var other = false;
        var position = 2;
        var segments = 0;
        var scanned = false;
        var ended = false;
        var profiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var iccParts = new SortedDictionary<byte, byte[]>();
        byte iccCount = 0;
        var iccBytes = 0;
        ImageResolution? resolution = null;
        while (position < bytes.Length && ++segments <= 10000)
        {
            if (bytes[position++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
            while (position < bytes.Length && bytes[position] == 255) position++;
            if (position >= bytes.Length) break;
            var marker = bytes[position++];
            if (marker == 217)
            {
                if (position != bytes.Length || !scanned) throw new InvalidDataException("Invalid JPEG ending.");
                ended = true;
                break;
            }
            if (marker is 216 or 0 or >= 208 and <= 215) throw new InvalidDataException("Invalid JPEG structure.");
            if (position > bytes.Length - 2) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
            if (length < 2 || length > bytes.Length - position) throw new InvalidDataException("Truncated JPEG segment.");
            var data = bytes.Slice(position + 2, length - 2);
            if (marker is 192 or 194)
            {
                if (data.Length < 6 || width != 0 || data[0] != 8 || data[5] is not (1 or 3))
                    throw new InvalidDataException("Only 8-bit grayscale/RGB JPEG is supported.");
                depth = data[0]; height = BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(data[3..]);
            }
            else if (marker is >= 193 and <= 207 && marker is not (196 or 200 or 204))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (marker is >= 224 and <= 239)
            {
                var text = Encoding.Latin1.GetString(data);
                if ((text.StartsWith("JFIF\0", StringComparison.Ordinal) && marker != 224) ||
                    (text.StartsWith("Exif\0", StringComparison.Ordinal) && (marker != 225 || !data.StartsWith("Exif\0\0"u8))) ||
                    (text.StartsWith("http://ns.adobe.com/xap/1.0/\0", StringComparison.Ordinal) && marker != 225) ||
                    (text.StartsWith("ICC_PROFILE\0", StringComparison.Ordinal) && marker != 226) ||
                    (text.StartsWith("Adobe", StringComparison.Ordinal) && marker != 238))
                    throw new InvalidDataException("JPEG metadata is stored in an invalid application marker.");
                if (text.StartsWith("JFIF\0", StringComparison.Ordinal))
                {
                    if (data.Length < 14 || data[7] > 2) throw new InvalidDataException("Invalid JPEG JFIF metadata.");
                    var x = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
                    var y = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
                    if (x == 0 || y == 0) throw new InvalidDataException("JPEG JFIF resolution is undefined.");
                    if (data[7] != 0 || x != y)
                    {
                        var next = new ImageResolution(x, y, (ushort)(data[7] + 1));
                        if (resolution is not null && resolution != next) throw new InvalidDataException("Conflicting JPEG resolutions.");
                        resolution = next;
                    }
                    if (data[12] != 0 || data[13] != 0) other = true;
                }
                if (text.StartsWith("MPF\0", StringComparison.Ordinal) || text.Contains("hdrgm", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("gainmap", StringComparison.OrdinalIgnoreCase) || text.Contains("hdr-gain-map", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("urn:iso:std:iso:ts:21496", StringComparison.OrdinalIgnoreCase))
                    throw new ImageFailureException(ImageFailure.UnsupportedInput);
                // Some decoders ignore application metadata after a scan. Never silently lose it.
                if (scanned) throw new ImageFailureException(ImageFailure.UnsupportedInput);
                if (marker == 225 && data.StartsWith("Exif\0\0"u8)) AddUniqueProfile(profiles, "exif", data.ToArray());
                if (marker == 225 && data.StartsWith("http://ns.adobe.com/xap/1.0/\0"u8)) AddUniqueProfile(profiles, "xmp", data[29..].ToArray());
                if (marker == 226 && data.StartsWith("ICC_PROFILE\0"u8))
                {
                    if (data.Length < 15 || data[12] == 0 || data[13] == 0 || data[12] > data[13] ||
                        (iccCount != 0 && iccCount != data[13]) || !iccParts.TryAdd(data[12], data[14..].ToArray()))
                        throw new InvalidDataException("Invalid or duplicate JPEG ICC segment.");
                    iccCount = data[13];
                    iccBytes += data.Length - 14;
                    if (iccBytes > 4 * 1024 * 1024) throw new InvalidDataException("JPEG ICC profile exceeds its limit.");
                }
                if (!text.StartsWith("Exif\0", StringComparison.Ordinal) && !text.StartsWith("ICC_PROFILE\0", StringComparison.Ordinal) &&
                    !text.StartsWith("http://ns.adobe.com/xap/1.0/\0", StringComparison.Ordinal) &&
                    !text.StartsWith("JFIF\0", StringComparison.Ordinal) && !text.StartsWith("Adobe", StringComparison.Ordinal)) other = true;
            }
            if (marker == 254) other = true;
            position += length;
            if (marker == 218)
            {
                if (width == 0 || data.Length < 4) throw new InvalidDataException("Invalid JPEG scan header.");
                scanned = true;
                // Find structural markers between scans, allowing stuffed FF bytes and restart markers.
                // Pixel/entropy validity remains the native decoder's responsibility.
                while (position < bytes.Length)
                {
                    var offset = bytes[position..].IndexOf((byte)255);
                    if (offset < 0) throw new InvalidDataException("Incomplete JPEG scan.");
                    position += offset;
                    var next = position + 1;
                    while (next < bytes.Length && bytes[next] == 255) next++;
                    if (next >= bytes.Length) throw new InvalidDataException("Incomplete JPEG scan marker.");
                    if (bytes[next] is 0 or >= 208 and <= 215) { position = next + 1; continue; }
                    break;
                }
            }
        }
        if (segments > 10000 || width == 0 || !ended) throw new InvalidDataException("JPEG structure is incomplete.");
        if (iccParts.Count != iccCount) throw new InvalidDataException("Incomplete JPEG ICC profile.");
        if (iccCount != 0) AddUniqueProfile(profiles, "icc", iccParts.Values.SelectMany(part => part).ToArray());
        return new(ImageFormat.Jpeg, width, height, depth, true, other, profiles, resolution);
    }

    private static ImageContainer WebP(ReadOnlySpan<byte> bytes)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length - 8) throw new InvalidDataException("Invalid WebP file size.");
        uint width = 0, height = 0, canvasWidth = 0, canvasHeight = 0;
        var lossy = false;
        var other = false;
        var frames = 0;
        var position = 12;
        var chunks = 0;
        var profiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte flags = 0;
        while (position <= bytes.Length - 8 && ++chunks <= 10000)
        {
            var type = Encoding.ASCII.GetString(bytes.Slice(position, 4));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(position + 4)..]);
            if (length > bytes.Length - position - 8) throw new InvalidDataException("Truncated WebP chunk.");
            var data = bytes.Slice(position + 8, (int)length);
            if (type is "ANIM" or "ANMF") throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (type == "VP8X")
            {
                if (data.Length == 10 && (data[0] & 2) != 0) throw new ImageFailureException(ImageFailure.UnsupportedInput);
                if (chunks != 1 || data.Length != 10 || (data[0] & 0xc3) != 0 || data[1] != 0 || data[2] != 0 || data[3] != 0)
                    throw new InvalidDataException("Invalid or animated WebP header.");
                flags = data[0];
                canvasWidth = 1 + UInt24(data[4..]); canvasHeight = 1 + UInt24(data[7..]);
            }
            else if (type == "VP8 ")
            {
                if (data.Length < 10 || !data.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a }))
                    throw new InvalidDataException("Invalid WebP lossy header.");
                width = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3fff);
                height = (uint)(BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3fff);
                lossy = true; frames++;
            }
            else if (type == "VP8L")
            {
                if (data.Length < 5 || data[0] != 0x2f) throw new InvalidDataException("Invalid WebP lossless header.");
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
                width = (bits & 0x3fff) + 1; height = ((bits >> 14) & 0x3fff) + 1; frames++;
            }
            else if (type is not ("ALPH" or "ICCP" or "EXIF" or "XMP ")) other = true;
            if (type is "ICCP" or "EXIF" or "XMP ")
                AddUniqueProfile(profiles, type switch { "ICCP" => "icc", "EXIF" => "exif", _ => "xmp" },
                    type == "EXIF" && !data.StartsWith("Exif\0\0"u8) ? "Exif\0\0"u8.ToArray().Concat(data.ToArray()).ToArray() : data.ToArray());
            position = checked(position + 8 + (int)length + (int)(length & 1));
            if (position > bytes.Length || ((length & 1) != 0 && bytes[position - 1] != 0))
                throw new InvalidDataException("Invalid WebP padding.");
        }
        if (position != bytes.Length || chunks > 10000 || frames != 1 ||
            (canvasWidth != 0 && (canvasWidth != width || canvasHeight != height)))
            throw new InvalidDataException("Unsupported or inconsistent WebP structure.");
        if (((flags & 32) != 0) != profiles.ContainsKey("icc") || ((flags & 8) != 0) != profiles.ContainsKey("exif") ||
            ((flags & 4) != 0) != profiles.ContainsKey("xmp")) throw new InvalidDataException("WebP metadata flags disagree with its chunks.");
        return new(ImageFormat.WebP, width, height, 8, lossy, other, profiles);
    }

    private static void AddUniqueProfile(Dictionary<string, byte[]> profiles, string name, byte[] data)
    {
        if (data.Length is 0 or > 4 * 1024 * 1024 || !profiles.TryAdd(name, data))
            throw new InvalidDataException("Duplicate, empty or oversized image metadata profile.");
    }

    private static uint UInt24(ReadOnlySpan<byte> data) => (uint)(data[0] | data[1] << 8 | data[2] << 16);
}
