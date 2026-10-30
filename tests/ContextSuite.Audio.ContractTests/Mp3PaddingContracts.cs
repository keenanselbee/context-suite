using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class Mp3PaddingContracts
{
    internal static async Task RunAsync(string binary, string root, string independent)
    {
        if (Directory.Exists(root)) throw new IOException("Use new MP3 padding evidence.");
        Directory.CreateDirectory(root);
        using var adapter = new AudioEncodingAdapter(binary, Path.Combine(root, "adapter"));
        using var decoderLease = new AudioEncodingAdapter(independent, Path.Combine(root, "decoder-lease"));
        var rows = new List<object>();
        var failures = new List<string>();
        foreach (var rate in new[] { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 })
        foreach (var channels in new[] { 1, 2 })
        foreach (var frames in new[] { 1, 2, 15, 46, 47 })
        {
            var name = $"{rate}-{channels}-{frames}";
            var source = EncodingContracts.Wave(16, false, rate, channels, frames);
            for (var frame = 0; frame < frames; frame++)
            for (var channel = 0; channel < channels; channel++)
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(44 + (frame * channels + channel) * 2),
                    (short)Math.Round(4096 * Math.Cos(2 * Math.PI * (311 + channel * 197) * frame / rate)));
            var inputPath = Path.Combine(root, name + ".wav");
            await File.WriteAllBytesAsync(inputPath, source);
            var time = File.GetLastWriteTimeUtc(inputPath);
            string? error = null;
            object? details = null;
            try
            {
                var encoded = await adapter.ConvertAsync(source, AudioFormat.Mp3, AudioConversionConsent.None, default);
                var output = Path.Combine(root, name + ".mp3");
                await File.WriteAllBytesAsync(output, encoded.Bytes);
                using var saved = File.OpenRead(output);
                var automaticDecode = true;
                try
                {
                    await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), root, saved,
                        ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 65536, default);
                }
                catch (InvalidDataException) { automaticDecode = false; }
                saved.Position = 0;
                var pcm = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), root, saved,
                    ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-f", "mp3", "-i", "fd:", "-map", "0:a:0", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 65536, default);
                if (encoded.DecodedFrames != frames || pcm.Length != frames * channels * 8)
                    throw new InvalidDataException("MP3 sample extent changed.");
                for (var offset = 0; offset < pcm.Length; offset += 8)
                    if (!double.IsFinite(BitConverter.ToDouble(pcm, offset))) throw new InvalidDataException("Nonfinite independent sample.");
                // Encode an uncorrected control with the identical source,
                // metadata mapping and fixed quality options.
                var rawPath = Path.Combine(root, name + "-raw.mp3");
                using var input = File.OpenRead(inputPath);
                await SeekableMediaProcess.RunAsync(Path.Combine(binary, "ffmpeg.exe"), root, input,
                    ["-hide_banner", "-v", "error", "-nostdin", "-n", "-xerror", "-err_detect", "explode", "-protocol_whitelist", "fd",
                     "-format_whitelist", "wav,flac,mp3,mov,ogg", "-threads", "1", "-filter_threads", "1", "-i", "fd:",
                     "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "0", "-map_metadata:s:a:0", "0:s:a:0", "-threads", "1", "-ar", rate.ToString(),
                     "-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3", rawPath], 65536, default);
                var raw = await File.ReadAllBytesAsync(rawPath);
                using var rawInput = File.OpenRead(rawPath);
                var inventory = await Mp3Metadata.ReadAsync(rawInput, default);
                var start = checked((int)inventory.AudioOffset);
                var bits = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(start));
                var version = (bits >> 19) & 3;
                var xing = start + 4 + (version == 3 ? channels == 1 ? 17 : 32 : channels == 1 ? 9 : 17);
                if (raw.Length != encoded.Bytes.Length) throw new InvalidDataException("Reconciliation changed MP3 length.");
                var changed = Enumerable.Range(0, raw.Length).Where(index => raw[index] != encoded.Bytes[index]).ToArray();
                if (changed.Any(index => index < xing + 141 || index > xing + 143 && index < xing + 154 || index > xing + 155) ||
                    (frames == 47 && changed.Length != 0)) throw new InvalidDataException("Reconciliation changed data outside delay/padding and checksum.");
                details = new { encoded.Plan, encoded.DecodedFrames, independentFrames = pcm.Length / (channels * 8), automaticDecode,
                    encoded.MaximumSampleError, encoded.RootMeanSquareError, changedBytes = changed,
                    sourceSha256 = Convert.ToHexString(SHA256.HashData(source)), outputSha256 = Convert.ToHexString(SHA256.HashData(encoded.Bytes)) };
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
            { error = exception.GetType().Name + ": " + exception.Message; failures.Add(name + ": " + error); }
            if (!(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(source) || File.GetLastWriteTimeUtc(inputPath) != time ||
                Directory.EnumerateFileSystemEntries(Path.Combine(root, "adapter")).Any()) throw new IOException("Source or cleanup failure.");
            rows.Add(new { rate, channels, frames, error, details });
            await File.WriteAllTextAsync(Path.Combine(root, "matrix.json"), JsonSerializer.Serialize(new { rows, failures }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine((error is null ? "PASS: " : "FAIL: ") + name + (error is null ? "" : ": " + error));
        }
        if (rows.Count != 90 || failures.Count != 0) throw new InvalidDataException($"MP3 padding matrix: {failures.Count} failures in {rows.Count} cases.");
        await GuardsAsync(root);
        Console.WriteLine("Passed 90 MP3 padding cases.");
    }

    private static async Task GuardsAsync(string root)
    {
        var original = await File.ReadAllBytesAsync(Path.Combine(root, "48000-2-1-raw.mp3"));
        using var source = File.OpenRead(Path.Combine(root, "48000-2-1-raw.mp3"));
        var inventory = await Mp3Metadata.ReadAsync(source, default);
        var xing = checked((int)inventory.AudioOffset + 36);
        var checks = new List<string>();
        foreach (var name in new[] { "checksum", "flags", "encoder", "frame-count", "byte-count", "truncated", "zero-reference", "long-reference", "cancelled" })
        {
            var bytes = original.ToArray();
            switch (name)
            {
                case "checksum": bytes[xing + 155] ^= 1; break;
                case "flags": bytes[xing + 7] = 0; break;
                case "encoder": bytes[xing + 120] = 0; break;
                case "frame-count": bytes[xing + 11]++; break;
                case "byte-count": bytes[xing + 15] ^= 1; break;
                case "truncated": bytes = bytes[..^1]; break;
            }
            var path = Path.Combine(root, "guard-" + name + ".mp3");
            await File.WriteAllBytesAsync(path, bytes);
            var refused = false;
            using (var candidate = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    await Mp3GaplessPadding.ReconcileAsync(candidate, name == "zero-reference" ? 0 : name == "long-reference" ? 47 : 1,
                        name == "cancelled" ? new CancellationToken(true) : default);
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OperationCanceledException)
                { refused = true; }
            }
            if (!refused || !(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException("MP3 guard failed or changed candidate: " + name);
            checks.Add(name);
        }
        var corrected = await File.ReadAllBytesAsync(Path.Combine(root, "48000-2-1.mp3"));
        var repeatPath = Path.Combine(root, "guard-idempotent.mp3");
        await File.WriteAllBytesAsync(repeatPath, corrected);
        using (var candidate = new FileStream(repeatPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Mp3GaplessPadding.ReconcileAsync(candidate, 1, default);
        if (!(await File.ReadAllBytesAsync(repeatPath)).AsSpan().SequenceEqual(corrected))
            throw new InvalidDataException("Repeated MP3 reconciliation changed the candidate.");
        checks.Add("idempotent");
        await File.WriteAllTextAsync(Path.Combine(root, "guards.json"), JsonSerializer.Serialize(checks));
        Console.WriteLine($"Passed {checks.Count} MP3 candidate guards.");
    }
}
