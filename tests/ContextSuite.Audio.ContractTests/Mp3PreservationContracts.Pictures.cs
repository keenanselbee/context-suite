using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static partial class Mp3PreservationContracts
{
    internal static byte[] WaveTag(int version, int encoding, byte[] png, byte[] jpeg, bool neutral = false) =>
        neutral ? Tag(version, Frame(version, version == 2 ? "TT2" : "TIT2", Text(encoding, "Artwork title")),
            Picture(version, encoding, "image/png", 0, "", png)) :
        Tag(version, Frame(version, version == 2 ? "TT2" : "TIT2", Text(encoding, "Artwork title")),
            Frame(version, version == 2 ? "TP1" : "TPE1", Text(encoding, "Fixture artist")),
            Picture(version, encoding, "image/png", 3, "Front \u00fc", png), Picture(version, encoding, "image/jpeg", 4, "Back \u00fc", jpeg));

    internal static async Task PicturesAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
        var path = Path.Combine(fixtures, "source.mp3"); var source = await File.ReadAllBytesAsync(path); var hash = SHA256.HashData(source);
        using var input = new MemoryStream(source);
        var sourceInventory = await Mp3Metadata.ReadAsync(input, default);
        var frames = source[checked((int)sourceInventory.AudioOffset)..];
        var pixels = new byte[32 * 32 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i / 4); pixels[i + 1] = (byte)(i / 128); pixels[i + 2] = 173; pixels[i + 3] = (byte)(i / 4 % 3 == 0 ? 128 : 255); }
        var png = FlacToOggArtworkContracts.Png(32, 32, pixels);
        var pngPath = Path.Combine(evidence, "authored.png"); await File.WriteAllBytesAsync(pngPath, png);
        byte[] jpeg;
        using (var file = File.OpenRead(pngPath))
            jpeg = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "png_pipe", "-i", "fd:",
                    "-frames:v", "1", "-c:v", "mjpeg", "-threads", "1", "-q:v", "2", "-f", "image2pipe", "pipe:1"], 1024 * 1024, default);
        var jpegPath = Path.Combine(evidence, "authored.jpg"); await File.WriteAllBytesAsync(jpegPath, jpeg);
        var jpegPixels = await Decode(jpegPath, "jpeg_pipe", 0);
        if (!(await Decode(pngPath, "png_pipe", 0)).AsSpan().SequenceEqual(pixels) || jpegPixels.Length != pixels.Length)
            throw new InvalidDataException("Independent cover fixture decoding failed.");
        checks.Add("MP3 artwork: authored PNG RGBA and independently encoded/decoded JPEG reference");
        var scratch = Path.Combine(evidence, "mp3-artwork-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var (version, encoding) in new[] { (2, 0), (2, 1), (3, 0), (3, 1), (4, 2), (4, 3) })
        {
            var tag = Tag(version, Frame(version, version == 2 ? "TT2" : "TIT2", Text(encoding, "Artwork title")),
                Frame(version, version == 2 ? "TP1" : "TPE1", Text(encoding, "Fixture artist")),
                Picture(version, encoding, "image/png", 3, "Front \u00fc", png), Picture(version, encoding, "image/jpeg", 4, "Back \u00fc", jpeg));
            await Accept(tag, "v" + version + "-e" + encoding);
        }
        foreach (var version in new[] { 2, 3, 4 })
        {
            var payload = Picture(version, 0, "image/jpeg", 3, "Escaped", jpeg);
            if (version <= 3) payload = Escape(payload);
            else payload = Frame(4, "APIC", Escape(payload[10..]), 2);
            var tag = Tag(version, payload); if (version <= 3) tag[5] = 128;
            await Accept(tag, "unsync-v" + version, false);
        }
        foreach (var target in new[] { AudioFormat.M4a })
            await Reject(Tag(4, Picture(4, 3, "image/png", 3, "Front", png)), target, "unreviewed target");
        foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Vorbis, AudioFormat.Opus })
        {
            await Reject(Tag(4, Picture(4, 3, "-->", 3, "Linked", "https://example.invalid/cover.png"u8.ToArray())), target, "linked picture");
            await Reject(Tag(4, Picture(4, 3, "image/png", 21, "Reserved", png)), target, "reserved type");
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("Artwork source changed or scratch remains.");
        checks.Add("MP3 artwork: original source hash and scratch cleanup");

        async Task Accept(byte[] tag, string name, bool multiple = true)
        {
            Console.WriteLine("Checking MP3 artwork " + name);
            var authored = tag.Concat(frames).ToArray(); var digest = SHA256.HashData(authored);
            var inputPath = Path.Combine(evidence, name + ".mp3"); await File.WriteAllBytesAsync(inputPath, authored);
            using var original = new MemoryStream(authored);
            var expected = (await Mp3Metadata.ReadAsync(original, default, true)).Pictures;
            var same = await adapter.ConvertAsync(authored, AudioFormat.Mp3, AudioConversionConsent.None, default);
            if (!same.Plan.AlreadyTarget || !same.Bytes.AsSpan().SequenceEqual(authored)) throw new InvalidDataException("MP3 artwork no-op changed bytes.");
            checks.Add("MP3 artwork: same-format bytes " + name);
            foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Vorbis, AudioFormat.Opus })
            {
                var result = await adapter.ConvertAsync(authored, target, consent, default);
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                    result.Facts.Streams.Count(stream => stream.AttachedPicture) != expected.Length)
                    throw new InvalidDataException("MP3 artwork conversion lost inventory or audio extent.");
                var outputPath = Path.Combine(evidence, name + result.Plan.Extension); await File.WriteAllBytesAsync(outputPath, result.Bytes);
                ImmutableDictionary<string, string> tags;
                if (target == AudioFormat.Flac)
                {
                    var header = FlacMetadata.Parse(result.Bytes); var actual = header.Blocks.Where(block => block.Type == 6).ToArray();
                    if (actual.Length != expected.Length || actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(expected[i].Data.AsSpan())).Any())
                        throw new InvalidDataException("FLAC output changed a complete MP3 picture block.");
                    tags = FlacConversionMetadata.Read(header, true);
                }
                else { using var output = new MemoryStream(result.Bytes); tags = (await OggMetadata.ReadAsync(output, default, true)).ConversionTags(expected); }
                if (multiple && (tags["title"] != "Artwork title" || tags["artist"] != "Fixture artist")) throw new InvalidDataException("Artwork overwrote descriptive tags.");
                checks.Add("MP3 artwork: exact blocks, tags and 96000 audio frames " + name + "/" + target);
                for (var i = 0; i < expected.Length; i++)
                {
                    var decoded = await Decode(outputPath, target == AudioFormat.Flac ? "flac" : "ogg", i);
                    var reference = multiple && i == 0 ? pixels : jpegPixels;
                    if (!decoded.AsSpan().SequenceEqual(reference)) throw new InvalidDataException("Output cover RGBA disagrees with independent source decode.");
                    checks.Add("MP3 artwork: independent cover RGBA " + name + "/" + target + "/" + i);
                }
                reports.Add(new { Name = name, Target = target.ToString(), Input = inputPath, Output = outputPath,
                    SourceSha256 = Convert.ToHexString(digest), OutputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)), result.DecodedFrames,
                    Blocks = expected.Select(block => Convert.ToHexString(SHA256.HashData(block.Data.AsSpan()))).ToArray() });
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(digest)) throw new IOException("Authored source changed.");
        }
        async Task<byte[]> Decode(string filePath, string format, int index)
        {
            using var file = File.OpenRead(filePath);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", format, "-i", "fd:",
                    "-map", "0:v:" + index, "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], pixels.Length + 1, default);
        }
        async Task Reject(byte[] tag, AudioFormat target, string name)
        {
            try { await adapter.ConvertAsync(tag.Concat(frames).ToArray(), target, consent, default); throw new Exception("MP3 artwork refusal failed: " + name); }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { checks.Add("MP3 artwork refuses " + name + "/" + target); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Refused artwork retained scratch.");
        }
    }

    private static byte[] Picture(int version, int encoding, string mime, byte type, string description, byte[] image)
    {
        var format = Encoding.ASCII.GetBytes(version == 2 ? mime == "image/png" ? "PNG" : "JPG" : mime + "\0");
        return Frame(version, version == 2 ? "PIC" : "APIC", new[] { (byte)encoding }.Concat(format).Append(type)
            .Concat(Text(encoding, description + "\0")[1..]).Concat(image).ToArray());
    }
}
