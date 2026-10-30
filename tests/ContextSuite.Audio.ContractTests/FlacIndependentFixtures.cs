using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FlacIndependentFixtures
{
    public static async Task RunAsync(string binary, string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use new independent FLAC evidence.");
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "adapter");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        var reports = new List<object>();
        foreach (var bits in new[] { 16, 24, 32 })
        foreach (var channels in new[] { 1, 2, 6 })
        {
            const int frames = 4097;
            var rate = channels == 6 ? 96000 : 48000;
            var wave = EncodingContracts.Wave(bits, false, rate, channels, frames);
            var offset = channels > 2 ? 68 : 44;
            var sampleBytes = frames * channels * bits / 8;
            if ((wave.Length & 1) != 0)
            {
                Array.Resize(ref wave, wave.Length + 1);
                BinaryPrimitives.WriteUInt32LittleEndian(wave.AsSpan(4), (uint)wave.Length - 8);
            }
            var maximum = (1L << (bits - 1)) - 1;
            var minimum = -maximum - 1;
            long[] extremes = [minimum, maximum, minimum + 1, maximum - 1, -1, 0, 1, 2, -2, maximum / 2, minimum / 2];
            for (var frame = 0; frame < 128; frame++)
            for (var channel = 0; channel < channels; channel++)
            {
                var value = extremes[(frame + channel * 3) % extremes.Length];
                for (var index = 0; index < bits / 8; index++)
                    wave[offset + (frame * channels + channel) * (bits / 8) + index] = (byte)(value >> (index * 8));
            }
            var name = $"pcm{bits}-{channels}ch";
            var source = Path.Combine(root, name + ".wav");
            await File.WriteAllBytesAsync(source, wave);
            var raw = wave.AsSpan(offset, sampleBytes).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(root, name + ".raw"), raw);
            var converted = await adapter.ConvertAsync(wave, AudioFormat.Flac, AudioConversionConsent.None, default);
            var optimized = await adapter.OptimizeFlacAsync(converted.Bytes, default);
            var after = await File.ReadAllBytesAsync(source);
            if (!converted.ExactSamples || !optimized.ExactSamples || converted.DecodedFrames != frames ||
                optimized.DecodedFrames != frames || optimized.Bytes.Length > converted.Bytes.Length ||
                converted.Facts.Streams[0].SampleBits != bits || !wave.SequenceEqual(after))
                throw new InvalidDataException("FLAC fixture did not preserve precision, frames or original.");
            await File.WriteAllBytesAsync(Path.Combine(root, name + ".flac"), converted.Bytes);
            await File.WriteAllBytesAsync(Path.Combine(root, name + ".optimized.flac"), optimized.Bytes);
            reports.Add(new { name, bits, channels, rate, frames, SourceSha256 = Hash(wave), RawSha256 = Hash(raw),
                FlacSha256 = Hash(converted.Bytes), OptimizedSha256 = Hash(optimized.Bytes), Smaller = optimized.Bytes.Length < converted.Bytes.Length });
            Console.WriteLine("PASS: authored adapter FLAC pair " + name);
        }
        if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("Adapter left scratch files.");
        await File.WriteAllTextAsync(Path.Combine(root, "fixtures.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
