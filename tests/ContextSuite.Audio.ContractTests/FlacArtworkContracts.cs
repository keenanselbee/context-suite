using ContextSuite.Private.IO;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FlacArtworkContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports,
        string? independentDecoder = null)
    {
        var original = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var sourceHeader = FlacMetadata.Parse(original);
        byte[] frontPixels = [255, 0, 0, 255, 0, 255, 0, 128];
        byte[] backPixels = [0, 0, 255, 255, 255, 255, 0, 255];
        var front = Picture(3, "image/png", "Front ü", Png(frontPixels));
        var back = Picture(4, "image/png", "Back ü", Png(backPixels));
        var comments = Comments();
        var rich = Assemble((4, comments), (6, front), (6, back), (1, new byte[262144]));
        var before = FlacDescriptiveMetadata.Read(FlacMetadata.Parse(rich));
        var digest = SHA256.HashData(rich);
        var scratch = Path.Combine(evidence, "artwork-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        var optimized = await adapter.OptimizeFlacAsync(rich, default);
        var afterHeader = FlacMetadata.Parse(optimized.Bytes);
        FlacMetadata.RequirePreservedMetadata(FlacMetadata.Parse(rich), afterHeader);
        var after = FlacDescriptiveMetadata.Read(afterHeader);
        if (!optimized.ExactSamples || optimized.Bytes.Length >= rich.Length || optimized.Facts.Streams.Count(stream => stream.AttachedPicture) != 2)
            throw new Exception("FLAC artwork optimization lost audio/pictures or failed to shrink.");
        checks.Add("FLAC artwork: smaller optimization retains exact samples and two attached images");
        if (before.Vendor != after.Vendor || !before.Comments.SequenceEqual(after.Comments) || !before.Pictures.SequenceEqual(after.Pictures))
            throw new Exception("FLAC vendor, duplicate comments, picture bytes or declarations changed.");
        checks.Add("FLAC artwork: vendor, duplicate artist values, Unicode and picture descriptions retain their original bytes");
        var inputPath = Path.Combine(evidence, "authored-artwork.flac");
        var outputPath = Path.Combine(evidence, "optimized-artwork.flac");
        await File.WriteAllBytesAsync(inputPath, rich);
        await File.WriteAllBytesAsync(outputPath, optimized.Bytes);
        foreach (var (index, expected) in new[] { (0, frontPixels), (1, backPixels) })
        {
            foreach (var path in new[] { inputPath, outputPath })
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var pixels = await SeekableMediaProcess.RunAsync(Path.Combine(independentDecoder ?? binary, "ffmpeg.exe"), evidence, input,
                    ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "flac", "-i", "fd:",
                        "-map", "0:v:" + index, "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], 4096, default);
                if (!pixels.SequenceEqual(expected)) throw new Exception("Embedded cover pixels or alpha changed.");
            }
            checks.Add("FLAC artwork: cover " + index + " decodes to authored RGBA before and after optimization");
        }
        try { await adapter.ConvertAsync(rich, AudioFormat.Wave, AudioConversionConsent.None, default); throw new Exception("Repeated comments or gain information was omitted during conversion."); }
        catch (NotSupportedException) { checks.Add("FLAC artwork: repeated comments and playback gain still require a preservation plan"); }
        var linked = Assemble((4, comments), (6, Picture(3, "-->", "Linked", "https://example.invalid/cover.png"u8.ToArray())));
        try { await adapter.OptimizeFlacAsync(linked, default); throw new Exception("Linked picture was rewritten without a location policy."); }
        catch (NotSupportedException) { checks.Add("FLAC artwork: linked resource refused before native probing"); }
        var malformed = comments.ToArray(); malformed[^1] = 0xff;
        try { await adapter.OptimizeFlacAsync(Assemble((4, malformed)), default); throw new Exception("Malformed UTF-8 metadata was admitted."); }
        catch (InvalidDataException) { checks.Add("FLAC artwork: malformed descriptive metadata refused"); }
        if (!SHA256.HashData(rich).SequenceEqual(digest) || !SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).SequenceEqual(digest) ||
            Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Artwork operation changed original bytes or left scratch.");
        checks.Add("FLAC artwork: original digest and scratch cleanup survive successful and refused work");
        reports.Add(new { operation = "flac-artwork", sourceBytes = rich.Length, outputBytes = optimized.Bytes.Length,
            optimized.DecodedFrames, optimized.ExactSamples, comments = after.Comments, pictures = after.Pictures,
            decodedRgbaMatches = true, sourceSha256 = Convert.ToHexString(digest) });

        byte[] Assemble(params (byte Type, byte[] Data)[] additional)
        {
            using var file = new MemoryStream(); file.Write("fLaC"u8);
            var blocks = new[] { ((byte)0, sourceHeader.Blocks[0].Data.ToArray()) }.Concat(additional).ToArray();
            for (var index = 0; index < blocks.Length; index++)
            {
                var (type, data) = blocks[index];
                file.WriteByte((byte)(type | (index == blocks.Length - 1 ? 128 : 0)));
                file.WriteByte((byte)(data.Length >> 16)); file.WriteByte((byte)(data.Length >> 8)); file.WriteByte((byte)data.Length); file.Write(data);
            }
            file.Write(original.AsSpan(sourceHeader.AudioOffset)); return file.ToArray();
        }
    }

    private static byte[] Comments()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var fields = new[] { "TITLE=Authored album ü", "ARTIST=First artist", "artist=Second artist", "LYRICS=Line one\nLine two", "REPLAYGAIN_TRACK_GAIN=-3.25 dB" };
        var vendor = Encoding.UTF8.GetBytes("Context Suite authored metadata"); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(fields.Length);
        foreach (var field in fields) { var value = Encoding.UTF8.GetBytes(field); writer.Write(value.Length); writer.Write(value); }
        return stream.ToArray();
    }

    private static byte[] Picture(uint type, string media, string description, byte[] picture)
    {
        using var stream = new MemoryStream();
        void Number(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); stream.Write(bytes); }
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); Number((uint)bytes.Length); stream.Write(bytes); }
        Number(type); Text(media); Text(description); Number(2); Number(1); Number(32); Number(0); Number((uint)picture.Length); stream.Write(picture);
        return stream.ToArray();
    }

    private static byte[] Png(byte[] pixels)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 2); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) { zlib.WriteByte(0); zlib.Write(pixels); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return stream.ToArray();
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); stream.Write(number);
            var type = Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(data);
            uint crc = uint.MaxValue;
            foreach (var value in type.Concat(data)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
        }
    }
}
