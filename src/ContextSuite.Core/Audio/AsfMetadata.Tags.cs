using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace ContextSuite.Core.Audio;

public static partial class AsfMetadata
{
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["Title"] = "title", ["Author"] = "artist", ["Copyright"] = "copyright", ["Description"] = "comment",
        ["WM/AlbumTitle"] = "album", ["WM/AlbumArtist"] = "album_artist", ["WM/Composer"] = "composer",
        ["WM/Genre"] = "genre", ["WM/Year"] = "date", ["WM/TrackNumber"] = "track", ["WM/PartOfSet"] = "disc",
        ["WM/Publisher"] = "publisher", ["WM/EncodedBy"] = "encoded_by", ["WM/Language"] = "language",
        ["WM/SetSubTitle"] = "disc_subtitle", ["WM/EncodingSettings"] = "encoder", ["WM/Tool"] = "encoder"
    };

    private sealed partial class Inventory
    {
        private void ReadContent(ReadOnlySpan<byte> body)
        {
            if (body.Length < 10) throw new InvalidDataException("ASF content description is truncated.");
            var fields = new[] { "Title", "Author", "Copyright", "Description", "Rating" };
            var cursor = new Cursor(body[10..]);
            for (var index = 0; index < fields.Length; index++)
            {
                var bytes = cursor.Take(U16(body, index * 2));
                if (!bytes.IsEmpty && !(bytes.Length == 2 && bytes[0] == 0 && bytes[1] == 0))
                    AddValue(fields[index], 0, bytes, false);
            }
            cursor.RequireEnd();
        }

        private void ReadDescriptors(ReadOnlySpan<byte> body, bool library, bool extendedRecord)
        {
            var cursor = new Cursor(body); var count = cursor.Word();
            _descriptors += count;
            if (_descriptors > MaximumObjects) throw new InvalidDataException("ASF metadata count exceeds its aggregate budget.");
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                string name; ushort type; ReadOnlySpan<byte> value;
                if (extendedRecord)
                {
                    var language = cursor.Word(); var stream = cursor.Word(); var nameBytes = cursor.Word();
                    type = cursor.Word(); var valueBytes = cursor.Dword();
                    if (stream > 127 || !library && (language != 0 || valueBytes > 65535))
                        throw new InvalidDataException("ASF metadata stream, reserved field or value size is invalid.");
                    if (language != 0) Unsupported.Add("language-attributed metadata");
                    _metadataStreams.Add(stream);
                    name = Text(cursor.Take(nameBytes)); value = cursor.Take(valueBytes);
                }
                else
                {
                    name = Text(cursor.Take(cursor.Word())); type = cursor.Word(); value = cursor.Take(cursor.Word());
                }
                if (name.Length is 0 or > 128) throw new InvalidDataException("ASF metadata name exceeds its transport limits.");
                AddValue(name, type, value, extendedRecord);
            }
            cursor.RequireEnd();
        }

        private void AddValue(string name, ushort type, ReadOnlySpan<byte> bytes, bool extendedRecord)
        {
            if (name.StartsWith("DRM", StringComparison.OrdinalIgnoreCase) || name.Equals("WM/Protected", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("WMA protection metadata cannot authorize conversion. Originals were kept.");
            if (name == "WM/Picture" && type == 1) { ReadPicture(bytes); return; }
            // Source encoder provenance does not describe the recording. New
            // output owns its bitrate mode and encoder version, as for WM/Tool.
            if (name == "IsVBR")
            {
                if (type != 2 || bytes.Length != (extendedRecord ? 2 : 4) ||
                    (extendedRecord ? U16(bytes, 0) : U32(bytes, 0)) > 1)
                    throw new InvalidDataException("ASF bitrate-mode metadata is malformed.");
                return;
            }
            if (name == "MediaFoundationVersion")
            {
                if (type != 0 || Text(bytes).Length > 128) throw new InvalidDataException("ASF encoder version is malformed.");
                return;
            }
            if (!Names.TryGetValue(name, out var mapped)) { Unsupported.Add("metadata " + name); return; }
            string value;
            if (type == 0) value = Text(bytes);
            else if (name == "WM/TrackNumber" && type == 3 && bytes.Length == 4)
                value = U32(bytes, 0).ToString(CultureInfo.InvariantCulture);
            else
            {
                if (type == 2 && bytes.Length != (extendedRecord ? 2 : 4) || type == 3 && bytes.Length != 4 ||
                    type == 4 && bytes.Length != 8 || type == 5 && bytes.Length != 2 || type == 6 && bytes.Length != 16)
                    throw new InvalidDataException("ASF typed metadata size is invalid.");
                Unsupported.Add("typed metadata " + name); return;
            }
            if (value.Length > 4096 || Tags.Count >= 128) throw new InvalidDataException("ASF text exceeds its conversion transport limits.");
            // An empty absent bibliographic field does not become a new tag.
            if (value.Length != 0) Tags.Add(new(mapped, value));
        }

        private string Text(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return "";
            if (bytes.Length % 2 != 0 || bytes.Length < 2 || bytes[^2] != 0 || bytes[^1] != 0 ||
                bytes.Length > VorbisComments.MaximumTextBytes - _textBytes)
                throw new InvalidDataException("ASF Unicode text is unterminated, odd-sized or over budget.");
            _textBytes += bytes.Length;
            string value;
            try { value = Utf16.GetString(bytes[..^2]); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("ASF Unicode text is malformed.", error); }
            if (value.Any(character => character < 32 && character is not ('\t' or '\r' or '\n')))
                throw new NotSupportedException("ASF text controls need a preservation policy.");
            return value;
        }

        private void ReadPicture(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 9 || Pictures.Count >= FlacDescriptiveMetadata.MaximumPictures ||
                bytes.Length > Mp3Metadata.MaximumTagBytes - _pictureBytes)
                throw new InvalidDataException("ASF picture exceeds its extent, count or byte budget.");
            _pictureBytes += bytes.Length;
            var type = bytes[0]; var size = U32(bytes, 1); var cursor = new Cursor(bytes[5..]);
            var mime = Text(cursor.TerminatedText()); var description = Text(cursor.TerminatedText());
            var image = cursor.Take(size); cursor.RequireEnd();
            if (type > 20 || mime is not ("image/png" or "image/jpeg") || description.Length > 64 || image.IsEmpty)
                throw new NotSupportedException("This WMA picture type, media format or description needs a preservation policy.");
            var mimeBytes = Encoding.ASCII.GetBytes(mime); var textBytes = Encoding.UTF8.GetBytes(description);
            using var output = new MemoryStream();
            var number = new byte[4];
            Write(type); Write((uint)mimeBytes.Length); output.Write(mimeBytes);
            Write((uint)textBytes.Length); output.Write(textBytes);
            output.Write(new byte[16]); Write(size); output.Write(image);
            var picture = new FlacMetadataBlock(6, output.ToArray().ToImmutableArray());
            // Reuse the bounded image-header and picture-transport checks.
            Mp3PictureFrames.ForOutput([picture]); Pictures.Add(picture);

            void Write(uint value)
            { BinaryPrimitives.WriteUInt32BigEndian(number, value); output.Write(number); }
        }
    }

    private ref struct Cursor(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        internal ReadOnlySpan<byte> Take(long count)
        {
            if (count < 0 || count > _remaining.Length) throw new InvalidDataException("ASF metadata field exceeds its object extent.");
            var result = _remaining[..(int)count]; _remaining = _remaining[(int)count..]; return result;
        }
        internal ushort Word() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        internal uint Dword() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal ReadOnlySpan<byte> TerminatedText()
        {
            for (var offset = 0; offset + 1 < _remaining.Length; offset += 2)
                if (_remaining[offset] == 0 && _remaining[offset + 1] == 0) return Take(offset + 2);
            throw new InvalidDataException("ASF picture text is unterminated.");
        }
        internal void RequireEnd()
        { if (!_remaining.IsEmpty) throw new InvalidDataException("ASF metadata has unparsed trailing bytes."); }
    }
}
