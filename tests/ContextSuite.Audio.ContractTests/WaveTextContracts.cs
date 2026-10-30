using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class WaveTextContracts
{
    internal static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
        var flac = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var original = FlacMetadata.Parse(flac);
        var scratch = Path.Combine(evidence, "encoding");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var probe = new AudioProbeAdapter(independent);
        var tags = new Dictionary<string, string> { ["title"] = "Title \u00fc \ud83d\ude00", ["artist"] = "\u65e5\u672c\u8a9e",
            ["album"] = "ASCII album", ["comment"] = "Line one\r\nLine two\t\u03a9 \"end\\" };
        var rgba = new byte[] { 21, 32, 43, 128 };
        var png = FlacToOggArtworkContracts.Png(1, 1, rgba);
        var picture = new FlacMetadataBlock(6, FlacToOggArtworkContracts.Picture(0, "image/png", "", 1, 1, png, 32).ToImmutableArray());
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var withPicture in new[] { false, true })
        {
            var source = Assemble(tags, withPicture ? [picture] : []);
            foreach (var format in new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
            {
                var encoded = format == AudioFormat.Flac ? source : (await adapter.ConvertAsync(source, format, consent, default)).Bytes;
                var expected = new Dictionary<string, string>(tags);
                if (format == AudioFormat.M4a) expected["language"] = "und";
                await Verify(encoded, format + (withPicture ? "-picture" : "-text"), expected, withPicture);
            }
        }
        var rich = new Dictionary<string, string>(tags) { ["album_artist"] = "Band \u00fc", ["track"] = "2/9", ["disc"] = "1/2",
            ["composer"] = "Composer \u00fc", ["copyright"] = "\u00a9 2026", ["encoded_by"] = "Person \u00fc", ["publisher"] = "Publisher \u00fc",
            ["language"] = "eng", ["performer"] = "Performer \u00fc", ["disc_subtitle"] = "Side \u00fc", ["grouping"] = "Group \u00fc",
            ["date"] = "2026-09-14", ["genre"] = "(17) literal", ["custom_field"] = "Custom \u00fc" };
        await Verify(Assemble(rich, []), "rich-fields", rich, false);
        var literal = new Dictionary<string, string>(tags) { ["track"] = "\u4e8c", ["disc"] = "Side \u4e00", ["copyright"] = "\u00a9 2026",
            ["language"] = "\u65e5\u672c\u8a9e", ["date"] = "Date \u00fc", ["genre"] = "(17) \u4e2d\u6587" };
        await Verify(Assemble(literal, []), "literal-structured-fields", literal, false);
        var large = new Dictionary<string, string> { [new string('k', 128)] = string.Concat(Enumerable.Repeat("\ud83d\ude00", 2048)) };
        await Verify(Assemble(large, []), "maximum-field", large, false);
        foreach (var invalid in new[] {
            new Dictionary<string, string> { ["title"] = "bad\0value" },
            new Dictionary<string, string> { ["title"] = "bad\u0001value" },
            new Dictionary<string, string> { ["title"] = new string('x', 4097) },
            new Dictionary<string, string> { [new string('k', 129)] = "value" },
            new Dictionary<string, string> { ["bad~key"] = "value" },
            new Dictionary<string, string> { ["title"] = "bad\ud800" } })
        {
            var rejected = false;
            try { WaveTextFrames.Create(invalid); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EncoderFallbackException) { rejected = true; }
            check(rejected, "Invalid WAV ID3 text refused before writing");
        }
        var oversized = Enumerable.Range(0, 70).ToDictionary(index => "field" + index, _ => new string('\u65e5', 4096));
        var exceeded = false;
        try { WaveTextFrames.Create(oversized); }
        catch (NotSupportedException) { exceeded = true; }
        check(exceeded, "Aggregate WAV ID3 text budget enforced");
        var refusedPath = Path.Combine(evidence, "refused-control.flac");
        var refusedBytes = Assemble(new Dictionary<string, string> { ["title"] = "Invalid\u0001text" }, []);
        await File.WriteAllBytesAsync(refusedPath, refusedBytes);
        using (var invalidSource = File.OpenRead(refusedPath))
        {
            var rejected = false;
            try { await adapter.InspectConversionAsync(Guid.NewGuid(), refusedPath, invalidSource, AudioFormat.Wave, default); }
            catch (NotSupportedException) { rejected = true; }
            check(rejected && invalidSource.Position == 0, "Invalid text cannot pass conversion inspection");
        }
        await FlacConversionContracts.RunAsync(binary, fixtures, evidence, checks, reports);

        async Task Verify(byte[] bytes, string name, IReadOnlyDictionary<string, string> expected, bool withPicture)
        {
            var path = Path.Combine(evidence, name + ".source"); await File.WriteAllBytesAsync(path, bytes);
            var hash = SHA256.HashData(bytes); var time = File.GetLastWriteTimeUtc(path);
            using var input = File.OpenRead(path);
            var admitted = await adapter.InspectConversionAsync(Guid.NewGuid(), path, input, AudioFormat.Wave, default);
            check(admitted.ConversionBlockReason is null, "WAV text admission: " + name);
            string outputPath;
            using (var artifact = await adapter.ConvertFileAsync(input, AudioFormat.Wave, consent, default))
            {
                var result = artifact.Result;
                check(result.SourceMetadataVerified && result.ExactSamples && result.DecodedFrames == 96000 && result.MissingTags.IsEmpty,
                    "Exact decoded samples and complete tag validation: " + name);
                var inventory = await WaveMetadata.ReadAsync(artifact.Content, default, true);
                var actual = inventory.Tags.Where(tag => tag.Name != "encoder").ToDictionary(tag => tag.Name, tag => tag.Value);
                check(inventory.UnsupportedChunks.IsEmpty && actual.Count == expected.Count && expected.All(tag => actual.GetValueOrDefault(tag.Key) == tag.Value),
                    "One exact INFO/ID3 value per descriptive field: " + name);
                check(inventory.Pictures.Length == (withPicture ? 1 : 0), "Picture count alongside Unicode text: " + name);
                outputPath = Path.Combine(evidence, name + ".wav"); artifact.Content.Position = 0;
                using var saved = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await artifact.Content.CopyToAsync(saved);
                reports.Add(new { name, result.DecodedFrames, result.ExactSamples, Tags = actual, Output = outputPath });
            }
            using (var output = File.OpenRead(outputPath))
            {
                var facts = await probe.ProbeSeekableAsync(output, evidence, default);
                var probed = facts.Tags.Concat(facts.Streams.Where(stream => stream.Kind == "audio").SelectMany(stream => stream.Tags));
                check(expected.All(tag => probed.Any(value => value.Key.Equals(tag.Key, StringComparison.OrdinalIgnoreCase) && value.Value == tag.Value)),
                    "Independent probe retains literal Unicode fields: " + name);
                using var roundTrip = await adapter.ConvertFileAsync(output, AudioFormat.Flac, consent, default);
                check(roundTrip.Result.SourceMetadataVerified && roundTrip.Result.DecodedFrames == 96000 &&
                    (!roundTrip.Result.Plan.RequiresExactSamples || roundTrip.Result.ExactSamples) && roundTrip.Result.MissingTags.IsEmpty,
                    "WAV ID3 source round trip: " + name);
            }
            var sourceSamples = await Decode(path); var outputSamples = await Decode(outputPath);
            check(sourceSamples.Length == 96000 * 2 * sizeof(double) && outputSamples.Length == sourceSamples.Length,
                "Independent decoded sample count: " + name);
            double maximumError = 0;
            for (var offset = 0; offset < sourceSamples.Length; offset += sizeof(double))
            {
                var difference = Math.Abs(BitConverter.ToDouble(sourceSamples, offset) - BitConverter.ToDouble(outputSamples, offset));
                if (!double.IsFinite(difference)) throw new InvalidDataException("Independent decode returned nonfinite samples.");
                maximumError = Math.Max(maximumError, difference);
            }
            // Different floating decoder builds may round differently. The
            // production adapter still requires exact own-decoder PCM equality.
            var tolerance = bytes.AsSpan(0, 4).SequenceEqual("fLaC"u8) ? 0 : 1.0 / 1048576;
            await File.WriteAllTextAsync(Path.Combine(evidence, name + "-independent-samples.json"),
                System.Text.Json.JsonSerializer.Serialize(new { maximumError, tolerance, Samples = sourceSamples.Length / sizeof(double) }));
            check(maximumError <= tolerance, "Independent decoded sample comparison: " + name);
            if (withPicture)
            {
                using var output = File.OpenRead(outputPath);
                var pixels = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, output,
                    ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:v:0", "-frames:v", "1", "-threads", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], 5, default);
                check(pixels.AsSpan().SequenceEqual(rgba), "Independent picture alpha/pixels with Unicode tags: " + name);
            }
            input.Position = 0;
            check((await SHA256.HashDataAsync(input)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(path) == time && !Directory.EnumerateFileSystemEntries(scratch).Any(),
                "Source bytes/time and scratch cleanup: " + name);
        }
        async Task<byte[]> Decode(string path)
        {
            using var input = File.OpenRead(path);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, input,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 2 * 1024 * 1024, default);
        }
        byte[] Assemble(IReadOnlyDictionary<string, string> values, ImmutableArray<FlacMetadataBlock> pictures)
        {
            using var comments = new MemoryStream(); using (var writer = new BinaryWriter(comments, Encoding.UTF8, true))
            {
                writer.Write(0); writer.Write(values.Count);
                foreach (var tag in values) { var bytes = Encoding.UTF8.GetBytes(tag.Key.ToUpperInvariant() + "=" + tag.Value); writer.Write(bytes.Length); writer.Write(bytes); }
            }
            var header = original with { Blocks = original.Blocks.Where(block => block.Type == 0).Append(new(4, comments.ToArray().ToImmutableArray())).Concat(pictures).ToImmutableArray() };
            return FlacMetadata.CreateRecompressionHeader(header, original).Concat(flac.AsSpan(original.AudioOffset).ToArray()).ToArray();
        }
        void check(bool passed, string message)
        { if (!passed) throw new InvalidDataException(message); checks.Add(message); }
    }
}
