using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

// Generated-signal acceptance of the actual fixed adapter policy, not listening
// certification. Retain failures alongside successes rather than skipping cells.
internal static class RateLayoutMatrix
{
    public static async Task RunAsync(string binary, string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use new rate/layout evidence.");
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "adapter");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        var reports = new List<object>();
        var unexpected = new List<string>();
        var cases = new List<(int Rate, int Channels, uint Mask, string Layout)>();
        foreach (var rate in new[] { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 176400, 192000 })
        foreach (var channels in new[] { 1, 2 })
            cases.Add((rate, channels, 0, channels == 1 ? "mono" : "stereo"));
        foreach (var layout in new (int Channels, uint Mask, string Name)[]
        {
            (3, 0xb, "2.1"), (4, 0x33, "quad"), (4, 0x107, "4.0"),
            (5, 0x37, "5.0"), (5, 0x607, "5.0(side)"), (6, 0x3f, "5.1"),
            (6, 0x60f, "5.1(side)"), (7, 0x70f, "6.1"), (8, 0x63f, "7.1")
        }) cases.Add((48000, layout.Channels, layout.Mask, layout.Name));

        foreach (var item in cases)
        {
            var name = $"{item.Rate}-{item.Layout.Replace('(', '-').Replace(")", "")}";
            var wave = Wave(item.Rate, item.Channels, item.Mask);
            var path = Path.Combine(root, name + ".wav");
            await File.WriteAllBytesAsync(path, wave);
            var hash = Convert.ToHexString(SHA256.HashData(wave));
            var modified = File.GetLastWriteTimeUtc(path);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                string outcome;
                string? errorText = null;
                object? details = null;
                try
                {
                    const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding |
                        AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
                    var result = await adapter.ConvertAsync(wave, target, consent, default);
                    var output = Path.Combine(root, name + "-to-" + target + result.Plan.Extension);
                    await File.WriteAllBytesAsync(output, result.Bytes);
                    var audio = result.Facts.Streams.Single(stream => stream.Kind == "audio");
                    if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty ||
                        audio.Channels != item.Channels || audio.SampleRate != result.Plan.SampleRate ||
                        (item.Channels > 2 && audio.ChannelLayout != item.Layout) ||
                        (!result.Plan.AlreadyTarget && result.DecodedFrames != result.Plan.SampleRate) ||
                        (!result.Plan.AlreadyTarget && result.RootMeanSquareError is > .1 or null) ||
                        (target == AudioFormat.Flac && !result.ExactSamples))
                        throw new InvalidDataException("Matrix result violated declared samples/layout/metadata.");
                    details = new { result.Plan, result.DecodedFrames, result.ExactSamples, result.MaximumSampleError,
                        result.RootMeanSquareError, audio.ChannelLayout, outputBytes = result.Bytes.Length,
                        outputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)) };
                    outcome = "passed";
                }
                catch (Exception error) when (error is NotSupportedException or InvalidDataException)
                {
                    outcome = error is NotSupportedException ? "refused" : "validation-failed";
                    errorText = error.Message;
                }
                var after = await File.ReadAllBytesAsync(path);
                if (!wave.SequenceEqual(after) || modified != File.GetLastWriteTimeUtc(path) ||
                    Directory.EnumerateFileSystemEntries(scratch).Any())
                    throw new IOException("Matrix changed an original or left adapter scratch.");
                reports.Add(new { source = name, item.Rate, item.Channels, item.Layout, target = target.ToString(),
                    sourceSha256 = hash, outcome, error = errorText, details });
                var expectedRefusal = target switch
                {
                    AudioFormat.Mp3 => item.Channels > 2 || item.Rate > 48000,
                    AudioFormat.M4a => item.Rate > 96000 || item.Layout is "5.0(side)" or "5.1(side)" or "6.1",
                    AudioFormat.Vorbis or AudioFormat.Opus => item.Layout is "2.1" or "4.0" or "5.0(side)" or "5.1(side)",
                    _ => false
                };
                if (outcome != (expectedRefusal ? "refused" : "passed"))
                    unexpected.Add(name + " -> " + target + ": " + outcome);
                await File.WriteAllTextAsync(Path.Combine(root, "matrix.json"),
                    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{outcome}: {name} -> {target}" + (errorText is null ? "" : ": " + errorText));
            }
        }
        Console.WriteLine($"Completed {reports.Count} rate/layout observations. Refusals and validation failures are not passes.");
        if (reports.Count != 222 || unexpected.Count != 0)
            throw new InvalidDataException("Unexpected matrix outcomes: " + string.Join("; ", unexpected));
    }

    internal static byte[] Wave(int rate, int channels, uint mask)
    {
        // One full second gives an exact 48 kHz resampling count at every rate.
        // Channels have distinct in-band tones; the LFE stays below 120 Hz.
        var bytes = EncodingContracts.Wave(16, false, rate, channels, rate);
        var offset = channels > 2 ? 68 : 44;
        if (channels > 2) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), mask);
        var speakers = Enumerable.Range(0, 32).Where(bit => (mask & (1u << bit)) != 0).ToArray();
        for (var frame = 0; frame < rate; frame++)
        for (var channel = 0; channel < channels; channel++)
        {
            var frequency = channels > 2 && speakers[channel] == 3 ? 60 : 211 + channel * 137;
            var value = (short)Math.Round(8192 * Math.Sin(2 * Math.PI * frequency * frame / rate));
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + (frame * channels + channel) * 2), value);
        }
        return bytes;
    }
}
