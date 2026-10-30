using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class FlacToOggArtworkContracts
{
    internal static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string? independent = null)
    {
        await OggPictureWriterContracts.RunAsync(evidence, checks);
        var sourcePath = Path.Combine(fixtures, "source.flac"); var source = await File.ReadAllBytesAsync(sourcePath);
        var hash = SHA256.HashData(source); var header = FlacMetadata.Parse(source);
        var small = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128 };
        var large = new byte[512 * 512 * 4]; uint seed = 73591;
        for (var i = 0; i < large.Length; i++) { seed = unchecked(seed * 1664525 + 1013904223); large[i] = (byte)(seed >> 24); }
        var scratch = Path.Combine(evidence, "flac-ogg-artwork-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        foreach (var big in new[] { false, true })
        {
            var pixels = big ? large : small; var width = big ? 512 : 2; var height = big ? 512 : 1;
            var pictures = big ? new[] { Picture(3, "image/png", "Large cover \u00fc", width, height, Png(width, height, pixels)) } :
                new[] { Picture(3, "image/png", "Front \u00fc", width, height, Png(width, height, pixels)),
                    Picture(4, "image/png", "Back \u00fc", width, height, Png(width, height, pixels)) };
            var authored = Assemble(pictures); var digest = SHA256.HashData(authored);
            var inputPath = Path.Combine(evidence, big ? "large-cover.flac" : "two-covers.flac"); await File.WriteAllBytesAsync(inputPath, authored);
            var expected = FlacMetadata.Parse(authored).Blocks.Where(block => block.Type == 6).ToImmutableArray();
            var same = await adapter.ConvertAsync(authored, AudioFormat.Flac, AudioConversionConsent.None, default);
            if (!same.Plan.AlreadyTarget || !same.Bytes.AsSpan().SequenceEqual(authored)) throw new InvalidDataException("Artwork no-op changed bytes.");
            checks.Add("FLAC artwork: same-format bytes retained " + big);
            foreach (var target in new[] { AudioFormat.Vorbis, AudioFormat.Opus })
            {
                var result = await adapter.ConvertAsync(authored, target, AudioConversionConsent.None, default);
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                    result.Facts.Streams.Count(stream => stream.AttachedPicture) != pictures.Length)
                    throw new InvalidDataException("Artwork conversion lost inventory, image streams or decoded frames.");
                var outputPath = Path.Combine(evidence, (big ? "large-cover" : "two-covers") + result.Plan.Extension);
                await File.WriteAllBytesAsync(outputPath, result.Bytes);
                using (var output = new MemoryStream(result.Bytes))
                {
                    var ogg = await OggMetadata.ReadAsync(output, default, true);
                    var tags = ogg.ConversionTags(expected);
                    if (tags["title"] != "Artwork title" || tags["artist"] != "Fixture artist") throw new InvalidDataException("Descriptive tags changed.");
                }
                checks.Add("FLAC artwork: exact blocks, descriptions, tags and samples to " + target + " large=" + big);
                for (var index = 0; index < pictures.Length; index++)
                {
                    using var output = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoded = await SeekableMediaProcess.RunAsync(Path.Combine(independent ?? binary, "ffmpeg.exe"), evidence, output,
                        ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "ogg", "-i", "fd:",
                            "-map", "0:v:" + index, "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], pixels.Length + 1, default);
                    if (!decoded.AsSpan().SequenceEqual(pixels)) throw new InvalidDataException("Converted cover pixels/alpha changed.");
                    checks.Add("Ogg artwork: decoded original RGBA " + target + "/" + big + "/" + index);
                }
                reports.Add(new { Target = target.ToString(), Large = big, Pictures = pictures.Length, Input = inputPath, Output = outputPath,
                    SourceSha256 = Convert.ToHexString(digest), OutputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)), result.DecodedFrames,
                    Blocks = expected.Select(block => Convert.ToHexString(SHA256.HashData(block.Data.AsSpan()))).ToArray() });
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(digest)) throw new IOException("Artwork input changed.");
            foreach (var target in new[] { AudioFormat.M4a })
                await Reject(authored, target, "unsupported artwork target " + target);
        }
        foreach (var target in new[] { AudioFormat.Vorbis, AudioFormat.Opus })
        {
            await Reject(Assemble([Picture(3, "-->", "Linked", 2, 1, "https://example.invalid/cover.png"u8.ToArray())]), target, "linked artwork");
            await Reject(Assemble([Picture(21, "image/png", "Reserved", 2, 1, Png(2, 1, small))]), target, "reserved picture type");
            await Reject(Assemble([Picture(3, "image/png", "Over budget", 2, 1, new byte[6 * 1024 * 1024])]), target, "picture transport budget");
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)).AsSpan().SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("Original fixture changed or artwork scratch remains.");
        checks.Add("FLAC to Ogg artwork: source digest and cleanup across all accepted/refused cases");

        async Task Reject(byte[] bytes, AudioFormat target, string name)
        {
            try { await adapter.ConvertAsync(bytes, target, AudioConversionConsent.None, default); throw new Exception("Artwork refusal failed: " + name); }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { checks.Add("FLAC artwork refuses " + name + " to " + target); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Artwork refusal retained scratch.");
        }
        byte[] Assemble(byte[][] pictures)
        {
            using var output = new MemoryStream(); output.Write("fLaC"u8);
            var blocks = new[] { ((byte)0, header.Blocks[0].Data.ToArray()), ((byte)4, Comments()) }
                .Concat(pictures.Select(picture => ((byte)6, picture))).ToArray();
            for (var i = 0; i < blocks.Length; i++)
            {
                var (type, data) = blocks[i]; output.WriteByte((byte)(type | (i == blocks.Length - 1 ? 128 : 0)));
                output.WriteByte((byte)(data.Length >> 16)); output.WriteByte((byte)(data.Length >> 8)); output.WriteByte((byte)data.Length); output.Write(data);
            }
            output.Write(source.AsSpan(header.AudioOffset)); return output.ToArray();
        }
    }

    private static byte[] Comments()
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        var vendor = Encoding.UTF8.GetBytes("Context Suite authored fixture"); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(2);
        foreach (var text in new[] { "TITLE=Artwork title", "ARTIST=Fixture artist" })
        { var data = Encoding.UTF8.GetBytes(text); writer.Write(data.Length); writer.Write(data); }
        return bytes.ToArray();
    }
    internal static byte[] Picture(uint type, string mime, string description, int width, int height, byte[] image, uint bitsPerPixel = 32)
    {
        using var result = new MemoryStream();
        void Number(uint number) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, number); result.Write(bytes); }
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); Number((uint)bytes.Length); result.Write(bytes); }
        Number(type); Text(mime); Text(description); Number((uint)width); Number((uint)height); Number(bitsPerPixel); Number(0); Number((uint)image.Length); result.Write(image);
        return result.ToArray();
    }
    internal static byte[] Png(int width, int height, byte[] pixels)
    {
        using var result = new MemoryStream(); result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
            for (var row = 0; row < height; row++) { zlib.WriteByte(0); zlib.Write(pixels.AsSpan(row * width * 4, width * 4)); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return result.ToArray();
        void Chunk(string name, byte[] bytes)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length); result.Write(number);
            var type = Encoding.ASCII.GetBytes(name); result.Write(type); result.Write(bytes); uint crc = uint.MaxValue;
            foreach (var value in type.Concat(bytes)) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); result.Write(number);
        }
    }
}
