using System.Text;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Keep the existing INFO convention where it is unambiguous. Other descriptive
// values use explicitly encoded ID3v2.4 text, never an inferred RIFF code page.
internal static class WaveTextFrames
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool UsesInfo(string key, string value) => key.ToLowerInvariant() is
        "title" or "artist" or "album" or "comment" or "date" or "genre" or "language" or "track" or "copyright" &&
        value.All(character => character is >= ' ' and <= '~' or '\t' or '\r' or '\n');

    internal static ImmutableDictionary<string, string> ForOutput(IReadOnlyDictionary<string, string> tags)
    {
        var result = tags.Where(tag => !UsesInfo(tag.Key, tag.Value)).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        if (!result.IsEmpty) Create(result);
        return result;
    }

    internal static byte[] Create(IReadOnlyDictionary<string, string> tags)
    {
        var checkedTags = AudioCommentConversion.Read(tags.Select(tag => new AudioComment(tag.Key, tag.Value)), false);
        using var output = new MemoryStream(); output.Write([73, 68, 51, 4, 0, 0, 0, 0, 0, 0]);
        var textBytes = 0;
        foreach (var tag in checkedTags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
        {
            if (tag.Key.Length == 0 || tag.Key.Any(character => character is < ' ' or > '}') ||
                tag.Value.Any(character => character < 32 && character is not ('\t' or '\r' or '\n')))
                throw new NotSupportedException("These WAV tag names or control characters need a preservation policy.");
            var frame = tag.Key switch
            {
                "title" => "TIT2", "artist" => "TPE1", "album_artist" => "TPE2", "album" => "TALB",
                "track" when IsPartNumber(tag.Value) => "TRCK", "disc" when IsPartNumber(tag.Value) => "TPOS", "composer" => "TCOM",
                "copyright" when tag.Value.Length >= 5 && tag.Value[4] == ' ' && tag.Value.Take(4).All(char.IsAsciiDigit) => "TCOP",
                "language" when tag.Value.Length == 3 && tag.Value.All(character => character is >= 'a' and <= 'z') => "TLAN",
                "encoded_by" => "TENC", "publisher" => "TPUB", "performer" => "TPE3",
                "disc_subtitle" => "TSST", "grouping" => "TIT1", "comment" => "COMM", _ => "TXXX"
            };
            // Custom fields retain literal values when the corresponding ID3
            // field has incompatible syntax or would reinterpret the value.
            var text = Utf8.GetBytes(frame == "TXXX" ? tag.Key + '\0' + tag.Value : frame == "COMM" ? "\0" + tag.Value : tag.Value);
            textBytes = checked(textBytes + text.Length);
            if (textBytes > VorbisComments.MaximumTextBytes) throw new NotSupportedException("WAV tags exceed the text budget.");
            var length = 1 + text.Length + (frame == "COMM" ? 3 : 0);
            output.Write(Encoding.ASCII.GetBytes(frame)); output.Write(Size(length)); output.Write([0, 0, 3]);
            if (frame == "COMM") output.Write("und"u8);
            output.Write(text);
        }
        var result = output.ToArray(); Size(result.Length - 10).CopyTo(result, 6);
        return result;
    }

    private static bool IsPartNumber(string value)
    {
        var parts = value.Split('/');
        return parts.Length is 1 or 2 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static byte[] Size(int value) => [(byte)(value >> 21 & 127), (byte)(value >> 14 & 127), (byte)(value >> 7 & 127), (byte)(value & 127)];
}
