using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Audio;

internal static class ExpandedAudioRefusalContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence already exists.");
        Directory.CreateDirectory(evidence);
        using var adapter = new AudioEncodingAdapter(binary, Path.Combine(evidence, "scratch"));
        var results = new List<object>();
        foreach (var path in Directory.EnumerateFiles(fixtures).Where(path => Path.GetExtension(path) is ".aif" or ".aifc").Order())
            foreach (var target in new[] { AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
            {
                using var input = File.OpenRead(path);
                try
                {
                    using var artifact = await adapter.ConvertFileAsync(input, target,
                        AudioConversionConsent.LossyTranscoding | AudioConversionConsent.PrecisionReduction | AudioConversionConsent.Resampling, default);
                    results.Add(new { name = Path.GetFileName(path), target = target.ToString(), passed = true, reason = (string?)null });
                }
                catch (ImageFailureException error) when (error.Failure == ImageFailure.ValidationFailed)
                {
                    results.Add(new { name = Path.GetFileName(path), target = target.ToString(), passed = false, reason = error.Message });
                    Console.WriteLine(Path.GetFileName(path) + " / " + target + ": " + error.Message);
                }
                await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
        Console.WriteLine($"Observed {results.Count} expanded audio lossy cells. Evidence: {evidence}");
    }
}
