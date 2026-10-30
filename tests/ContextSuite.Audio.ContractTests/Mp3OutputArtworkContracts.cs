using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class Mp3OutputArtworkContracts
{
    internal static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
        var flac = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac")); var original = FlacMetadata.Parse(flac);
        var m4a = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.m4a"));
        var rgba = new byte[32 * 32 * 4];
        for (var index = 0; index < rgba.Length; index += 4)
        { rgba[index] = (byte)(index / 4); rgba[index + 1] = (byte)(index / 128); rgba[index + 2] = 173; rgba[index + 3] = (byte)(index % 12 == 0 ? 128 : 255); }
        var png = FlacToOggArtworkContracts.Png(32, 32, rgba); var pngPath = Path.Combine(evidence, "authored.png");
        await File.WriteAllBytesAsync(pngPath, png);
        byte[] jpeg;
        using (var image = File.OpenRead(pngPath))
            jpeg = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, image,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "png_pipe", "-i", "fd:",
                    "-frames:v", "1", "-c:v", "mjpeg", "-threads", "1", "-q:v", "2", "-f", "image2pipe", "pipe:1"], 1024 * 1024, default);
        var jpegPath = Path.Combine(evidence, "authored.jpg"); await File.WriteAllBytesAsync(jpegPath, jpeg);
        var jpegRgba = await Decode(jpegPath, "jpeg_pipe", 0, rgba.Length);
        var large = new byte[512 * 512 * 4]; uint random = 78293;
        for (var index = 0; index < large.Length; index++) { random = unchecked(random * 1664525 + 1013904223); large[index] = (byte)(random >> 24); }
        var largePng = FlacToOggArtworkContracts.Png(512, 512, large);
        var scratch = Path.Combine(evidence, "encoding-scratch"); using var adapter = new AudioEncodingAdapter(binary, scratch);
        var plain = await adapter.ConvertAsync(flac, AudioFormat.Mp3, AudioConversionConsent.None, default);
        var plainPath = Path.Combine(evidence, "writer-input.mp3"); await File.WriteAllBytesAsync(plainPath, plain.Bytes);
        using (var raw = File.OpenRead(plainPath))
        {
            var originalMp3 = await Mp3Metadata.ReadAsync(raw, default); using var rewritten = new MemoryStream();
            await Mp3PictureWriter.RewriteAsync(raw, rewritten, [Block(3, "image/png", "Cover", 32, png, 32)], default);
            var rewrittenInventory = await Mp3Metadata.ReadAsync(rewritten, default, true);
            check(rewritten.ToArray().AsSpan((int)rewrittenInventory.AudioOffset).SequenceEqual(plain.Bytes.AsSpan((int)originalMp3.AudioOffset)),
                "MP3 output writer: every encoded MPEG byte, including gapless header, stays unchanged");
            using var cancelled = new MemoryStream(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await Mp3PictureWriter.RewriteAsync(raw, cancelled, [Block(3, "image/png", "Cover", 32, png, 32)], cancellation.Token); throw new Exception("Cancelled picture writer ran."); }
            catch (OperationCanceledException) { check(cancelled.Length == 0, "MP3 output writer: pre-cancellation publishes no bytes"); }
        }
        check((await File.ReadAllBytesAsync(plainPath)).AsSpan().SequenceEqual(plain.Bytes), "MP3 output writer: source MP3 stays unchanged");
        foreach (var variant in new[] { "png", "jpeg", "mixed", "large" })
        {
            var big = variant == "large"; var jpg = variant == "jpeg"; var width = big ? 512 : 32;
            var image = big ? largePng : jpg ? jpeg : png;
            var expectedPixels = big ? large : jpg ? jpegRgba : rgba;
            var pictures = ImmutableArray.Create(Block(3, jpg ? "image/jpeg" : "image/png", "Front \u00fc", width, image, jpg ? 24u : 32u));
            if (variant == "mixed") pictures = pictures.Add(Block(4, "image/jpeg", "Back", 32, jpeg, 24));
            var sourceFlac = Assemble(pictures);
            var sources = new List<(string Family, string Extension, byte[] Bytes, ImmutableArray<FlacMetadataBlock> Pictures, AudioConversionConsent Consent)> {
                ("flac", ".flac", sourceFlac, pictures, AudioConversionConsent.None) };
            foreach (var target in new[] { AudioFormat.Vorbis, AudioFormat.Opus })
            {
                var result = await adapter.ConvertAsync(sourceFlac, target, AudioConversionConsent.None, default);
                sources.Add((target.ToString().ToLowerInvariant(), result.Plan.Extension, result.Bytes, pictures, AudioConversionConsent.LossyTranscoding));
            }
            if (variant != "mixed")
            foreach (var fastStart in new[] { false, true })
            {
                var bytes = M4aPreservationContracts.SingleArtwork(m4a, image, jpg, fastStart);
                using var stream = new MemoryStream(bytes); var inventory = await M4aMetadata.ReadAsync(stream, default, true);
                sources.Add((fastStart ? "m4a-fast" : "m4a-tail", ".m4a", bytes, inventory.Pictures, AudioConversionConsent.LossyTranscoding));
            }
            foreach (var source in sources)
            {
                var name = source.Family + "-" + variant; var path = Path.Combine(evidence, name + source.Extension);
                await File.WriteAllBytesAsync(path, source.Bytes); var modified = File.GetLastWriteTimeUtc(path); var hash = SHA256.HashData(source.Bytes);
                using var input = File.OpenRead(path);
                var admission = await adapter.InspectConversionAsync(Guid.NewGuid(), path, input, AudioFormat.Mp3, default);
                if (admission.ConversionBlockReason is not null) throw new InvalidDataException("MP3 artwork admission failed: " + name);
                checks.Add("MP3 output artwork: admitted " + name);
                if (source.Consent != AudioConversionConsent.None)
                {
                    try { using var refused = await adapter.ConvertFileAsync(input, AudioFormat.Mp3, AudioConversionConsent.None, default); throw new Exception("Missing quality consent was accepted."); }
                    catch (InvalidOperationException error) when (error.Message == "The audio conversion needs acknowledgement of its quality changes.")
                    { checks.Add("MP3 output artwork: lossy consent retained " + name); }
                }
                string outputPath;
                using (var artifact = await adapter.ConvertFileAsync(input, AudioFormat.Mp3, source.Consent, default))
                {
                    var result = artifact.Result;
                    if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                        result.Facts.Streams.Count(stream => stream.AttachedPicture) != source.Pictures.Length)
                        throw new InvalidDataException("MP3 artwork changed tags, covers or sample extent: " + name);
                    var inventory = await Mp3Metadata.ReadAsync(artifact.Content, default, true);
                    var expected = Mp3PictureFrames.ForOutput(source.Pictures);
                    if (inventory.Tags["title"] != "Artwork title" || inventory.Pictures.Length != expected.Length ||
                        inventory.Pictures.Where((block, index) => !block.Data.AsSpan().SequenceEqual(expected[index].Data.AsSpan())).Any())
                        throw new InvalidDataException("MP3 APIC fields or tags differ: " + name);
                    checks.Add("MP3 output artwork: exact APIC metadata and validated decoded audio " + name);
                    outputPath = Path.Combine(evidence, name + ".mp3"); artifact.Content.Position = 0;
                    await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await artifact.Content.CopyToAsync(output);
                    reports.Add(new { Source = path, Output = outputPath, result.DecodedFrames, result.RootMeanSquareError,
                        Pictures = FlacDescriptiveMetadata.ReadBlocks(inventory.Pictures).Pictures, SourceSha256 = Convert.ToHexString(hash) });
                }
                for (var index = 0; index < source.Pictures.Length; index++)
                {
                    var pixels = index == 0 ? expectedPixels : jpegRgba;
                    if (!(await Decode(outputPath, "mp3", index, pixels.Length)).AsSpan().SequenceEqual(pixels)) throw new InvalidDataException("MP3 independent cover RGBA differs.");
                    checks.Add("MP3 output artwork: independent RGBA image " + name + "/" + index);
                }
                input.Position = 0;
                if (!(await SHA256.HashDataAsync(input)).AsSpan().SequenceEqual(hash) || File.GetLastWriteTimeUtc(path) != modified || Directory.EnumerateFileSystemEntries(scratch).Any())
                    throw new IOException("MP3 output changed source or retained encoding scratch.");
                checks.Add("MP3 output artwork: preserved source bytes/time and cleanup " + name);
            }
        }
        var duplicate = Block(3, "image/png", "Same", 32, png, 32);
        foreach (var invalid in new[] { Assemble([duplicate, duplicate]), Assemble([Block(3, "image/png", "Geometry", 31, png, 32)]) })
        {
            try { await adapter.ConvertAsync(invalid, AudioFormat.Mp3, AudioConversionConsent.None, default); throw new Exception("Unrepresentable artwork was accepted."); }
            catch (NotSupportedException) { checks.Add("MP3 output refuses unrepresentable picture metadata"); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Refused MP3 artwork retained scratch.");
        }

        byte[] Assemble(ImmutableArray<FlacMetadataBlock> pictures)
        {
            using var comments = new MemoryStream(); using (var writer = new BinaryWriter(comments, Encoding.UTF8, true))
            { var vendor = "Context Suite authored fixture"u8.ToArray(); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(1); var title = "TITLE=Artwork title"u8.ToArray(); writer.Write(title.Length); writer.Write(title); }
            var header = original with { Blocks = original.Blocks.Where(block => block.Type == 0).Append(new(4, comments.ToArray().ToImmutableArray())).Concat(pictures).ToImmutableArray() };
            return FlacMetadata.CreateRecompressionHeader(header, original).Concat(flac.AsSpan(original.AudioOffset).ToArray()).ToArray();
        }
        static FlacMetadataBlock Block(uint type, string mime, string description, int width, byte[] image, uint bits) =>
            new(6, FlacToOggArtworkContracts.Picture(type, mime, description, width, width, image, bits).ToImmutableArray());
        async Task<byte[]> Decode(string path, string format, int index, int count)
        {
            using var file = File.OpenRead(path);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", format, "-i", "fd:", "-map", "0:v:" + index,
                    "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], count + 1, default);
        }
        void check(bool passed, string message)
        { if (!passed) throw new InvalidDataException(message); checks.Add(message); }
    }
}
