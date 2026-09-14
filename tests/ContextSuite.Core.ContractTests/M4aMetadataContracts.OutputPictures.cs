using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class M4aMetadataContracts
{
    private static async Task OutputPicturesAsync(Action<bool, string> check)
    {
        // Deliberate header fixtures; independent image decoding is in the
        // private generated-fixture suite, not claimed by these parser checks.
        var png = new byte[40]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(8), 13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 32); BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 32);
        png[24] = 8; png[25] = 6;
        byte[] jpeg = [255, 216, 255, 192, 0, 17, 8, 0, 32, 0, 32, 3, 1, 17, 0, 2, 17, 1, 3, 17, 1, 255, 217];
        var first = Block("image/png", png, width: 32, height: 32, bits: 32);
        var second = Block("image/jpeg", jpeg, width: 32, height: 32, bits: 24);
        var projected = M4aPictureAtoms.ForOutput([first, second, first]);
        using (var file = new MemoryStream(FileOf(tags: [Text("\u00a9nam", "Retained"), M4aPictureAtoms.CreateCover([first, second, first])])))
        {
            var output = await M4aMetadata.ReadAsync(file, default, true);
            check(output.Pictures.Length == 3 && output.Tags["title"] == "Retained" && output.Pictures.Zip(projected).All(pair =>
                pair.First.Data.AsSpan().SequenceEqual(pair.Second.Data.AsSpan())), "M4A output: ordered duplicate covers, image bytes and tags survive round trip");
            check(FlacDescriptiveMetadata.ReadBlocks(output.Pictures).Pictures.All(picture => picture.Type == 0 && picture.Description == "" &&
                picture.Width == 0 && picture.Height == 0 && picture.BitsPerPixel == 0 && picture.Colors == 0), "M4A output: consistent redundant geometry is normalized without invented labels");
        }
        foreach (var (mime, image) in new[] { ("image/png", png), ("image/jpeg", jpeg) })
        {
            var block = Block(mime, image);
            check(M4aPictureAtoms.ForOutput([block])[0].Data.AsSpan().SequenceEqual(block.Data.AsSpan()), "M4A output: unspecified geometry unchanged " + mime);
        }
        var many = Enumerable.Repeat(first, 31).ToImmutableArray();
        using (var file = new MemoryStream(FileOf(tags: [M4aPictureAtoms.CreateCover(many)])))
            check((await M4aMetadata.ReadAsync(file, default, true)).Pictures.Length == 31, "M4A output: 31 pictures may share empty descriptions");
        check(M4aPictureAtoms.CreateCover([]).Length == 0, "M4A output: no empty cover atom");
        Reject(default, "uninitialized inventory"); Reject([new(6, default)], "uninitialized picture");
        Reject([new(4, first.Data)], "non-picture block"); Reject(many.Add(first), "picture count");
        foreach (var type in new uint[] { 1, 2, 3, 4, 20, 21 }) Reject([Block("image/png", png, type)], "picture designation " + type);
        foreach (var description in new[] { "Front", "\u00fc", " ", "\0" }) Reject([Block("image/png", png, description: description)], "picture description");
        Reject([Block("-->", "https://example.invalid/cover"u8.ToArray())], "linked picture");
        Reject([Block("image/gif", png)], "unsupported image type");
        Reject([Block("image/png", jpeg)], "conflicting PNG type"); Reject([Block("image/jpeg", png)], "conflicting JPEG type");
        Reject([Block("image/png", png, width: 31)], "conflicting width");
        Reject([Block("image/png", png, height: 31)], "conflicting height");
        Reject([Block("image/png", png, bits: 24)], "conflicting depth");
        Reject([Block("image/png", png, colors: 256)], "conflicting palette");
        Reject([Block("image/jpeg", jpeg, bits: 32)], "conflicting JPEG depth");
        Reject([Block("image/png", png[..20])], "truncated PNG"); Reject([Block("image/jpeg", jpeg[..6])], "truncated JPEG");
        var huge = new byte[VorbisComments.MaximumPictureTextBytes]; png.CopyTo(huge, 0);
        Reject([Block("image/png", huge)], "aggregate image byte budget");
        var transport = new byte[VorbisComments.MaximumPictureTextBytes / 2]; png.CopyTo(transport, 0);
        Reject([Block("image/png", transport), Block("image/png", transport[..(transport.Length / 2)])], "base64 transport budget");

        void Reject(ImmutableArray<FlacMetadataBlock> pictures, string name)
        {
            try { M4aPictureAtoms.ForOutput(pictures); check(false, "M4A output refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "M4A output refuses " + name); }
        }
        static FlacMetadataBlock Block(string mime, byte[] image, uint type = 0, string description = "", uint width = 0, uint height = 0, uint bits = 0, uint colors = 0)
        {
            using var bytes = new MemoryStream();
            void Number(uint value) { Span<byte> field = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(field, value); bytes.Write(field); }
            void TextValue(string value) { var text = Encoding.UTF8.GetBytes(value); Number((uint)text.Length); bytes.Write(text); }
            Number(type); TextValue(mime); TextValue(description); Number(width); Number(height); Number(bits); Number(colors); Number((uint)image.Length); bytes.Write(image);
            return new(6, bytes.ToArray().ToImmutableArray());
        }
    }
}
