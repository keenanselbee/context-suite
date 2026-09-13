using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace ContextSuite.Core.Audio;

public sealed record AudioComment(string Name, string Value);
public sealed record VorbisCommentList(string Vendor, ImmutableArray<AudioComment> Comments);

// FLAC, Vorbis and Opus share this length-prefixed UTF-8 list. The container
// owns its framing byte, padding or trailing-data policy.
public static class VorbisComments
{
    public const int MaximumComments = 4096;
    public const int MaximumTextBytes = 256 * 1024;
    public const int MaximumPictureTextBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static VorbisCommentList Read(ReadOnlySpan<byte> data, ref int textBytes, out int consumed, bool allowPictures = false)
    {
        var offset = 0;
        try
        {
            var vendor = Text(data, ref offset, ref textBytes);
            var count = Number(data, ref offset);
            if (count > MaximumComments) throw new InvalidDataException("Audio comments exceed their count budget.");
            var comments = ImmutableArray.CreateBuilder<AudioComment>();
            var pictureBytes = 0;
            for (var i = 0; i < count; i++)
            {
                string field;
                if (allowPictures)
                {
                    var length = Number(data, ref offset);
                    if (length > data.Length - offset) throw new InvalidDataException("Truncated audio comment field.");
                    var bytes = data.Slice(offset, (int)length); offset += (int)length;
                    var picture = bytes.Length >= 23 && Encoding.ASCII.GetString(bytes[..23]).Equals("METADATA_BLOCK_PICTURE=", StringComparison.OrdinalIgnoreCase);
                    if (picture)
                    {
                        if (length > MaximumPictureTextBytes - pictureBytes) throw new InvalidDataException("Artwork comments exceed their byte budget.");
                        pictureBytes += (int)length;
                    }
                    else
                    {
                        if (textBytes < 0 || length > MaximumTextBytes - textBytes) throw new InvalidDataException("Audio comments exceed their text budget.");
                        textBytes += (int)length;
                    }
                    field = Utf8.GetString(bytes);
                }
                else field = Text(data, ref offset, ref textBytes);
                var split = field.IndexOf('=');
                if (split <= 0 || field.AsSpan(0, split).ContainsAnyExceptInRange(' ', '}'))
                    throw new InvalidDataException("Audio comment names require ASCII 0x20 through 0x7D and a value separator.");
                comments.Add(new(field[..split], field[(split + 1)..]));
            }
            consumed = offset;
            return new(vendor, comments.ToImmutable());
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Audio comments contain invalid UTF-8.", ex); }
    }

    private static uint Number(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset > data.Length - 4) throw new InvalidDataException("Truncated audio comment length.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]); offset += 4; return value;
    }
    private static string Text(ReadOnlySpan<byte> data, ref int offset, ref int textBytes)
    {
        var length = Number(data, ref offset);
        if (textBytes < 0 || length > data.Length - offset || length > MaximumTextBytes - textBytes)
            throw new InvalidDataException("Audio comment text is truncated or exceeds its byte budget.");
        var value = Utf8.GetString(data.Slice(offset, (int)length)); offset += (int)length; textBytes += (int)length; return value;
    }
}
