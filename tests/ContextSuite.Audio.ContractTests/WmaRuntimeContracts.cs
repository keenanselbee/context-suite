using System.Collections.Immutable;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class WmaRuntimeContracts
{
    internal static async Task RunAsync(string previous, string repaired, string corpus, string evidence)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence already exists.");
        Directory.CreateDirectory(evidence);
        var checks = new List<string>();
        var empty = ImmutableDictionary<string, string>.Empty;
        foreach (var bits in new[] { 16, 24 })
        {
            var inventory = new AsfMetadataInventory("wmalossless", 48000, 2, bits, 1, 100, empty, [], []);
            foreach (int? nativeBits in new int?[] { null, bits })
            {
                var facts = new AudioProbeFacts("asf", null, [new(0, "audio", "wmalossless", 48000, 2, nativeBits, null, null, "stereo", empty)], empty);
                var actual = AudioEncodingAdapter.PreserveAsfPrecision(facts, inventory);
                if (actual.Streams[0].SampleBits != bits) throw new Exception("ASF lossless precision not preserved.");
                checks.Add("inventoried Lossless precision supplements absent native value or agrees with it: " + bits);
            }
            foreach (var bad in new[]
            {
                new AudioProbeFacts("asf", null, [new(0, "audio", "wmalossless", 48000, 2, bits == 16 ? 24 : 16, null, null, "stereo", empty)], empty),
                new AudioProbeFacts("asf", null, [new(0, "audio", "wmalossless", 44100, 2, bits, null, null, "stereo", empty)], empty),
                new AudioProbeFacts("asf", null, [new(0, "audio", "wmav2", 48000, 2, bits, null, null, "stereo", empty)], empty),
                new AudioProbeFacts("wav", null, [new(0, "audio", "wmalossless", 48000, 2, bits, null, null, "stereo", empty)], empty)
            })
            {
                try { AudioEncodingAdapter.PreserveAsfPrecision(bad, inventory); throw new Exception("Conflicting Lossless facts admitted."); }
                catch (InvalidDataException) { checks.Add("conflicting Lossless precision/profile refused"); }
            }
        }
        using (var old = new AudioEncodingAdapter(previous, Path.Combine(evidence, "previous")))
        using (var current = new AudioEncodingAdapter(repaired, Path.Combine(evidence, "repaired")))
        {
            foreach (var name in new[] { "pro-48000-2-24-48000", "lossless-44100-2-16-44100" })
            {
                var path = Path.Combine(corpus, name, "fixture.wma");
                using var source = File.OpenRead(path);
                try { await old.InspectConversionAsync(Guid.NewGuid(), path, source, AudioFormat.Wave, default); throw new Exception("Unrepaired runtime admitted new codec."); }
                catch (NotSupportedException) { checks.Add("old runtime refuses newly admitted codec before native work: " + name); }
                var actual = await current.InspectConversionAsync(Guid.NewGuid(), path, source, AudioFormat.Wave, default);
                if (actual.ConversionBlockReason is not null) throw new Exception(actual.ConversionBlockReason);
                checks.Add("repaired runtime admits reviewed codec: " + name);
            }
            var voice = Path.Combine(corpus, "voice-8000-1-16-8000", "fixture.wma");
            using var input = File.OpenRead(voice);
            try { await current.InspectConversionAsync(Guid.NewGuid(), voice, input, AudioFormat.Wave, default); throw new Exception("FFmpeg Voice fallback admitted."); }
            catch (NotSupportedException) { checks.Add("repaired runtime still refuses Voice without the Windows provider"); }
        }
        RuntimeIdentityContracts.Run(repaired, previous, evidence, checks);
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} WMA runtime/precision checks. Evidence: {evidence}");
    }
}
