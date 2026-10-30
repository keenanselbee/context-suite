using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class WmaVoiceDurationContracts
{
    public static async Task RunAsync(string binary, string helper, string corpus, string evidence)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence already exists.");
        Directory.CreateDirectory(evidence);
        var checks = new List<string>(); var results = new List<object>();
        await StreamingAsync(checks);
        using var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "results.json")));
        var rates = new[] { 8000, 11025, 16000, 22050 };
        if (rows.RootElement.GetArrayLength() != rates.Length) throw new Exception("Expected all four duration profiles.");
        var scratch = Path.Combine(evidence, "scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch, helper);
        using var decoder = new WindowsVoiceDecoder(helper);
        foreach (var rate in rates)
        {
            var row = rows.RootElement.EnumerateArray().Single(value => value.GetProperty("sampleRate").GetInt32() == rate);
            var frames = rate * 300L + 1;
            var name = $"voice-{rate}-five-minute-tail";
            if (row.GetProperty("name").GetString() != name || !row.GetProperty("passed").GetBoolean() || row.GetProperty("sourceFrames").GetInt64() != frames)
                throw new Exception("Unexpected duration fixture contract.");
            var sourcePath = Path.Combine(corpus, name, "fixture.wma");
            var referencePath = Path.Combine(corpus, name, "windows.f32");
            string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
            if (Hash(sourcePath) != row.GetProperty("fixture.wma").GetProperty("sha256").GetString() ||
                Hash(referencePath) != row.GetProperty("windows.f32").GetProperty("sha256").GetString())
                throw new Exception("Duration fixture identity changed.");
            var sourceHash = Hash(sourcePath);
            using var source = File.OpenRead(sourcePath);
            var inventory = await AsfMetadata.ReadAsync(source, default);
            if (rate == rates[0])
            {
                var malformed = Path.Combine(evidence, "malformed.wma");
                var failedOutput = Path.Combine(evidence, "failed.wav");
                await File.WriteAllBytesAsync(malformed, new byte[32]);
                using var bad = File.OpenRead(malformed);
                try { await decoder.DecodeWaveAsync(bad, inventory, failedOutput, default); throw new Exception("Malformed native input admitted."); }
                catch (InvalidDataException)
                {
                    if (File.Exists(failedOutput)) throw new Exception("Failed native decode retained its partial output.");
                    checks.Add("native failure removes only its newly reserved output; subsequent same-provider decode recovers");
                }
            }
            var wave = Path.Combine(evidence, name + "-decoded.wav");
            var timer = Stopwatch.StartNew();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var decodedFrames = await decoder.DecodeWaveAsync(source, inventory, wave, deadline.Token);
            var decodeSeconds = timer.Elapsed.TotalSeconds;
            if (decodedFrames != frames) throw new Exception("Voice streaming lost duration.");
            using (var actual = File.OpenRead(wave))
            using (var reference = File.OpenRead(referencePath))
            {
                var header = new byte[80]; await actual.ReadExactlyAsync(header);
                if (!header.SequenceEqual(DecodedAudioWave.Header(frames * 8, rate, 1, "mono"))) throw new Exception("Final WAV header differs.");
                var input = new byte[32768]; var output = new byte[65536];
                long remaining = frames;
                while (remaining != 0)
                {
                    var count = (int)Math.Min(input.Length / 4, remaining);
                    await reference.ReadExactlyAsync(input.AsMemory(0, count * 4));
                    await actual.ReadExactlyAsync(output.AsMemory(0, count * 8));
                    for (var i = 0; i < count; i++)
                        if (BinaryPrimitives.ReadDoubleLittleEndian(output.AsSpan(i * 8)) != BinaryPrimitives.ReadSingleLittleEndian(input.AsSpan(i * 4)))
                            throw new Exception("Long Voice stream differs from Windows reference.");
                    remaining -= count;
                }
                if (actual.Position != actual.Length || reference.Position != reference.Length) throw new Exception("Long reference extent changed.");
            }
            checks.Add(name + ": exact streamed Windows reference and finalized WAV header");
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                source.Position = 3; timer.Restart();
                using (var artifact = await adapter.ConvertFileAsync(source, target,
                    AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling, default))
                {
                    var result = artifact.Result;
                    if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || source.Position != 3 ||
                        (target is AudioFormat.Wave or AudioFormat.Flac && !result.ExactSamples))
                        throw new Exception("Duration conversion lost metadata, caller position or exact samples.");
                    var minimum = target == AudioFormat.Opus ? frames * 48000 / rate : frames;
                    var maximum = target == AudioFormat.Opus ? (frames * 48000 + rate - 1) / rate : frames;
                    if (result.DecodedFrames is null || result.DecodedFrames < minimum || result.DecodedFrames > maximum)
                        throw new Exception("Long conversion changed sample extent.");
                    var output = Path.Combine(evidence, name + result.Plan.Extension);
                    using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await artifact.Content.CopyToAsync(file);
                    results.Add(new { name, target = target.ToString(), frames = result.DecodedFrames,
                        seconds = timer.Elapsed.TotalSeconds, decodeSeconds, bytes = new FileInfo(output).Length, sha256 = Hash(output) });
                }
                if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Duration conversion left scratch files.");
                checks.Add(name + ": validated conversion and cleanup to " + target);
                Console.WriteLine(name + ": " + target + " passed");
            }
            if (Hash(sourcePath) != sourceHash) throw new Exception("Original duration fixture changed.");
            checks.Add(name + ": original unchanged");
            await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { checks, results },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"Passed {checks.Count} Voice streaming/duration checks with {results.Count} conversions. Evidence: {evidence}");
    }

    private static async Task StreamingAsync(List<string> checks)
    {
        byte[] Transport(params float[] samples)
        {
            var bytes = new byte[24 + samples.Length * 4]; "CSVOICE1"u8.CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8000);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 1);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 32);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 16);
            for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(24 + i * 4), samples[i]);
            return bytes;
        }
        var valid = Transport(-1, -1f / 32768, 0, 32767f / 32768);
        foreach (var chunk in new[] { 1, 2, 3, 5, 7, 24, 65536 })
        {
            using var input = new FragmentedStream(valid, chunk); using var output = new MemoryStream();
            if (await WindowsVoiceDecoder.CaptureWaveAsync(input, output, 8000, 32, default) != 4 || output.Length != 112)
                throw new Exception("Streaming sample boundary changed extent.");
            var bytes = output.ToArray();
            for (var i = 0; i < 4; i++)
                if (BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(80 + i * 8)) != BinaryPrimitives.ReadSingleLittleEndian(valid.AsSpan(24 + i * 4)))
                    throw new Exception("Streaming sample boundary changed value.");
            checks.Add("transport fragment size " + chunk + ": exact samples at budget boundary");
        }
        foreach (var bad in new[] { valid[..23], valid[..^1], Transport(), Transport(float.NaN), Transport(float.PositiveInfinity), Transport(1), Transport(.00001f) })
        {
            using var input = new FragmentedStream(bad, 3); using var output = new MemoryStream();
            try { await WindowsVoiceDecoder.CaptureWaveAsync(input, output, 8000, 32, default); throw new Exception("Invalid transport admitted."); }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            { checks.Add("truncated, empty or invalid sample transport refused"); }
        }
        foreach (var offset in new[] { 0, 8, 12, 16, 20 })
        {
            var wrong = valid.ToArray(); wrong[offset] ^= 1;
            using var input = new FragmentedStream(wrong, 3);
            try { await WindowsVoiceDecoder.CaptureWaveAsync(input, Stream.Null, 8000, 32, default); throw new Exception("Incorrect transport profile admitted."); }
            catch (InvalidDataException) { checks.Add("incorrect transport profile field refused: " + offset); }
        }
        using (var input = new FragmentedStream(Transport(Enumerable.Repeat(.5f, 17001).ToArray()), 65535))
        using (var output = new MemoryStream())
        {
            if (await WindowsVoiceDecoder.CaptureWaveAsync(input, output, 8000, 17001 * 8, default) != 17001)
                throw new Exception("Cross-buffer sample count changed.");
            var bytes = output.ToArray();
            for (var offset = 80; offset < bytes.Length; offset += 8)
                if (BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset)) != .5)
                    throw new Exception("Cross-buffer sample value changed.");
            checks.Add("sample split across 64 KiB buffer preserved exactly");
        }
        using (var input = new FragmentedStream(valid, 3))
        {
            try { await WindowsVoiceDecoder.CaptureWaveAsync(input, Stream.Null, 8000, 24, default); throw new Exception("Decoded budget ignored."); }
            catch (InvalidDataException) { checks.Add("one-sample decoded-budget excess refused"); }
        }
        using (var input = new MemoryStream())
        using (var output = new MemoryStream())
        {
            if (await WindowsVoiceDecoder.CaptureWaveAsync(input, output, 8000, 32, default) != 0 || output.Length != 0)
                throw new Exception("Empty child output must defer exit classification without writing a file header.");
            checks.Add("empty child output defers exit-status classification");
        }
    }

    private sealed class FragmentedStream(byte[] bytes, int chunk) : MemoryStream(bytes, false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
    }
}
