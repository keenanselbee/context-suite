using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// Retain every cell, including failures, so a codec-boundary failure cannot
// disappear behind a successful longer signal or an accepted refusal.
internal static class ShortAudioMatrix
{
    internal static async Task RunAsync(string binary, string root, string independent)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new short-audio evidence directory.");
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "adapter");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var decoderLease = new AudioEncodingAdapter(independent, Path.Combine(root, "decoder-lease"));
        var reports = new List<object>();
        var failures = new List<string>();
        foreach (var frames in new[] { 1, 2, 15, 16, 46, 47, 48, 127, 1023, 1024, 1151, 1152, 2049, 4801, 48000 })
        {
            var source = EncodingContracts.Wave(16, false, 48000, 2, frames);
            for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < 2; channel++)
            {
                // Distinct channel content with a nonzero first/last sample.
                var sample = (short)Math.Round(4096 * Math.Cos(2 * Math.PI * (311 + channel * 197) * frame / 48000));
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(44 + (frame * 2 + channel) * 2), sample);
            }
            var inputPath = Path.Combine(root, frames + "-source.wav");
            await File.WriteAllBytesAsync(inputPath, source);
            var sourceHash = Convert.ToHexString(SHA256.HashData(source));
            var sourceTime = File.GetLastWriteTimeUtc(inputPath);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                string? error = null;
                object? details = null;
                try
                {
                    using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var artifact = await adapter.ConvertFileAsync(input, target, AudioConversionConsent.None, default);
                    var result = artifact.Result;
                    var outputPath = Path.Combine(root, frames + "-to-" + target + result.Plan.Extension);
                    await using (var output = File.Create(outputPath))
                    {
                        await artifact.Content.CopyToAsync(output);
                    }
                    using var saved = File.OpenRead(outputPath);
                    var samples = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), root, saved,
                        ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0",
                         "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 2 * 1024 * 1024, default);
                    var finite = true;
                    for (var offset = 0; offset + 8 <= samples.Length; offset += 8)
                        finite &= double.IsFinite(BitConverter.ToDouble(samples, offset));
                    using var hashInput = File.OpenRead(outputPath);
                    var outputHash = Convert.ToHexString(await SHA256.HashDataAsync(hashInput));
                    details = new { result.Plan, result.DecodedFrames, result.ExactSamples, result.MaximumSampleError,
                        result.RootMeanSquareError, independentFrames = samples.Length / 16, finite, outputHash };
                    if (samples.Length != frames * 16 || !finite || !result.SourceMetadataVerified || !result.MissingTags.IsEmpty ||
                        (!result.Plan.AlreadyTarget && result.DecodedFrames != frames))
                        throw new InvalidDataException("Short audio changed sample count, metadata or finite samples.");
                    if (target == AudioFormat.Wave && !(await File.ReadAllBytesAsync(outputPath)).AsSpan().SequenceEqual(source))
                        throw new InvalidDataException("Same-format WAV changed bytes.");
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
                {
                    error = exception.GetType().Name + ": " + exception.Message;
                    failures.Add(frames + " -> " + target + ": " + error);
                }
                if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(inputPath))) != sourceHash ||
                    File.GetLastWriteTimeUtc(inputPath) != sourceTime || Directory.EnumerateFileSystemEntries(scratch).Any())
                    throw new IOException("Short-audio operation changed its source or left scratch.");
                reports.Add(new { frames, target = target.ToString(), sourceHash, sourceTime, error, details });
                await File.WriteAllTextAsync(Path.Combine(root, "matrix.json"), JsonSerializer.Serialize(new { reports, failures },
                    new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{(error is null ? "passed" : "FAILED")}: {frames} -> {target}" + (error is null ? "" : ": " + error));
            }
        }
        if (reports.Count != 90 || failures.Count != 0)
            throw new InvalidDataException($"Short-audio matrix: {failures.Count} failures in {reports.Count} observations.");
        Console.WriteLine("Passed 90 short-audio observations.");
    }
}
