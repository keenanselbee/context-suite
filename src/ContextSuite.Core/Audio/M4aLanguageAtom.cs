using System.Buffers.Binary;
using System.Text;

namespace ContextSuite.Core.Audio;

// Preserve the complete language identifier in a scoped iTunes freeform atom.
// The mdhd ISO-639 field cannot represent regional identifiers such as en-us.
public static class M4aLanguageAtom
{
    public static byte[] Create(string language)
    {
        Require(language);
        byte[] Atom(string type, byte[] payload)
        {
            var bytes = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4); payload.CopyTo(bytes, 8); return bytes;
        }
        var mean = Atom("mean", new byte[4].Concat("com.apple.iTunes"u8.ToArray()).ToArray());
        var name = Atom("name", new byte[4].Concat("language"u8.ToArray()).ToArray());
        var data = new byte[8 + language.Length]; data[3] = 1;
        Encoding.ASCII.GetBytes(language).CopyTo(data, 8);
        return Atom("----", mean.Concat(name).Concat(Atom("data", data)).ToArray());
    }

    internal static string Read(ReadOnlyMemory<byte> payload, M4aBoxes boxes)
    {
        var children = boxes.Read(payload, "mean", "name", "data");
        var mean = M4aBoxes.One(children, "mean").Span;
        var name = M4aBoxes.One(children, "name").Span;
        var data = M4aBoxes.One(children, "data").Span;
        if (mean.Length < 4 || name.Length < 4 || data.Length < 8 ||
            M4aBoxes.Number(mean, 0) != 0 || M4aBoxes.Number(name, 0) != 0 ||
            !mean[4..].SequenceEqual("com.apple.iTunes"u8) || !name[4..].SequenceEqual("language"u8) ||
            M4aBoxes.Number(data, 0) != 1 || M4aBoxes.Number(data, 4) != 0 || data[8..].IndexOfAnyInRange((byte)128, byte.MaxValue) >= 0)
            throw new NotSupportedException("This M4A freeform metadata needs a preservation handler.");
        var language = Encoding.ASCII.GetString(data[8..]); Require(language); return language;
    }

    private static void Require(string language)
    {
        if (string.IsNullOrEmpty(language) || language.Length > 63 ||
            language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            language.StartsWith('-') || language.EndsWith('-') || language.Contains("--", StringComparison.Ordinal))
            throw new NotSupportedException("This language identifier needs an M4A preservation policy.");
    }
}
