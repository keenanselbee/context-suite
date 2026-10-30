using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static partial class M4aPreservationContracts
{
    internal static byte[] WideOffsets(byte[] source)
    {
        var movieEnd = 0;
        for (var offset = 0; offset < source.Length;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset)));
            if (source.AsSpan(offset + 4, 4).SequenceEqual("moov"u8)) movieEnd = offset + size;
            offset += size;
        }
        return Replace(source, ["moov", "trak", "mdia", "minf", "stbl"], table =>
        {
            var expanded = Replace(table, ["stco"], data =>
            {
                var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
                var result = new byte[8 + count * 8]; data.AsSpan(0, 8).CopyTo(result);
                for (var index = 0; index < count; index++)
                {
                    ulong value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8 + index * 4));
                    if (value >= (ulong)movieEnd) value += (uint)(4 * count);
                    BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8 + index * 8), value);
                }
                return result;
            });
            var cursor = 0;
            while (cursor < expanded.Length)
            {
                if (expanded.AsSpan(cursor + 4, 4).SequenceEqual("stco"u8)) "co64"u8.CopyTo(expanded.AsSpan(cursor + 4));
                cursor += checked((int)BinaryPrimitives.ReadUInt32BigEndian(expanded.AsSpan(cursor)));
            }
            return expanded;
        });
    }

    internal static byte[] SingleArtwork(byte[] source, byte[] image, bool jpeg, bool fastStart)
    {
        var result = Replace(source, ["moov", "udta", "meta", "ilst"], _ => Text("\u00a9nam", "Artwork title")
            .Concat(Box("covr", Box("data", N(jpeg ? 13u : 14u).Concat(N(0)).Concat(image).ToArray()))).ToArray());
        return fastStart ? FastStart(result) : result;
    }

    internal static async Task PicturesAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
        var path = Path.Combine(fixtures, "source.m4a"); var source = await File.ReadAllBytesAsync(path); var sourceHash = SHA256.HashData(source);
        if (source.AsSpan().IndexOf("moov"u8) < source.AsSpan().IndexOf("mdat"u8)) throw new InvalidDataException("Expected trailing movie fixture.");
        var pixels = new byte[32 * 32 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i / 4); pixels[i + 1] = (byte)(i / 128); pixels[i + 2] = 173; pixels[i + 3] = (byte)(i / 4 % 3 == 0 ? 128 : 255); }
        var png = FlacToOggArtworkContracts.Png(32, 32, pixels); var pngPath = Path.Combine(evidence, "authored.png"); await File.WriteAllBytesAsync(pngPath, png);
        byte[] jpeg;
        using (var file = File.OpenRead(pngPath))
            jpeg = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "png_pipe", "-i", "fd:",
                    "-frames:v", "1", "-c:v", "mjpeg", "-threads", "1", "-q:v", "2", "-f", "image2pipe", "pipe:1"], 1024 * 1024, default);
        var jpegPath = Path.Combine(evidence, "authored.jpg"); await File.WriteAllBytesAsync(jpegPath, jpeg);
        var jpegPixels = await Decode(jpegPath, "jpeg_pipe", 0, pixels.Length);
        if (!(await Decode(pngPath, "png_pipe", 0, pixels.Length)).AsSpan().SequenceEqual(pixels)) throw new InvalidDataException("Authored PNG differs.");
        checks.Add("M4A artwork: authored alpha PNG and independent JPEG reference");
        var large = new byte[512 * 512 * 4]; uint seed = 73591;
        for (var i = 0; i < large.Length; i++) { seed = unchecked(seed * 1664525 + 1013904223); large[i] = (byte)(seed >> 24); }
        var scratch = Path.Combine(evidence, "m4a-artwork-scratch"); using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var shape in new[] { "covers", "split", "large" })
        foreach (var fastStart in new[] { false, true })
        {
            var big = shape == "large";
            var cover = big ? Box("covr", Data(14, FlacToOggArtworkContracts.Png(512, 512, large))) : shape == "split" ?
                Box("covr", Data(14, png)).Concat(Box("covr", Data(13, jpeg))).ToArray() : Box("covr", Data(14, png).Concat(Data(13, jpeg)).ToArray());
            var authored = Authored(cover); if (fastStart) authored = FastStart(authored);
            var name = shape + (fastStart ? "-faststart" : "-trailing"); var inputPath = Path.Combine(evidence, name + ".m4a");
            await File.WriteAllBytesAsync(inputPath, authored); var digest = SHA256.HashData(authored);
            using var input = new MemoryStream(authored); var inventory = await M4aMetadata.ReadAsync(input, default, true); var pictures = inventory.Pictures;
            if (pictures.Length != (big ? 1 : 2)) throw new InvalidDataException("M4A source picture count differs.");
            checks.Add("M4A artwork: complete managed source inventory " + name);
            var same = await adapter.ConvertAsync(authored, AudioFormat.M4a, AudioConversionConsent.None, default);
            if (!same.Plan.AlreadyTarget || !same.Bytes.AsSpan().SequenceEqual(authored)) throw new InvalidDataException("M4A no-op changed bytes.");
            checks.Add("M4A artwork: no-op keeps complete source " + name);
            foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Vorbis, AudioFormat.Opus })
            {
                await Reject(authored, target, "missing quality consent " + name, AudioConversionConsent.None);
                var result = await adapter.ConvertAsync(authored, target, consent, default);
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                    result.Facts.Streams.Count(stream => stream.AttachedPicture) != pictures.Length)
                    throw new InvalidDataException("M4A lost picture inventory, tags or priming extent: " + name + "/" + target);
                var outputPath = Path.Combine(evidence, name + result.Plan.Extension); await File.WriteAllBytesAsync(outputPath, result.Bytes);
                if (target == AudioFormat.Flac)
                {
                    var header = FlacMetadata.Parse(result.Bytes); var actual = header.Blocks.Where(block => block.Type == 6).ToArray();
                    if (actual.Length != pictures.Length || actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(pictures[i].Data.AsSpan())).Any() ||
                        FlacConversionMetadata.Read(header, true)["title"] != "Artwork title") throw new InvalidDataException("M4A to FLAC pictures/tags changed.");
                }
                else
                {
                    using var output = new MemoryStream(result.Bytes);
                    if ((await OggMetadata.ReadAsync(output, default, true)).ConversionTags(pictures)["title"] != "Artwork title")
                        throw new InvalidDataException("M4A to Ogg pictures/tags changed.");
                }
                checks.Add("M4A artwork: exact ordered picture blocks, tags and 96000 audio frames " + name + "/" + target);
                for (var i = 0; i < pictures.Length; i++)
                {
                    var expected = big ? large : i == 0 ? pixels : jpegPixels;
                    if (!(await Decode(outputPath, target == AudioFormat.Flac ? "flac" : "ogg", i, expected.Length)).AsSpan().SequenceEqual(expected))
                        throw new InvalidDataException("Independent artwork pixels differ.");
                    checks.Add("M4A artwork: independent cover RGBA " + name + "/" + target + "/" + i);
                }
                reports.Add(new { Source = inputPath, Output = outputPath, Target = target.ToString(), SourceSha256 = Convert.ToHexString(digest),
                    OutputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)), result.DecodedFrames,
                    Pictures = pictures.Select(block => Convert.ToHexString(SHA256.HashData(block.Data.AsSpan()))).ToArray() });
            }
            if (!big) await Reject(authored, AudioFormat.Wave, "duplicate empty WAV picture descriptions " + name);
            if (!big) await Reject(authored, AudioFormat.Mp3, "duplicate empty MP3 picture descriptions " + name);
            if (!SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(digest)) throw new IOException("Authored source changed.");
        }
        foreach (var target in new[] { AudioFormat.Flac, AudioFormat.Vorbis, AudioFormat.Opus })
        {
            foreach (var bad in new[] { Data(13, png), Data(14, jpeg), Data(0, png), Data(14, []), Data(14, png, 1),
                Box("url ", "https://example.invalid/cover"u8.ToArray()) })
                await Reject(Authored(Box("covr", bad)), target, "unsupported/malformed artwork");
            await Reject(Authored(Box("covr", Data(14, png)).Concat(Text("----", "Unhandled")).ToArray()), target, "unhandled metadata with cover");
            var external = Replace(Authored(Box("covr", Data(14, png))), ["moov", "trak", "mdia", "minf", "dinf", "dref"],
                _ => N(0).Concat(N(1)).Concat(Box("url ", N(0).Concat("file:///must-not-read\0"u8.ToArray()).ToArray())).ToArray());
            await Reject(external, target, "external audio reference with cover");
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(sourceHash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("M4A source changed or scratch remains.");
        checks.Add("M4A artwork: original source digest and cleanup");

        byte[] Authored(byte[] covers) => Replace(source, ["moov", "udta", "meta", "ilst"], _ => Text("\u00a9nam", "Artwork title").Concat(covers).ToArray());
        async Task Reject(byte[] bytes, AudioFormat target, string name, AudioConversionConsent accepted = consent)
        {
            try { await adapter.ConvertAsync(bytes, target, accepted, default); throw new Exception("M4A artwork refusal failed: " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException ||
                accepted == AudioConversionConsent.None && error is InvalidOperationException && error.Message == "The audio conversion needs acknowledgement of its quality changes.")
            { checks.Add("M4A artwork refuses " + name + "/" + target); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Refused artwork retained scratch.");
        }
        async Task<byte[]> Decode(string filePath, string format, int index, int bytes)
        {
            using var file = File.OpenRead(filePath);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", format, "-i", "fd:", "-map", "0:v:" + index,
                    "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], bytes + 1, default);
        }
        static byte[] Data(uint type, byte[] image, uint locale = 0) => Box("data", N(type).Concat(N(locale)).Concat(image).ToArray());
    }

    private static byte[] FastStart(byte[] source)
    {
        var roots = new List<(int Offset, int Length)>();
        for (var offset = 0; offset < source.Length;)
        { var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset))); roots.Add((offset, length)); offset += length; }
        var movie = roots.Single(root => source.AsSpan(root.Offset + 4, 4).SequenceEqual("moov"u8));
        if (movie.Offset + movie.Length != source.Length || !source.AsSpan(4, 4).SequenceEqual("ftyp"u8)) throw new InvalidDataException("Expected ftyp and trailing moov.");
        var adjusted = Replace(source, ["moov", "trak", "mdia", "minf", "stbl", "stco"], data =>
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            if (data.Length != 8 + count * 4) throw new InvalidDataException("Invalid fixture stco.");
            for (var index = 0; index < count; index++)
            {
                var offset = 8 + index * 4; var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), checked(value + (uint)movie.Length));
            }
            return data;
        });
        return adjusted[..roots[0].Length].Concat(adjusted.AsSpan(movie.Offset, movie.Length).ToArray())
            .Concat(adjusted.AsSpan(roots[0].Length, movie.Offset - roots[0].Length).ToArray()).ToArray();
    }
}
