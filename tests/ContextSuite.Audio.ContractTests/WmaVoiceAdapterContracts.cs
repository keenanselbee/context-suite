using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class WmaVoiceAdapterContracts
{
    public static async Task RunAsync(string binary, string voiceDirectory, string corpus, string evidence, string independent)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence already exists.");
        Directory.CreateDirectory(evidence);
        var scratch = Path.Combine(evidence, "scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch, voiceDirectory);
        using var supplierLease = new AudioEncodingAdapter(independent, Path.Combine(evidence, "supplier-lease"));
        using var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "results.json")));
        var voices = rows.RootElement.EnumerateArray().Where(row => row.GetProperty("codec").GetString() == "voice").ToArray();
        if (voices.Length != 36) throw new InvalidDataException("Expected the complete finite Voice corpus.");
        var results = new List<object>(); var checks = new List<string>();
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.PrecisionReduction | AudioConversionConsent.Resampling;
        foreach (var row in voices)
        {
            var name = row.GetProperty("name").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid fixture name.");
            var path = Path.Combine(corpus, name, "fixture.wma");
            var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (sourceHash != row.GetProperty("wmaSha256").GetString()) throw new InvalidDataException("Fixture changed.");
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                using var source = File.OpenRead(path);
                var inspection = await adapter.InspectConversionAsync(Guid.NewGuid(), path, source, target, default);
                if (inspection.ConversionBlockReason is not null) throw new Exception(inspection.ConversionBlockReason);
                source.Position = 3;
                using (var artifact = await adapter.ConvertFileAsync(source, target, consent, default))
                {
                    var result = artifact.Result;
                    if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.Plan.AlreadyTarget || source.Position != 3)
                        throw new Exception("Voice metadata, conversion or caller-position contract failed.");
                    var output = Path.Combine(evidence, name + result.Plan.Extension);
                    using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await artifact.Content.CopyToAsync(writer);
                    var frames = row.GetProperty("sourceFrames").GetInt64();
                    var rate = row.GetProperty("rate").GetInt32();
                    if (target == AudioFormat.Opus)
                    {
                        if (result.DecodedFrames < frames * 48000 / rate || result.DecodedFrames > (frames * 48000 + rate - 1) / rate)
                            throw new Exception("Voice resampling changed the expected extent.");
                    }
                    else if (result.DecodedFrames != frames) throw new Exception("Voice conversion changed its sample extent.");
                    if (target is AudioFormat.Wave or AudioFormat.Flac)
                    {
                        using var outputInput = File.OpenRead(output);
                        var decoded = await SeekableMediaProcess.RunAsync(Path.Combine(binary, "ffmpeg.exe"), evidence, outputInput,
                            ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0", "-threads", "1", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 2 * 1024 * 1024, default);
                        var reference = File.ReadAllBytes(Path.Combine(corpus, name, "windows.f32"));
                        if (decoded.Length != reference.Length * 2) throw new Exception("Lossless output extent differs from Windows Voice.");
                        for (var offset = 0; offset < reference.Length; offset += 4)
                            if (BitConverter.ToDouble(decoded, offset * 2) != BitConverter.ToSingle(reference, offset))
                                throw new Exception("Lossless output differs from retained Windows Voice samples.");
                        checks.Add("exact Windows-reference samples: " + name + " / " + target);
                    }
                    checks.Add("six-target validated copy candidate: " + name + " / " + target);
                    results.Add(new { name, target = target.ToString(), result.DecodedFrames, result.MaximumSampleError, result.RootMeanSquareError });
                }
                if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Voice scratch was not recovered.");
            }
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != sourceHash) throw new Exception("Voice original changed.");
            checks.Add("source hash and per-output scratch preserved: " + name);
            await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { checks, results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(name + ": six targets passed");
        }
        var control = Path.Combine(corpus, "voice-16000-1-16-16000", "fixture.wma");
        var described = WmaInputContracts.VoiceMetadataFixture(File.ReadAllBytes(control));
        var describedPath = Path.Combine(evidence, "voice-metadata.wma"); File.WriteAllBytes(describedPath, described);
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
        {
            using var source = File.OpenRead(describedPath);
            using (var artifact = await adapter.ConvertFileAsync(source, target, consent, default))
            {
                var result = artifact.Result;
                var tags = result.Facts.Tags.Concat(result.Facts.Streams.SelectMany(stream => stream.Tags)).ToArray();
                foreach (var tag in new Dictionary<string, string> { ["title"] = "Voice title \u00fc", ["artist"] = "Voice artist \u4e2d",
                    ["album"] = "Voice album", ["date"] = "2026", ["track"] = "2", ["language"] = "en-us" })
                    if (!tags.Any(actual => actual.Key.Equals(tag.Key, StringComparison.OrdinalIgnoreCase) && actual.Value == tag.Value))
                        throw new Exception("Voice descriptive value was lost: " + tag.Key + " / " + target);
                var output = Path.Combine(evidence, "voice-metadata" + result.Plan.Extension);
                using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await artifact.Content.CopyToAsync(writer);
                using var encoded = File.OpenRead(output);
                var pixels = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, encoded,
                    ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:v:0", "-frames:v", "1", "-threads", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], 16, default);
                if (!pixels.AsSpan().SequenceEqual(new byte[] { 255, 0, 128, 255, 0, 255, 0, 128 })) throw new Exception("Voice artwork pixels changed.");
                checks.Add("Unicode tags, regional language and exact artwork pixels: " + target);
                results.Add(new { name = "voice-metadata", target = target.ToString(), result.DecodedFrames, result.MaximumSampleError, result.RootMeanSquareError });
            }
            if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Voice metadata scratch survived disposal.");
        }
        using (var absent = new AudioEncodingAdapter(binary, Path.Combine(evidence, "without-voice")))
        {
            using var source = File.OpenRead(control);
            try { await absent.ConvertFileAsync(source, AudioFormat.Wave, consent, default); throw new Exception("Absent Voice provider admitted input."); }
            catch (NotSupportedException error) when (error.Message.Contains("verified Windows Voice decoder", StringComparison.Ordinal))
            { checks.Add("absent provider refuses Voice without fallback"); }
        }
        using (var source = File.OpenRead(control))
        using (var artifact = await adapter.ConvertFileAsync(source, AudioFormat.Flac, AudioConversionConsent.None, default))
        {
            if (!artifact.Result.ExactSamples || artifact.Result.Plan.RequiredConsent != AudioConversionConsent.None)
                throw new Exception("Voice PCM16 was treated as a lossy precision reduction.");
            checks.Add("PCM16 Voice to fixed FLAC preserves exact samples without unnecessary precision consent");
        }
        foreach (var target in new[] { AudioFormat.Mp3, AudioFormat.Opus })
        {
            using var source = File.OpenRead(control);
            try { await adapter.ConvertFileAsync(source, target, AudioConversionConsent.None, default); throw new Exception("Voice quality consent bypassed."); }
            catch (InvalidOperationException) { checks.Add("quality consent retained: " + target); }
        }
        using (var source = File.OpenRead(control))
        {
            try { await adapter.ConvertFileAsync(source, AudioFormat.Wave, consent, new CancellationToken(true)); throw new Exception("Voice pre-cancellation ignored."); }
            catch (OperationCanceledException) { checks.Add("adapter pre-cancellation"); }
        }
        if (!File.ReadAllBytes(describedPath).AsSpan().SequenceEqual(described) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new Exception("Voice metadata source or scratch changed.");
        checks.Add("metadata source and failure cleanup preserved");
        var changedDirectory = Path.Combine(evidence, "changed-helper"); Directory.CreateDirectory(changedDirectory);
        var changedHelper = Path.Combine(changedDirectory, "WmaVoiceMftProbe.exe");
        var helperBytes = File.ReadAllBytes(Path.Combine(voiceDirectory, "WmaVoiceMftProbe.exe")); helperBytes[^1] ^= 1;
        File.WriteAllBytes(changedHelper, helperBytes);
        try { using var changed = new WindowsVoiceDecoder(changedDirectory); throw new Exception("Changed helper admitted."); }
        catch (InvalidDataException) { checks.Add("changed helper identity refused before execution"); }
        try { using var missing = new WindowsVoiceDecoder(Path.Combine(evidence, "missing-helper")); throw new Exception("Missing helper admitted."); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or System.ComponentModel.Win32Exception)
        { checks.Add("missing helper refused without fallback"); }
        try
        {
            using var write = new FileStream(Path.Combine(voiceDirectory, "WmaVoiceMftProbe.exe"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            throw new Exception("Pinned helper could be opened for writing.");
        }
        catch (IOException) { checks.Add("pinned helper read lease prevents concurrent mutation"); }
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { checks, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Voice conversion checks with {results.Count} outputs. Evidence: {evidence}");
    }
}
