using System.Buffers.Binary;
using System.Text.Json;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class FiniteResamplingContracts
{
    internal static async Task RunAsync(string binary, string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use new finite-resampling evidence.");
        Directory.CreateDirectory(root);
        using var lease = new AudioEncodingAdapter(binary, Path.Combine(root, "lease"));
        var rows = new List<object>();
        foreach (var rate in new[] { 8000, 11025, 16000, 44100, 48000, 96000, 192000 })
        foreach (var channels in new[] { 1, 2, 4, 5, 6, 7, 8 })
        foreach (var harmonic in new[] { 0, 1, 4, 20, 60, 100 })
        {
            var frames = harmonic == 0 ? 1 : 129;
            var frequency = rate * harmonic / 256.0;
            var band = harmonic == 0 ? "dc" : frequency <= Math.Min(rate, 48000) * .4 ? "passband" :
                frequency >= 30000 ? "stopband" : "transition";
            var pcm = new byte[frames * channels * 8];
            var layout = channels switch { 1 => "mono", 2 => "stereo", 4 => "quad", 5 => "5.0", 6 => "5.1", 7 => "6.1", _ => "7.1" };
            for (var index = 0; index < frames; index++)
            for (var channel = 0; channel < channels; channel++)
                BinaryPrimitives.WriteDoubleLittleEndian(pcm.AsSpan((index * channels + channel) * 8),
                    .125 / (channel + 1) * (channel % 2 == 0 ? 1 : -1) * Math.Cos(2 * Math.PI * harmonic * index / 256));
            var wave = ShortAudioResampling.Wave(pcm, rate, channels, layout, true);
            var path = Path.Combine(root, $"{rate}-{channels}-{harmonic}.wav");
            await File.WriteAllBytesAsync(path, wave);
            using var input = File.OpenRead(path);
            var decoded = await SeekableMediaProcess.RunAsync(Path.Combine(binary, "ffmpeg.exe"), root, input,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-ar", "48000",
                 "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], ShortAudioResampling.MaximumNativeBytes, default);
            var expected = ((long)frames * 48000 + rate - 1) / rate;
            if (decoded.Length < expected * channels * 8) throw new InvalidDataException("Incomplete analytic interval.");
            double maximumError = 0, energy = 0;
            for (var index = 0; index < expected; index++)
            for (var channel = 0; channel < channels; channel++)
            {
                var actual = BinaryPrimitives.ReadDoubleLittleEndian(decoded.AsSpan((index * channels + channel) * 8));
                var ideal = .125 / (channel + 1) * (channel % 2 == 0 ? 1 : -1) * Math.Cos(2 * Math.PI * frequency * index / 48000);
                if (!double.IsFinite(actual)) throw new InvalidDataException("Nonfinite analytic output.");
                maximumError = Math.Max(maximumError, Math.Abs(actual - ideal));
                energy += actual * actual;
            }
            var rms = Math.Sqrt(energy / (expected * channels));
            if (band == "dc" && maximumError > .000005 || band == "passband" && maximumError > .00125 ||
                band == "stopband" && rms > .000125)
                throw new InvalidDataException("Finite resampling failed the analytic waveform bound.");
            rows.Add(new { rate, channels, frames, frequency, band, expected, maximumError, rms });
            await File.WriteAllTextAsync(Path.Combine(root, "analytic.json"),
                JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        using var prefix = new ShortAudioResampling.Prefix(8);
        var inputBytes = new byte[65536];
        for (var index = 0; index < inputBytes.Length; index++) inputBytes[index] = (byte)index;
        await prefix.WriteAsync(inputBytes.AsMemory());
        await prefix.WriteAsync(inputBytes.AsMemory());
        if (!prefix.ToArray().AsSpan().SequenceEqual(inputBytes.AsSpan(0, 256 * 8 * 8)))
            throw new InvalidDataException("The source prefix did not retain its exact bounded interval.");
        Console.WriteLine("Passed 294 finite-resampling analytic cases and bounded-prefix capture.");
    }
}
