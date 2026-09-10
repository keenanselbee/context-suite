using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record FlacComment(string Name, string Value);
public sealed record FlacPicture(uint Type, string MediaType, string Description, uint Width, uint Height,
    uint BitsPerPixel, uint Colors, int DataBytes, string Sha256, bool IsLinked);
public sealed record FlacDescriptions(string? Vendor, ImmutableArray<FlacComment> Comments, ImmutableArray<FlacPicture> Pictures);

// Bounded descriptive inventory, preserving duplicate comments. Picture dimensions
// are declarations only; no embedded image or linked resource is opened here.
public static class FlacDescriptiveMetadata
{
    public const int MaximumComments = 4096;
    public const int MaximumTextBytes = 256 * 1024;
    public const int MaximumPictures = 31;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static FlacDescriptions Read(FlacMetadataHeader header)
    {
        string? vendor = null;
        var comments = ImmutableArray.CreateBuilder<FlacComment>();
        var pictures = ImmutableArray.CreateBuilder<FlacPicture>();
        var icons = new HashSet<uint>();
        var textBytes = 0;
        try
        {
            foreach (var block in header.Blocks)
            {
                var data = block.Data.AsSpan();
                var offset = 0;
                if (block.Type == 4)
                {
                    vendor = Text(data, ref offset, true, ref textBytes);
                    var count = Number(data, ref offset, true);
                    if (count > MaximumComments) throw new InvalidDataException("FLAC comments exceed their count budget.");
                    for (var index = 0; index < count; index++)
                    {
                        var field = Text(data, ref offset, true, ref textBytes);
                        var split = field.IndexOf('=');
                        if (split <= 0 || field.AsSpan(0, split).ContainsAnyExceptInRange(' ', '~'))
                            throw new InvalidDataException("FLAC comment names require printable ASCII and a value separator.");
                        comments.Add(new(field[..split], field[(split + 1)..]));
                    }
                    RequireEnd(data, offset);
                }
                else if (block.Type == 6)
                {
                    if (pictures.Count >= MaximumPictures) throw new InvalidDataException("FLAC pictures exceed their count budget.");
                    var type = Number(data, ref offset, false);
                    if (type > 20 || (type is 1 or 2 && !icons.Add(type))) throw new InvalidDataException("Invalid or duplicate FLAC picture icon type.");
                    var media = Text(data, ref offset, false, ref textBytes);
                    if (media.Length == 0 || media.AsSpan().ContainsAnyExceptInRange(' ', '~'))
                        throw new InvalidDataException("FLAC picture media types require printable ASCII.");
                    var description = Text(data, ref offset, false, ref textBytes);
                    var width = Number(data, ref offset, false);
                    var height = Number(data, ref offset, false);
                    var bits = Number(data, ref offset, false);
                    var colors = Number(data, ref offset, false);
                    var length = Number(data, ref offset, false);
                    if (length == 0 || length != data.Length - offset) throw new InvalidDataException("FLAC picture data is empty, truncated or has trailing bytes.");
                    pictures.Add(new(type, media, description, width, height, bits, colors, (int)length,
                        Convert.ToHexString(SHA256.HashData(data[offset..])), media == "-->"));
                }
            }
        }
        catch (DecoderFallbackException error) { throw new InvalidDataException("FLAC descriptive text is not valid UTF-8.", error); }
        return new(vendor, comments.ToImmutable(), pictures.ToImmutable());
    }

    private static uint Number(ReadOnlySpan<byte> data, ref int offset, bool littleEndian)
    {
        if (offset > data.Length - 4) throw new InvalidDataException("Truncated FLAC descriptive field.");
        var value = littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static string Text(ReadOnlySpan<byte> data, ref int offset, bool littleEndian, ref int textBytes)
    {
        var length = Number(data, ref offset, littleEndian);
        if (length > data.Length - offset || length > MaximumTextBytes - textBytes) throw new InvalidDataException("FLAC text is truncated or exceeds its byte budget.");
        var text = Utf8.GetString(data.Slice(offset, (int)length));
        offset += (int)length; textBytes += (int)length;
        return text;
    }

    private static void RequireEnd(ReadOnlySpan<byte> data, int offset)
    {
        if (offset != data.Length) throw new InvalidDataException("FLAC comments contain trailing bytes.");
    }
}
