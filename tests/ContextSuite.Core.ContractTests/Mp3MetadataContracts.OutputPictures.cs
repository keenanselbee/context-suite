using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class Mp3MetadataContracts
{
    private static async Task OutputPicturesAsync(Action<bool, string> check)
    {
        var png = new byte[40]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 32); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 32);
        png[24] = 8; png[25] = 6; png[33] = 255; png[34] = 224;
        var jpeg = new byte[] { 255, 216, 255, 192, 0, 17, 8, 0, 32, 0, 32, 3, 1, 17, 0, 2, 17, 1, 3, 17, 1, 255, 217 };
        var title = Frame(4, "TIT2", Text(3, "Kept title")); var tag = Tag(4, title, new byte[19]);
        var front = Block("image/png", 3, "Front \u00fc", png, 32, 32, 32);
        var back = Block("image/jpeg", 4, "Back", jpeg, 32, 32, 24);
        var projected = Mp3PictureFrames.ForOutput([front, back]);
        var output = Mp3PictureFrames.AppendToTag(tag, [front, back]);
        using (var file = new MemoryStream(FileOf(output)))
        {
            var inventory = await Mp3Metadata.ReadAsync(file, default, true);
            check(inventory.Tags["title"] == "Kept title" && inventory.AudioFrames == 2 && inventory.Pictures.Length == 2 &&
                inventory.Pictures.Where((picture, index) => !picture.Data.AsSpan().SequenceEqual(projected[index].Data.AsSpan())).Count() == 0,
                "MP3 output: independently read exact APIC content, tags, order and audio framing");
        }
        check(output.AsSpan(10, title.Length).SequenceEqual(title) && output.AsSpan()[^19..].IndexOfAnyExcept((byte)0) < 0,
            "MP3 output: existing text frame and padding remain byte-identical");
        check(FlacDescriptiveMetadata.ReadBlocks(projected).Pictures.All(picture => picture.Width == 0 && picture.Height == 0 && picture.BitsPerPixel == 0),
            "MP3 output: checked redundant geometry stays in encoded pictures");
        foreach (var (mime, image) in new[] { ("image/png", png), ("image/jpeg", jpeg) })
        {
            var unknown = Block(mime, 0, "", image);
            check(Mp3PictureFrames.ForOutput([unknown])[0].Data.AsSpan().SequenceEqual(unknown.Data.AsSpan()), "MP3 output: unspecified geometry stays unspecified " + mime);
        }
        var many = Enumerable.Range(0, 31).Select(index => Block("image/png", 3, index.ToString(), png)).ToImmutableArray();
        using (var file = new MemoryStream(FileOf(Mp3PictureFrames.AppendToTag(Tag(4), many))))
            check((await Mp3Metadata.ReadAsync(file, default, true)).Pictures.Length == 31, "MP3 output: 31 ordered distinct covers");
        var boundary = Block("image/png", 3, new string('a', 64), png);
        check(Mp3PictureFrames.ForOutput([boundary]).Length == 1, "MP3 output: description length boundary");
        check(Mp3PictureFrames.ForOutput([Block("image/png", 1, "Icon", png)]).Length == 1, "MP3 output: PNG file icon");
        Reject([front, front], "duplicate descriptors"); Reject(many.Add(front), "picture count");
        Reject([Block("image/png", 3, new string('a', 65), png)], "long description");
        Reject([Block("image/png", 3, "A\0B", png)], "NUL description");
        Reject([Block("image/png", 3, "A\u0001B", png)], "control description");
        Reject([Block("-->", 3, "Link", "https://example.invalid/image"u8.ToArray())], "linked artwork");
        Reject([Block("image/gif", 3, "Other", png)], "unhandled format");
        Reject([Block("image/png", 21, "Reserved", png)], "reserved type");
        Reject([Block("image/jpeg", 1, "Icon", jpeg)], "JPEG file icon");
        Reject([Block("image/png", 3, "Width", png, 31)], "conflicting width");
        Reject([Block("image/png", 3, "Height", png, height: 31)], "conflicting height");
        Reject([Block("image/png", 3, "Depth", png, bits: 24)], "conflicting precision");
        Reject([Block("image/png", 3, "Palette", png, colors: 256)], "conflicting palette");
        Reject([Block("image/jpeg", 3, "Depth", jpeg, bits: 32)], "conflicting JPEG precision");
        Reject([Block("image/png", 3, "Truncated", png[..20])], "truncated PNG header");
        Reject([Block("image/jpeg", 3, "Truncated", jpeg[..6])], "truncated JPEG header");
        var huge = new byte[Mp3Metadata.MaximumTagBytes]; png.CopyTo(huge, 0);
        Reject([Block("image/png", 3, "Oversized", huge)], "image byte budget");
        foreach (var badTag in new[] { Tag(3, title), Tag(4, Picture(4, 3, "image/png", 3, "Existing", png)), Tag(4, new byte[] { 0, 1 }),
                     Tag(4, Frame(4, "TIT2", Text(3, "Flag"), 1)), Tag(4, title)[..^1] })
            RejectTag(badTag, "invalid/existing encoder tag");
        var nonSync = Tag(4, title); nonSync[6] = 128; RejectTag(nonSync, "tag size encoding");
        RejectTag(Tag(4, new byte[Mp3Metadata.MaximumTagBytes]), "combined tag budget");
        RejectTag(Tag(4, Enumerable.Repeat(Frame(4, "TSSE", Text(3, "Engine")), Mp3Metadata.MaximumTagFrames).ToArray()), "combined frame count");

        void Reject(ImmutableArray<FlacMetadataBlock> pictures, string name)
        {
            try { Mp3PictureFrames.ForOutput(pictures); check(false, "MP3 output refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "MP3 output refuses " + name); }
        }
        void RejectTag(byte[] bytes, string name)
        {
            try { Mp3PictureFrames.AppendToTag(bytes, [front]); check(false, "MP3 output refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "MP3 output refuses " + name); }
        }
        static FlacMetadataBlock Block(string mime, uint type, string description, byte[] image, uint width = 0, uint height = 0, uint bits = 0, uint colors = 0)
        {
            using var bytes = new MemoryStream();
            void N(uint value) { Span<byte> field = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(field, value); bytes.Write(field); }
            void T(string value) { var text = Encoding.UTF8.GetBytes(value); N((uint)text.Length); bytes.Write(text); }
            N(type); T(mime); T(description); N(width); N(height); N(bits); N(colors); N((uint)image.Length); bytes.Write(image);
            return new(6, bytes.ToArray().ToImmutableArray());
        }
    }
}
