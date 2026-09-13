using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

internal static partial class Mp3MetadataContracts
{
    private static async Task PicturesAsync(Action<bool, string> check)
    {
        // Structural signatures only: native contracts separately decode real covers.
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 255, 0, 255, 224 };
        var jpeg = new byte[] { 255, 216, 255, 224, 255, 0, 255, 217 };
        foreach (var (version, encoding) in new[] { (2, 0), (2, 1), (3, 0), (3, 1), (4, 0), (4, 1), (4, 2), (4, 3) })
        foreach (var empty in new[] { false, true })
        {
            var description = empty ? "" : " Cover \u00fc ";
            var tag = Tag(version, Picture(version, encoding, "image/png", 3, description, png),
                Picture(version, encoding, "image/jpeg", 4, "Back", jpeg));
            using var stream = new MemoryStream(FileOf(tag)); stream.Position = 5;
            var result = await Mp3Metadata.ReadAsync(stream, default, true);
            var pictures = FlacDescriptiveMetadata.ReadBlocks(result.Pictures).Pictures;
            check(result.Tags.IsEmpty && result.AudioFrames == 2 && stream.Position == 5 && pictures.Length == 2 &&
                pictures[0].Description == description && pictures[1].Description == "Back" &&
                pictures[0].Type == 3 && pictures[1].Type == 4 && pictures[0].MediaType == "image/png" && pictures[1].MediaType == "image/jpeg" &&
                pictures.All(p => p.Width == 0 && p.Height == 0 && p.BitsPerPixel == 0 && p.Colors == 0) &&
                pictures[0].Sha256 == Convert.ToHexString(SHA256.HashData(png)) && pictures[1].Sha256 == Convert.ToHexString(SHA256.HashData(jpeg)),
                "MP3 pictures: exact image bytes, order and fields " + version + "/" + encoding + "/" + empty);
            try { await Mp3Metadata.ReadAsync(stream, default); check(false, "MP3 pictures require opt-in"); }
            catch (NotSupportedException) { check(stream.Position == 5, "MP3 pictures: default handler refusal restores position " + version + "/" + encoding + "/" + empty); }
        }
        foreach (var version in new[] { 2, 3, 4 })
        {
            var payload = Picture(version, 0, "image/jpeg", 3, "Escaped", jpeg);
            if (version <= 3) payload = Escape(payload);
            else
            {
                var data = payload[10..];
                payload = Frame(4, "APIC", Escape(Size(data.Length).Concat(data).ToArray()), 3);
            }
            var tag = Tag(version, payload); if (version <= 3) tag[5] = 128;
            using var stream = new MemoryStream(FileOf(tag));
            var inventory = await Mp3Metadata.ReadAsync(stream, default, true);
            check(inventory.Pictures[0].Data.AsSpan()[^jpeg.Length..].SequenceEqual(jpeg), "MP3 pictures: binary unsynchronisation and v4 DLI " + version);
        }
        var valid = Picture(4, 3, "image/png", 3, "Cover", png);
        var many = Enumerable.Range(0, 31).Select(i => Picture(4, 3, "image/png", 3, i.ToString(), png)).ToArray();
        using (var stream = new MemoryStream(FileOf(Tag(4, many))))
            check((await Mp3Metadata.ReadAsync(stream, default, true)).Pictures.Length == 31, "MP3 pictures: reviewed count boundary");
        await Reject(Tag(4, many.Append(valid).ToArray()), "count limit");
        await Reject(Tag(4, valid, valid), "duplicate descriptions");
        await Reject(Tag(4, Picture(4, 3, "image/png", 2, "One", png), Picture(4, 3, "image/png", 2, "Two", png)), "duplicate icons");
        foreach (var mime in new[] { "-->", "image/gif", "png", "", new string('x', 128) })
            await Reject(Tag(4, Picture(4, 3, mime, 3, "Cover", png)), "unreviewed MIME " + mime.Length);
        foreach (var type in new byte[] { 21, 255 }) await Reject(Tag(4, Picture(4, 3, "image/png", type, "Cover", png)), "reserved type " + type);
        await Reject(Tag(4, Picture(4, 3, "image/jpeg", 3, "Cover", png)), "signature mismatch");
        await Reject(Tag(4, Picture(4, 3, "image/png", 3, "Cover", [])), "empty image");
        await Reject(Tag(4, Picture(4, 3, "image/png", 3, new string('x', 65), png)), "description bound");
        await Reject(Tag(4, Picture(4, 3, "image/png", 1, "Icon", png)), "invalid icon size");
        var icon = new byte[24]; png.AsSpan(0, 8).CopyTo(icon); "IHDR"u8.CopyTo(icon.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(icon.AsSpan(16), 32); BinaryPrimitives.WriteInt32BigEndian(icon.AsSpan(20), 32);
        using (var stream = new MemoryStream(FileOf(Tag(4, Picture(4, 3, "image/png", 1, "Icon", icon)))))
            check((await Mp3Metadata.ReadAsync(stream, default, true)).Pictures.Length == 1, "MP3 pictures: declared PNG icon extent");
        foreach (var data in new byte[][] { [3], [3, 105], [3, 105, 0], [3, .."image/png\0"u8, 3, 65],
            [1, .."image/png\0"u8, 3, 65, 0, 0, 0, ..png], [3, .."image/png\0"u8, 3, 255, 0, ..png] })
            await Reject(Tag(4, Frame(4, "APIC", data)), "truncated header/description or invalid text");
        await Reject(Tag(4, Picture(4, 3, "image/png", 3, "Cover", new byte[Mp3Metadata.MaximumTagBytes])), "tag budget");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var cancelledStream = new MemoryStream(FileOf(Tag(4, valid))); cancelledStream.Position = 7;
        try { await Mp3Metadata.ReadAsync(cancelledStream, cancelled.Token, true); check(false, "MP3 pictures cancellation"); }
        catch (OperationCanceledException) { check(cancelledStream.Position == 7, "MP3 pictures: cancellation restores position"); }

        async Task Reject(byte[] tag, string name)
        {
            using var stream = new MemoryStream(FileOf(tag)); stream.Position = 3;
            try { await Mp3Metadata.ReadAsync(stream, default, true); check(false, "MP3 pictures reject " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            { check(stream.Position == 3, "MP3 pictures reject " + name + " and restore position"); }
        }
    }

    private static byte[] Picture(int version, int encoding, string mime, byte type, string description, byte[] image)
    {
        var format = Encoding.ASCII.GetBytes(version == 2 ? mime == "image/png" ? "PNG" : "JPG" : mime + "\0");
        var text = Text(encoding, description + "\0");
        return Frame(version, version == 2 ? "PIC" : "APIC", new[] { (byte)encoding }.Concat(format).Append(type).Concat(text[1..]).Concat(image).ToArray());
    }
}
