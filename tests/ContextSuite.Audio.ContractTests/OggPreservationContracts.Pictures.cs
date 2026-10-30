using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static partial class OggPreservationContracts
{
    internal static async Task PicturesAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
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
        var jpegPixels = await Decode(jpegPath, "jpeg_pipe", 0, pixels.Length);
        if (!(await Decode(pngPath, "png_pipe", 0, pixels.Length)).AsSpan().SequenceEqual(pixels)) throw new InvalidDataException("Authored PNG pixels differ.");
        checks.Add("Ogg artwork: authored alpha PNG and independent JPEG reference");
        var large = new byte[512 * 512 * 4]; uint seed = 73591;
        for (var i = 0; i < large.Length; i++) { seed = unchecked(seed * 1664525 + 1013904223); large[i] = (byte)(seed >> 24); }
        var scratch = Path.Combine(evidence, "ogg-artwork-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var opus in new[] { false, true })
        {
            var path = Path.Combine(fixtures, opus ? "source.opus" : "source.ogg");
            var source = await File.ReadAllBytesAsync(path); var digest = SHA256.HashData(source);
            var bare = ReplaceComments(source, opus, "TITLE=Artwork title", "ARTIST=Fixture artist");
            var barePath = Path.Combine(evidence, opus ? "bare.opus" : "bare.ogg"); await File.WriteAllBytesAsync(barePath, bare);
            foreach (var big in new[] { false, true })
            {
                ImmutableArray<FlacMetadataBlock> pictures = big ? [Block(3, "image/png", "Large \u00fc", 512, large)] :
                    [Block(3, "image/png", "Front \u00fc", 32, pixels), new(6, FlacToOggArtworkContracts.Picture(4, "image/jpeg", "Back \u00fc", 32, 32, jpeg, 24).ToImmutableArray())];
                using var authoredStream = new MemoryStream();
                using (var input = File.OpenRead(barePath)) await OggPictureWriter.RewriteAsync(input, authoredStream, pictures, default);
                var authored = authoredStream.ToArray(); var authoredHash = SHA256.HashData(authored);
                var name = (opus ? "opus" : "vorbis") + (big ? "-large" : "-covers");
                var inputPath = Path.Combine(evidence, name + (opus ? ".opus" : ".ogg")); await File.WriteAllBytesAsync(inputPath, authored);
                using (var inventoryInput = new MemoryStream(authored))
                {
                    var inventory = await OggMetadata.ReadAsync(inventoryInput, default, true);
                    OggPictureComments.RequirePreserved(pictures, inventory.Descriptions);
                }
                checks.Add("Ogg artwork: authored complete source inventory " + name);
                var same = await adapter.ConvertAsync(authored, opus ? AudioFormat.Opus : AudioFormat.Vorbis, AudioConversionConsent.None, default);
                if (!same.Plan.AlreadyTarget || !same.Bytes.AsSpan().SequenceEqual(authored)) throw new InvalidDataException("Ogg no-op changed source.");
                checks.Add("Ogg artwork: same-format no-op retains bytes " + name);
                foreach (var target in new[] { AudioFormat.Flac, opus ? AudioFormat.Vorbis : AudioFormat.Opus })
                {
                    await Reject(authored, target, "missing quality consent " + name, AudioConversionConsent.None);
                    var result = await adapter.ConvertAsync(authored, target, consent, default);
                    if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                        result.Facts.Streams.Count(stream => stream.AttachedPicture) != pictures.Length)
                        throw new InvalidDataException("Ogg artwork conversion lost pictures, tags or audio extent: " + name + "/" + target);
                    var outputPath = Path.Combine(evidence, name + "-converted" + result.Plan.Extension); await File.WriteAllBytesAsync(outputPath, result.Bytes);
                    if (target == AudioFormat.Flac)
                    {
                        var header = FlacMetadata.Parse(result.Bytes); var actual = header.Blocks.Where(block => block.Type == 6).ToArray();
                        if (actual.Length != pictures.Length || actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(pictures[i].Data.AsSpan())).Any() ||
                            FlacConversionMetadata.Read(header, true)["title"] != "Artwork title" || FlacConversionMetadata.Read(header, true)["artist"] != "Fixture artist")
                            throw new InvalidDataException("Ogg to FLAC changed complete picture blocks or tags.");
                    }
                    else
                    {
                        using var output = new MemoryStream(result.Bytes); var inventory = await OggMetadata.ReadAsync(output, default, true);
                        var tags = inventory.ConversionTags(pictures);
                        if (tags["title"] != "Artwork title" || tags["artist"] != "Fixture artist") throw new InvalidDataException("Ogg tags changed.");
                    }
                    checks.Add("Ogg artwork: exact picture blocks, ordered inventory, tags and 96000 audio frames " + name + "/" + target);
                    for (var index = 0; index < pictures.Length; index++)
                    {
                        var expectedPixels = big ? large : index == 0 ? pixels : jpegPixels;
                        if (!(await Decode(outputPath, target == AudioFormat.Flac ? "flac" : "ogg", index, expectedPixels.Length)).AsSpan().SequenceEqual(expectedPixels))
                            throw new InvalidDataException("Independent decoded cover pixels differ.");
                        checks.Add("Ogg artwork: independent cover RGBA " + name + "/" + target + "/" + index);
                    }
                    reports.Add(new { Source = inputPath, Output = outputPath, Target = target.ToString(), SourceSha256 = Convert.ToHexString(authoredHash),
                        OutputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)), result.DecodedFrames,
                        Pictures = pictures.Select(block => Convert.ToHexString(SHA256.HashData(block.Data.AsSpan()))).ToArray() });
                }
                foreach (var target in new[] { AudioFormat.M4a }) await Reject(authored, target, "unreviewed artwork target " + name);
                if (!SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(authoredHash)) throw new IOException("Authored source changed.");
            }
            var valid = FlacToOggArtworkContracts.Picture(3, "image/png", "Cover", 32, 32, png);
            foreach (var target in new[] { AudioFormat.Flac, opus ? AudioFormat.Vorbis : AudioFormat.Opus })
            {
                foreach (var field in new[] {
                    "METADATA_BLOCK_PICTURE= " + Convert.ToBase64String(valid),
                    "METADATA_BLOCK_PICTURE=not-base64!",
                    Field(FlacToOggArtworkContracts.Picture(3, "-->", "Linked", 0, 0, "https://example.invalid/cover"u8.ToArray())),
                    Field(FlacToOggArtworkContracts.Picture(21, "image/png", "Reserved", 32, 32, png)) })
                    await Reject(ReplaceComments(source, opus, "TITLE=Artwork title", field), target, "malformed/noncanonical/linked/reserved picture " + opus);
                var icon = Field(FlacToOggArtworkContracts.Picture(2, "image/png", "Icon", 32, 32, png));
                await Reject(ReplaceComments(source, opus, icon, icon), target, "duplicate icons " + opus);
                foreach (var extra in new[] { "COVERART=Legacy", "REPLAYGAIN_TRACK_GAIN=-3 dB", "R128_TRACK_GAIN=-12" })
                    await Reject(ReplaceComments(source, opus, Field(valid), extra), target, "unsupported metadata with picture " + extra);
                if (opus)
                {
                    var gain = ReplaceComments(source, true, Field(valid)); var body = 27 + gain[26]; gain[body + 16] = 1;
                    Checksum(gain.AsSpan(0, PageLength(gain, 0))); await Reject(gain, target, "Opus output gain with picture");
                }
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(digest)) throw new IOException("Original fixture changed.");
            checks.Add("Ogg artwork: source digest and scratch cleanup " + opus);
        }
        if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Artwork scratch remains.");

        async Task Reject(byte[] bytes, AudioFormat target, string name, AudioConversionConsent accepted = consent)
        {
            try { await adapter.ConvertAsync(bytes, target, accepted, default); throw new Exception("Ogg artwork refusal failed: " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException ||
                accepted == AudioConversionConsent.None && error is InvalidOperationException && error.Message == "The audio conversion needs acknowledgement of its quality changes.")
            { checks.Add("Ogg artwork refuses " + name + "/" + target); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Refused artwork retained scratch.");
        }
        async Task<byte[]> Decode(string path, string format, int index, int bytes)
        {
            using var file = File.OpenRead(path);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", format, "-i", "fd:", "-map", "0:v:" + index,
                    "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], bytes + 1, default);
        }
        static string Field(byte[] block) => "METADATA_BLOCK_PICTURE=" + Convert.ToBase64String(block);
        static FlacMetadataBlock Block(uint type, string mime, string description, int width, byte[] rgba) =>
            new(6, FlacToOggArtworkContracts.Picture(type, mime, description, width, width, FlacToOggArtworkContracts.Png(width, width, rgba)).ToImmutableArray());
    }
}
