using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// Duration comes from generated source frames, independently of the resampler
// used by both encoding and signal validation. Retain every failed admission.
internal static class ShortResamplingMatrix
{
    internal static async Task RunSourcesAsync(string binary, string root, string independent)
    {
        if (Directory.Exists(root)) throw new IOException("Use new resampling-source evidence.");
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "adapter");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var decoderLease = new AudioEncodingAdapter(independent, Path.Combine(root, "decoder-lease"));
        var rows = new List<object>();
        foreach (var sourceFrames in new[] { 47, 44107 })
        foreach (var channels in new[] { 1, 2 })
        foreach (var format in new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis })
        {
            var expectedFrames = ((long)sourceFrames * 48000 + 44100 - 1) / 44100;
            var wave = EncodingContracts.Wave(16, false, 44100, channels, sourceFrames);
            for (var frame = 0; frame < sourceFrames; frame++)
            for (var channel = 0; channel < channels; channel++)
                BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(44 + (frame * channels + channel) * 2),
                    (short)Math.Round(4096 * Math.Cos(2 * Math.PI * (311 + channel * 197) * frame / 44100)));
            var title = "Finite resampling\0"u8.ToArray();
            var info = new byte[12 + title.Length + (title.Length & 1)];
            "INFOINAM"u8.CopyTo(info);
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), (uint)title.Length);
            title.CopyTo(info, 12);
            var seed = new byte[wave.Length + 8 + info.Length];
            wave.CopyTo(seed, 0);
            "LIST"u8.CopyTo(seed.AsSpan(wave.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(wave.Length + 4), (uint)info.Length);
            info.CopyTo(seed, wave.Length + 8);
            BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(4), (uint)seed.Length - 8);
            var source = await adapter.ConvertAsync(seed, format, AudioConversionConsent.None, default);
            var name = sourceFrames + "-" + channels + "-" + format;
            var path = Path.Combine(root, name + source.Plan.Extension);
            await File.WriteAllBytesAsync(path, source.Bytes);
            var modified = File.GetLastWriteTimeUtc(path);
            using var input = File.OpenRead(path);
            var originalPcm = await DecodeAsync(input);
            if (originalPcm.Length != sourceFrames * channels * 8)
                throw new InvalidDataException("The independent decoder found a different source extent.");
            input.Position = 0;
            using (var artifact = await adapter.ConvertFileAsync(input, AudioFormat.Opus,
                AudioConversionConsent.Resampling | AudioConversionConsent.LossyTranscoding, default))
            {
                var outputPath = Path.Combine(root, name + ".opus");
                await using (var output = File.Create(outputPath)) await artifact.Content.CopyToAsync(output);
                using var outputInput = File.OpenRead(outputPath);
                var pcm = await DecodeAsync(outputInput);
                if (pcm.Length != expectedFrames * channels * 8 || artifact.Result.DecodedFrames != expectedFrames ||
                    !artifact.Result.SourceMetadataVerified || !artifact.Result.MissingTags.IsEmpty ||
                    !artifact.Result.Facts.Tags.Values.Concat(artifact.Result.Facts.Streams.SelectMany(stream => stream.Tags.Values)).Contains("Finite resampling"))
                    throw new InvalidDataException("Resampling changed the intended source duration or metadata.");
                for (var offset = 0; offset < pcm.Length; offset += 8)
                    if (!double.IsFinite(BitConverter.ToDouble(pcm, offset))) throw new InvalidDataException("Nonfinite independent samples.");
                rows.Add(new { format = format.ToString(), channels, sourceFrames, expectedFrames,
                    independentFrames = pcm.Length / (channels * 8),
                    sourceSha256 = Convert.ToHexString(SHA256.HashData(source.Bytes)),
                    outputSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(outputPath))) });
            }
            if (!(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(source.Bytes) ||
                File.GetLastWriteTimeUtc(path) != modified || Directory.EnumerateFileSystemEntries(scratch).Any())
                throw new IOException("Resampling-source preservation or cleanup failure.");
            await File.WriteAllTextAsync(Path.Combine(root, "sources.json"),
                JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: resample {format}, {channels} channels, {sourceFrames} -> {expectedFrames} frames");
        }
        Console.WriteLine("Passed sixteen tagged resampling-source cases.");

        Task<byte[]> DecodeAsync(FileStream source) => SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), root, source,
            ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0",
             "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 1024 * 1024, default);
    }

    internal static async Task RunAsync(string binary, string root, string independent)
    {
        if (Directory.Exists(root)) throw new IOException("Use new short-resampling evidence.");
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "adapter");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var decoderLease = new AudioEncodingAdapter(independent, Path.Combine(root, "decoder-lease"));
        var rows = new List<object>();
        var failures = new List<string>();
        var unsafeResults = new List<string>();
        foreach (var rate in new[] { 8000, 11025, 16000, 44100, 48000, 96000, 192000 })
        foreach (var channels in new[] { 1, 2 })
        foreach (var frames in new[] { 1, 16, 47, 127, 255, 256, 257 })
        {
            var name = $"{rate}-{channels}-{frames}";
            var source = EncodingContracts.Wave(16, false, rate, channels, frames);
            for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < channels; channel++)
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(44 + (frame * channels + channel) * 2),
                    (short)Math.Round(4096 * Math.Cos(2 * Math.PI * (311 + channel * 197) * frame / rate)));
            var inputPath = Path.Combine(root, name + ".wav");
            await File.WriteAllBytesAsync(inputPath, source);
            var modified = File.GetLastWriteTimeUtc(inputPath);
            var expectedFrames = ((long)frames * 48000 + rate - 1) / rate;
            var minimumFrames = Math.Max(1, (long)frames * 48000 / rate);
            string? error = null;
            object? details = null;
            try
            {
                using var input = File.OpenRead(inputPath);
                using var artifact = await adapter.ConvertFileAsync(input, AudioFormat.Opus,
                    AudioConversionConsent.Resampling, default);
                var path = Path.Combine(root, name + ".opus");
                await using (var output = File.Create(path)) await artifact.Content.CopyToAsync(output);
                using var saved = File.OpenRead(path);
                var pcm = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), root, saved,
                    ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0",
                     "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 65536, default);
                var finite = true;
                for (var offset = 0; offset + 8 <= pcm.Length; offset += 8)
                    finite &= double.IsFinite(BitConverter.ToDouble(pcm, offset));
                var result = artifact.Result;
                details = new { result.DecodedFrames, independentFrames = pcm.Length / (channels * 8), finite,
                    result.MaximumSampleError, result.RootMeanSquareError,
                    outputSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))) };
                var actualFrames = pcm.Length / (channels * 8);
                if (result.DecodedFrames != actualFrames || actualFrames < minimumFrames || actualFrames > expectedFrames ||
                    frames <= 256 && actualFrames != expectedFrames || pcm.Length % (channels * 8) != 0 || !finite)
                {
                    unsafeResults.Add(name);
                    throw new InvalidDataException("Accepted output does not preserve the independently calculated sample extent.");
                }
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty)
                    throw new InvalidDataException("Resampled result did not verify its source metadata.");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                failures.Add(name + ": " + error);
            }
            if (!(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(source) ||
                File.GetLastWriteTimeUtc(inputPath) != modified || Directory.EnumerateFileSystemEntries(scratch).Any())
                throw new IOException("Short-resampling source or cleanup failure.");
            rows.Add(new { rate, channels, frames, minimumFrames, expectedFrames, error, details,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(source)) });
            await File.WriteAllTextAsync(Path.Combine(root, "matrix.json"),
                JsonSerializer.Serialize(new { rows, failures, unsafeResults }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine((error is null ? "PASS: " : "FAIL: ") + name + (error is null ? "" : ": " + error));
        }
        if (rows.Count != 98 || failures.Count != 0)
            throw new InvalidDataException($"Short resampling: {failures.Count} failures, {unsafeResults.Count} invalid accepted outputs in {rows.Count} cases.");
        Console.WriteLine("Passed 98 short-resampling cases.");
    }
}
