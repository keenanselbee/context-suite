using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class AiffInputContracts
{
    internal static async Task RunAsync(string binary, string fixtures, string evidence, string independent)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(evidence);
        Console.WriteLine("AIFF adapter evidence: " + evidence);
        var scratch = Path.Combine(evidence, "scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var independentLease = new AudioEncodingAdapter(independent, Path.Combine(evidence, "decoder-lease"));
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.PrecisionReduction | AudioConversionConsent.Resampling;
        var checks = new List<string>(); var results = new List<object>(); var failures = new List<string>();
        foreach (var file in Directory.EnumerateFiles(fixtures).Where(path => Path.GetExtension(path) is ".aif" or ".aifc").Order())
            foreach (var target in new[] { AudioFormat.Wave, AudioFormat.Flac })
                await Verify(await File.ReadAllBytesAsync(file), Path.GetFileNameWithoutExtension(file), target);

        var wave = await File.ReadAllBytesAsync(Path.Combine(fixtures, "tone.wav"));
        if (!wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wave.AsSpan(36, 4).SequenceEqual("data"u8))
            throw new InvalidDataException("Use the authored plain PCM16 stereo tone fixture.");
        var samples = wave.AsSpan(44).ToArray();
        for (var offset = 0; offset < samples.Length; offset += 2) (samples[offset], samples[offset + 1]) = (samples[offset + 1], samples[offset]);
        var common = Convert.FromHexString("00020000BB800010400EBB80000000000000");
        var plain = FileOf(("COMM", common), ("SSND", new byte[8].Concat(samples).ToArray()));
        var tags = new Dictionary<string, string> { ["title"] = "Title \u00fc", ["artist"] = "Artist \u4e2d", ["comment"] = "Line 1\nLine 2" };
        var pixels = new byte[] { 255, 0, 128, 255, 0, 255, 0, 128 };
        var png = FlacToOggArtworkContracts.Png(2, 1, pixels);
        var picture = new FlacMetadataBlock(6, FlacToOggArtworkContracts.Picture(0, "image/png", "", 2, 1, png).ToImmutableArray());
        var id3 = Mp3PictureFrames.AppendToTag(WaveTextFrames.Create(tags), [picture]);
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
        {
            await Verify(plain, "tone-plain", target);
            await Verify(Append(plain, "ID3 ", id3), "tone-id3-picture", target, tags, pixels);
            await Verify(Append(Append(plain, "NAME", "Title"u8.ToArray()), "ANNO", "Comment"u8.ToArray()),
                "tone-legacy-text", target, new Dictionary<string, string> { ["title"] = "Title", ["comment"] = "Comment" });
        }
        foreach (var kind in new[] { "MARK", "INST", "COMT", "APPL", "CHAN", "abcd" })
        {
            try { await adapter.ConvertAsync(Append(plain, kind, new byte[3]), AudioFormat.Flac, consent, default); failures.Add("Unpreserved " + kind + " accepted"); }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException)
            { Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "Refused " + kind + " cleans scratch"); }
        }
        // Missing generation consent must fail before encoding a compressed source.
        try { await adapter.ConvertAsync(await File.ReadAllBytesAsync(Path.Combine(fixtures, "pcm_alaw.aifc")), AudioFormat.Mp3, AudioConversionConsent.None, default); failures.Add("Missing lossy consent accepted"); }
        catch (InvalidOperationException) { Check(true, "Compressed AIFF requires lossy-generation consent"); }
        await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { checks, results, failures }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count != 0) throw new InvalidDataException(string.Join("\n", failures));
        Console.WriteLine($"Passed {checks.Count} AIFF adapter checks with {results.Count} outputs: {evidence}");

        async Task Verify(byte[] source, string name, AudioFormat target, IReadOnlyDictionary<string, string>? tags = null, byte[]? expectedPixels = null)
        {
            var label = name + "-" + target;
            var path = Path.Combine(evidence, label + ".source"); await File.WriteAllBytesAsync(path, source);
            var hash = SHA256.HashData(source); var time = File.GetLastWriteTimeUtc(path);
            try
            {
                using var input = File.OpenRead(path);
                var admitted = await adapter.InspectConversionAsync(Guid.NewGuid(), path, input, target, default);
                Check(admitted.ConversionBlockReason is null, "AIFF inspection " + label);
                using var result = await adapter.ConvertFileAsync(input, target, consent, default);
                var info = result.Result;
                Check(info.SourceMetadataVerified && info.MissingTags.IsEmpty && !info.Plan.AlreadyTarget, "AIFF verified metadata " + label);
                var outputPath = Path.Combine(evidence, label + info.Plan.Extension);
                using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await result.Content.CopyToAsync(output);
                if (tags is not null)
                {
                    var actual = info.Facts.Tags.Concat(info.Facts.Streams.Where(stream => stream.Kind == "audio").SelectMany(stream => stream.Tags)).ToArray();
                    Check(tags.All(tag => actual.Any(value => value.Key.Equals(tag.Key, StringComparison.OrdinalIgnoreCase) && value.Value == tag.Value)), "AIFF output text " + label);
                }
                var reference = await Decode(path); var decoded = await Decode(outputPath);
                Check(decoded.Length == reference.Length, "Supplier decoded extent " + label);
                double maximum = 0, sum = 0;
                for (var offset = 0; offset < reference.Length; offset += 8)
                {
                    var error = Math.Abs(BitConverter.ToDouble(reference, offset) - BitConverter.ToDouble(decoded, offset));
                    maximum = Math.Max(maximum, error); sum += error * error;
                }
                var rms = Math.Sqrt(sum / (reference.Length / 8));
                Check(double.IsFinite(maximum) && (info.Plan.RequiresExactSamples ? maximum == 0 : target == AudioFormat.Flac ? maximum <= 1.0 / 8388608 : rms <= .02), "Supplier sample comparison " + label);
                if (expectedPixels is not null)
                {
                    using var output = File.OpenRead(outputPath);
                    var actualPixels = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, output,
                        ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:v:0", "-frames:v", "1", "-threads", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], 16, default);
                    Check(actualPixels.AsSpan().SequenceEqual(expectedPixels), "Supplier artwork pixels " + label);
                }
                input.Position = 0;
                Check((await SHA256.HashDataAsync(input)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(path) == time, "AIFF original bytes/time " + label);
                results.Add(new { name, target = target.ToString(), info.DecodedFrames, maximum, rms, info.SourceMetadataVerified });
            }
            catch (Exception error) { failures.Add(label + ": " + error.GetType().Name + ": " + error.Message); }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) failures.Add("Retained owned scratch: " + label);
            await File.WriteAllTextAsync(Path.Combine(evidence, "progress.json"), JsonSerializer.Serialize(new { checks, results, failures }));
        }
        async Task<byte[]> Decode(string path)
        {
            using var input = File.OpenRead(path);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, input,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 2 * 1024 * 1024, default);
        }
        void Check(bool passed, string message)
        { if (!passed) throw new InvalidDataException(message); checks.Add(message); }
    }

    private static byte[] Append(byte[] original, string kind, byte[] data)
    {
        using var output = new MemoryStream(); output.Write(original); output.Write(Encoding.ASCII.GetBytes(kind));
        var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); output.Write(length);
        output.Write(data); if ((data.Length & 1) != 0) output.WriteByte(0);
        var result = output.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)result.Length - 8); return result;
    }

    private static byte[] FileOf(params (string Kind, byte[] Data)[] chunks)
    {
        var result = "FORM\0\0\0\u0004AIFF"u8.ToArray();
        foreach (var (kind, data) in chunks) result = Append(result, kind, data);
        return result;
    }
}
