namespace ContextSuite.Core.Audio;

public static partial class AsfMetadata
{
    private sealed partial class Inventory
    {
        private ushort? _bitrateStream, _extendedStream, _languageIndex;
        private readonly List<string> _languages = [];

        private void ReadStreamBitrates(ReadOnlySpan<byte> body)
        {
            if (body.Length != 8 || U16(body, 0) != 1 || U16(body, 2) is 0 or > 127 || U32(body, 4) == 0)
                throw new NotSupportedException("ASF bitrate properties must describe one ordinary audio stream.");
            _bitrateStream = U16(body, 2);
        }

        private void ReadLanguages(ReadOnlySpan<byte> body)
        {
            if (body.Length < 2 || U16(body, 0) is 0 or > 128)
                throw new InvalidDataException("ASF language list count is invalid.");
            var position = 2;
            for (var index = 0; index < U16(body, 0); index++)
            {
                if (position >= body.Length) throw new InvalidDataException("ASF language list is truncated.");
                var bytes = body[position++];
                if (bytes > body.Length - position) throw new InvalidDataException("ASF language exceeds its object.");
                var language = Text(body.Slice(position, bytes)); position += bytes;
                if (language.Length is 0 or > 63 || language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                    language.StartsWith('-') || language.EndsWith('-') || language.Contains("--", StringComparison.Ordinal))
                    throw new NotSupportedException("ASF language identifier needs a preservation policy.");
                _languages.Add(language);
            }
            if (position != body.Length) throw new InvalidDataException("ASF language list has trailing bytes.");
        }

        private void ReadExtendedStream(ReadOnlySpan<byte> body)
        {
            // The fixed single-stream form has no localized names, payload
            // extension systems or embedded alternate stream. Do not skip them.
            if (body.Length != 64 || U16(body, 60) != 0 || U16(body, 62) != 0)
                throw new NotSupportedException("ASF stream names, payload extensions or alternate streams need a preservation handler.");
            if (U16(body, 48) is 0 or > 127 || (U32(body, 44) & ~15u) != 0 ||
                U32(body, 16) == 0 || U32(body, 24) > U32(body, 20) || U32(body, 36) > U32(body, 32) ||
                U64(body, 8) != 0 && U64(body, 0) > U64(body, 8))
                throw new InvalidDataException("ASF extended stream identity, bitrate, timing or flags are invalid.");
            _extendedStream = U16(body, 48); _languageIndex = U16(body, 50);
        }

        private void RequireExtendedStream()
        {
            if (_bitrateStream is { } bitrate && bitrate != StreamNumber ||
                _extendedStream is { } stream && stream != StreamNumber)
                throw new InvalidDataException("ASF extended properties refer to a different stream.");
            if (_languageIndex is { } language)
            {
                if (_languages.Count == 0)
                {
                    if (language != 0) throw new InvalidDataException("ASF stream language has no language list.");
                }
                else
                {
                    if (language >= _languages.Count) throw new InvalidDataException("ASF stream language index is outside its list.");
                    var declared = _languages[language];
                    var existing = Tags.Where(tag => tag.Name == "language").ToArray();
                    if (existing.Any(tag => tag.Value != declared)) throw new NotSupportedException("ASF language declarations disagree.");
                    if (existing.Length == 0) Tags.Add(new("language", declared));
                }
            }
            else if (_languages.Count > 0) throw new NotSupportedException("ASF language list has no verified stream association.");
        }
    }
}
