using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class LocalAudioLibraryContracts
{
    internal static async Task RunAsync(string binary, string root, string scenario)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use fresh absolute evidence.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        string? outputSha256 = null;
        if (scenario is "valid" or "local")
        {
            using (var adapter = new AudioEncodingAdapter(binary, Path.Combine(root, "scratch")))
            {
                // Authored mono PCM16 WAV: one tenth of a second, nonconstant samples.
                var source = new byte[44 + 4800 * 2];
                "RIFF"u8.CopyTo(source); BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(4), source.Length - 8);
                "WAVEfmt "u8.CopyTo(source.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(16), 16);
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(22), 1);
                BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(24), 48000); BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(28), 96000);
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(34), 16);
                "data"u8.CopyTo(source.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(40), source.Length - 44);
                for (var index = 0; index < 4800; index++)
                    BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(44 + index * 2), (short)(8192 * Math.Sin(index * 2 * Math.PI / 97)));
                var original = SHA256.HashData(source);
                var encoded = await adapter.ConvertAsync(source, AudioFormat.Flac, AudioConversionConsent.None, default);
                Check(encoded.ExactSamples && encoded.DecodedFrames == 4800, "Native WAV to FLAC preserves every sample");
                var decoded = await adapter.ConvertAsync(encoded.Bytes, AudioFormat.Wave, AudioConversionConsent.None, default);
                Check(decoded.ExactSamples && decoded.DecodedFrames == 4800, "Native FLAC to WAV preserves every sample");
                Check(SHA256.HashData(source).SequenceEqual(original), "Original unchanged");
                outputSha256 = Convert.ToHexString(SHA256.HashData(encoded.Bytes));
                await File.WriteAllBytesAsync(Path.Combine(root, "original.wav"), source);
                await File.WriteAllBytesAsync(Path.Combine(root, "output.flac"), encoded.Bytes);
                await File.WriteAllBytesAsync(Path.Combine(root, "roundtrip.wav"), decoded.Bytes);
                foreach (var path in Directory.EnumerateFiles(binary, "*.dll").Append(Path.Combine(binary, "ffprobe.exe"))
                    .Append(Path.Combine(binary, "ffmpeg.exe")).Concat(scenario == "local" ? [Path.Combine(binary, LocalLibraryManifest.FileName)] : []))
                {
                    var refused = false;
                    try { using var write = File.OpenWrite(path); }
                    catch (IOException) { refused = true; }
                    Check(refused, "Engine file locked: " + Path.GetFileName(path));
                }
                var renamed = false;
                try { Directory.Move(binary, binary + "-moved"); Directory.Move(binary + "-moved", binary); renamed = true; }
                catch (IOException) { }
                Check(!renamed, "Engine directory locked during use");
            }
        }
        else
        {
            try { using var adapter = new AudioEncodingAdapter(binary, Path.Combine(root, "scratch")); }
            catch (InvalidDataException) { checks.Add("Invalid engine/manifest refused"); }
            if (checks.Count == 0) throw new InvalidDataException("Invalid engine accepted: " + scenario);
        }
        foreach (var path in Directory.EnumerateFiles(binary, "*.dll").Append(Path.Combine(binary, "ffprobe.exe")))
        {
            using var write = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
            checks.Add("Lease released: " + Path.GetFileName(path));
        }
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new {
            Passed = true, Scenario = scenario, OutputSha256 = outputSha256, Checks = checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} local audio library checks: {scenario}.");
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidDataException(name); checks.Add(name); }
    }
}
