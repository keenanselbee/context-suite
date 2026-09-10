using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Audio;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--flac-preservation"))
    throw new ArgumentException("Expected verified payload directory, new repository-local scratch directory and optional --flac-preservation.");
var payload = Path.GetFullPath(args[0]);
var scratch = Path.GetFullPath(args[1]);
if (Directory.Exists(scratch)) throw new IOException("Evaluation scratch must be new; prior evidence is never overwritten.");
Directory.CreateDirectory(scratch);
var ffmpeg = Path.Combine(payload, "bin", "ffmpeg.exe");
var ffprobe = Path.Combine(payload, "bin", "ffprobe.exe");
var versions = await Run(ffmpeg, ["-version"]);
await File.WriteAllTextAsync(Path.Combine(scratch, "version.txt"), versions.Output + versions.Error);
if (args.Length == 3) return await VerifyFlacPreservation();
var encoders = await Run(ffmpeg, ["-hide_banner", "-encoders"]);
await File.WriteAllTextAsync(Path.Combine(scratch, "encoders.txt"), encoders.Output + encoders.Error);
var formats = new[] { "wav", "flac", "mp3", "m4a", "ogg", "opus" };
var tags = new Dictionary<string, string> { ["title"] = "Context Suite authored audio fixture",
    ["artist"] = "Context Suite tests", ["album"] = "Generated fixtures", ["comment"] = "Disposable local test" };
var original = Path.Combine(scratch, "authored-stereo.wav");
WriteWave(original);
var seeds = new Dictionary<string, string>();
var baselineSamples = new Dictionary<string, float[]>();
var rows = new List<object>();
var failures = 0;
foreach (var format in formats)
{
    var target = Path.Combine(scratch, "source." + format);
    var seedArgs = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
        "-i", original, "-map", "0:a:0", "-threads", "1" };
    seedArgs.AddRange(EncodingFor(format, true, seed: true));
    foreach (var tag in tags) seedArgs.AddRange(["-metadata", tag.Key + "=" + tag.Value]);
    seedArgs.Add(target);
    RequireSuccess(await Run(ffmpeg, seedArgs), "seed " + format);
    seeds.Add(format, target);
    baselineSamples.Add(format, await Decode(target, "source-" + format));
}
foreach (var source in formats)
{
    var before = Hash(seeds[source]);
    var sourceProbe = await Probe(seeds[source]);
    foreach (var targetFormat in formats)
    {
        var id = source + "-to-" + targetFormat;
        try
        {
            var target = Path.Combine(scratch, id + "." + targetFormat);
            var losslessSource = source is "wav" or "flac";
            var conversion = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
                "-i", seeds[source], "-map", "0:a:0", "-map_metadata", source is "ogg" or "opus" ? "0:s:a:0" : "0", "-threads", "1" };
            conversion.AddRange(EncodingFor(targetFormat, losslessSource));
            conversion.AddRange(["-fs", "16777216", target]);
            var process = await Run(ffmpeg, conversion);
            RequireSuccess(process, id);
            var probe = await Probe(target);
            var decoded = await Decode(target, id);
            var expected = baselineSamples[source];
            var count = Math.Min(expected.Length, decoded.Length);
            var squared = 0.0;
            var maxError = 0.0;
            for (var index = 0; index < count; index++)
            {
                var delta = Math.Abs((double)expected[index] - decoded[index]);
                if (!double.IsFinite(delta)) throw new InvalidDataException("Non-finite decoded sample.");
                squared += delta * delta;
                maxError = Math.Max(maxError, delta);
            }
            var rmse = Math.Sqrt(squared / Math.Max(1, count));
            var expectedCodec = targetFormat switch { "wav" => "pcm_f32le", "flac" => "flac", "mp3" => "mp3",
                "m4a" => "aac", "ogg" => "vorbis", "opus" => "opus", _ => throw new InvalidOperationException() };
            var exactRequired = targetFormat == "wav" || (targetFormat == "flac" && losslessSource);
            var precisionDecision = targetFormat == "flac" && !losslessSource;
            var tagLoss = sourceProbe.Tags.Where(tag => tags.ContainsKey(tag.Key) &&
                (!probe.Tags.TryGetValue(tag.Key, out var value) || value != tag.Value)).Select(tag => tag.Key).ToArray();
            var basicPass = probe.Codec == expectedCodec && probe.Channels == 2 && probe.SampleRate == 48000 &&
                decoded.Length > 0 && Math.Abs(decoded.Length - expected.Length) <= 4096 && rmse < 0.15 &&
                (!exactRequired || (decoded.Length == expected.Length && maxError == 0)) && Hash(seeds[source]) == before;
            var testedFidelityPass = basicPass && tagLoss.Length == 0;
            if (!testedFidelityPass) failures++;
            rows.Add(new { source, target = targetFormat, basicPass, testedFidelityPass, exactSamplesRequired = exactRequired,
                exactSamples = decoded.Length == expected.Length && maxError == 0, precisionDecisionRequired = precisionDecision,
                lossyToLossyConsentRequired = !losslessSource && targetFormat is not ("wav" or "flac"),
                sourceFrames = expected.Length / 2, decodedFrames = decoded.Length / 2, maxError, rmse,
                missingPreservedTags = tagLoss, probe, process.ElapsedMs, process.PeakWorkingSetBytes,
                outputBytes = new FileInfo(target).Length, inputSha256 = before, outputSha256 = Hash(target),
                arguments = conversion, diagnostics = process.Error });
            Console.WriteLine($"{id}: basic={(basicPass ? "pass" : "FAIL")}, frames={decoded.Length / 2}, exact={maxError == 0 && decoded.Length == expected.Length}, tagLoss={tagLoss.Length}");
        }
        catch (Exception exception)
        {
            failures++;
            rows.Add(new { source, target = targetFormat, basicPass = false, error = exception.Message });
            Console.WriteLine(id + ": FAIL " + exception.Message);
        }
    }
}
// A private application block is an authored preservation canary, not copied media.
var optimizationInput = Path.Combine(scratch, "low-compression.flac");
RequireSuccess(await Run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
    "-i", seeds["wav"], "-map", "0:a:0", "-map_metadata", "0", "-c:a", "flac", "-compression_level", "0", optimizationInput]), "FLAC optimize seed");
var lowBytes = await File.ReadAllBytesAsync(optimizationInput);
if (!lowBytes.AsSpan(0, 4).SequenceEqual("fLaC"u8) || (lowBytes[4] & 128) != 0 || lowBytes[7] != 34)
    throw new InvalidDataException("Unexpected native FLAC seed layout.");
var applicationBlock = new byte[] { 2, 0, 0, 8, (byte)'C', (byte)'S', (byte)'T', (byte)'F', 1, 2, 3, 4 };
await File.WriteAllBytesAsync(optimizationInput, lowBytes[..42].Concat(applicationBlock).Concat(lowBytes[42..]).ToArray());
var optimizationOutput = Path.Combine(scratch, "recompressed.flac");
RequireSuccess(await Run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
    "-i", optimizationInput, "-map", "0:a:0", "-map_metadata", "0", "-c:a", "flac", "-compression_level", "8", optimizationOutput]), "FLAC recompression");
var optimizationSamplesBefore = await Decode(optimizationInput, "optimize-before");
var optimizationSamplesAfter = await Decode(optimizationOutput, "optimize-after");
var optimizedBytes = await File.ReadAllBytesAsync(optimizationOutput);
var optimization = new { inputBytes = new FileInfo(optimizationInput).Length, outputBytes = optimizedBytes.Length,
    exactSamples = optimizationSamplesBefore.SequenceEqual(optimizationSamplesAfter),
    applicationBlockPreserved = HasApplicationBlock(optimizedBytes),
    inputSha256 = Hash(optimizationInput), outputSha256 = Hash(optimizationOutput),
    conclusion = "Sample equality alone cannot authorize optimization: all required metadata blocks must also survive." };
if (!optimization.exactSamples) failures++;
var metadataGaps = new Dictionary<string, string[]>();
foreach (var seed in seeds)
{
    var probe = await Probe(seed.Value);
    metadataGaps[seed.Key] = tags.Where(tag => !probe.Tags.TryGetValue(tag.Key, out var value) || value != tag.Value)
        .Select(tag => tag.Key).ToArray();
}
var report = new { schema = 1, utc = DateTime.UtcNow, machine = new { Environment.OSVersion.VersionString, Environment.ProcessorCount },
    scope = "Generated 48 kHz stereo tone/chirp fixture only; not listening, full metadata, worker, UI or release acceptance",
    ffmpegSha256 = Hash(ffmpeg), ffprobeSha256 = Hash(ffprobe), seedMetadataGaps = metadataGaps, failures, optimization, rows };
await File.WriteAllTextAsync(Path.Combine(scratch, "matrix.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Saved 36-pair evaluation with {failures} matrix-check failures: {scratch}");
return failures == 0 ? 0 : 1;

async Task<AudioProbe> Probe(string path)
{
    var result = await Run(ffprobe, ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_entries",
        "stream=codec_name,sample_rate,channels,bits_per_raw_sample:stream_tags:format=duration:format_tags", "-of", "json", path]);
    RequireSuccess(result, "probe");
    using var json = JsonDocument.Parse(result.Output);
    var streams = json.RootElement.GetProperty("streams");
    if (streams.GetArrayLength() != 1) throw new InvalidDataException("Expected exactly one audio stream.");
    var stream = streams[0];
    var collected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var section in new[] { json.RootElement.GetProperty("format"), stream })
        if (section.TryGetProperty("tags", out var objectTags))
            foreach (var tag in objectTags.EnumerateObject()) collected[tag.Name] = tag.Value.GetString() ?? "";
    return new(stream.GetProperty("codec_name").GetString()!, int.Parse(stream.GetProperty("sample_rate").GetString()!,
        System.Globalization.CultureInfo.InvariantCulture), stream.GetProperty("channels").GetInt32(), collected);
}

async Task<int> VerifyFlacPreservation()
{
    var wave = Path.Combine(scratch, "authored.wav");
    WriteWave(wave);
    var sourcePath = Path.Combine(scratch, "source.flac");
    var encodedPath = Path.Combine(scratch, "encoded.flac");
    var finalPath = Path.Combine(scratch, "reconciled.flac");
    RequireSuccess(await Run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
        "-i", wave, "-c:a", "flac", "-compression_level", "0", "-metadata", "title=Authored preservation test", sourcePath]), "FLAC source");
    var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
    var sourceHeader = FlacMetadata.Parse(sourceBytes);
    var blockOffset = 4;
    foreach (var block in sourceHeader.Blocks)
    {
        if (block.Type == 4 && block.Data.Length > 8 && block.Data[0] > 0)
            sourceBytes[blockOffset + 8] = (byte)'X'; // Distinct, valid original vendor string.
        blockOffset += 4 + block.Data.Length;
    }
    await File.WriteAllBytesAsync(sourcePath, sourceBytes);
    var sourceHash = Hash(sourcePath);
    sourceHeader = FlacMetadata.Parse(sourceBytes);
    RequireSuccess(await Run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
        "-i", sourcePath, "-map", "0:a:0", "-map_metadata", "0", "-c:a", "flac", "-compression_level", "8", encodedPath]), "FLAC encode");
    var encodedBytes = await File.ReadAllBytesAsync(encodedPath);
    var encodedHeader = FlacMetadata.Parse(encodedBytes);
    var rawMetadataRejected = false;
    try { FlacMetadata.RequirePreservedMetadata(sourceHeader, encodedHeader); }
    catch (InvalidDataException) { rawMetadataRejected = true; }
    var prefix = FlacMetadata.CreateRecompressionHeader(sourceHeader, encodedHeader);
    await File.WriteAllBytesAsync(finalPath, prefix.Concat(encodedBytes[encodedHeader.AudioOffset..]).ToArray());
    FlacMetadata.RequirePreservedMetadata(sourceHeader, FlacMetadata.Parse(await File.ReadAllBytesAsync(finalPath)));
    var before = await Decode(sourcePath, "before");
    var after = await Decode(finalPath, "after");
    var exact = before.SequenceEqual(after);
    var smaller = new FileInfo(finalPath).Length < sourceBytes.Length;
    var unchanged = Hash(sourcePath) == sourceHash;
    var canary = new byte[] { 2, 0, 0, 4, (byte)'C', (byte)'S', (byte)'T', (byte)'F' };
    var guardedBytes = sourceBytes[..42].Concat(canary).Concat(sourceBytes[42..]).ToArray();
    var guarded = FlacMetadata.Parse(guardedBytes);
    var applicationBlocked = false;
    try { FlacMetadata.CreateRecompressionHeader(guarded, encodedHeader); }
    catch (NotSupportedException) { applicationBlocked = true; }
    var result = new { schema = 1, utc = DateTime.UtcNow, rawMetadataRejected, exactDecodedSamples = exact,
        exactRequiredMetadata = true, smaller, originalUnchanged = unchanged, applicationBlocked,
        inputBytes = sourceBytes.Length, outputBytes = new FileInfo(finalPath).Length,
        inputSha256 = sourceHash, outputSha256 = Hash(finalPath), ffmpegSha256 = Hash(ffmpeg),
        scope = "Generated stereo PCM16 and comment/vendor byte preservation; not complete worker or all-metadata acceptance" };
    await File.WriteAllTextAsync(Path.Combine(scratch, "preservation.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    var passed = rawMetadataRejected && exact && smaller && unchanged && applicationBlocked;
    Console.WriteLine($"FLAC reconciliation {(passed ? "passed" : "FAILED")}: {sourceBytes.Length} -> {new FileInfo(finalPath).Length} bytes. Evidence: {scratch}");
    return passed ? 0 : 1;
}

async Task<float[]> Decode(string path, string name)
{
    var decoded = Path.Combine(scratch, name + ".f32");
    RequireSuccess(await Run(ffmpeg, ["-hide_banner", "-v", "error", "-nostdin", "-n", "-protocol_whitelist", "file,pipe",
        "-i", path, "-map", "0:a:0", "-threads", "1", "-c:a", "pcm_f32le", "-f", "f32le", "-fs", "16777216", decoded]), "decode");
    var bytes = await File.ReadAllBytesAsync(decoded);
    if (bytes.Length >= 16777216 || bytes.Length % 8 != 0) throw new InvalidDataException("Unexpected decoded output length.");
    var values = new float[bytes.Length / 4];
    Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
    return values;
}

static string[] EncodingFor(string format, bool losslessSource, bool seed = false)
{
    return format switch
    {
        "wav" => ["-c:a", seed ? "pcm_s16le" : "pcm_f32le"],
        "flac" => ["-c:a", "flac", "-compression_level", "8", "-sample_fmt", losslessSource ? "s16" : "s32",
            "-bits_per_raw_sample", losslessSource ? "16" : "24"],
        "mp3" => ["-c:a", "libmp3lame", "-q:a", "2"],
        "m4a" => ["-c:a", "aac", "-b:a", "192k"],
        "ogg" => ["-c:a", "libvorbis", "-q:a", "5"],
        "opus" => ["-c:a", "libopus", "-b:a", "160k", "-vbr", "on", "-application", "audio"],
        _ => throw new ArgumentException("Unknown evaluation format.")
    };
}

static bool HasApplicationBlock(byte[] bytes)
{
    for (var offset = 4; offset <= bytes.Length - 4;)
    {
        var length = (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        if (length > bytes.Length - offset - 4) return false;
        if ((bytes[offset] & 127) == 2 && length == 8 && bytes.AsSpan(offset + 4, 8).SequenceEqual("CSTF\x01\x02\x03\x04"u8)) return true;
        if ((bytes[offset] & 128) != 0) return false;
        offset += 4 + length;
    }
    return false;
}

static void WriteWave(string path)
{
    using var writer = new BinaryWriter(File.Create(path));
    const int frames = 96000;
    writer.Write("RIFF"u8); writer.Write(36 + frames * 4); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(48000); writer.Write(192000);
    writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(frames * 4);
    for (var index = 0; index < frames; index++)
    {
        var t = index / 48000.0;
        var envelope = Math.Min(1.0, Math.Min(index / 480.0, (frames - 1 - index) / 480.0));
        writer.Write((short)Math.Round(10000 * envelope * (0.7 * Math.Sin(2 * Math.PI * 440 * t) + 0.3 * Math.Sin(2 * Math.PI * (1200 * t + 300 * t * t)))));
        writer.Write((short)Math.Round(9000 * envelope * Math.Sin(2 * Math.PI * 659.25 * t)));
    }
}

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static void RequireSuccess(ProcessResult result, string operation)
{
    if (result.ExitCode != 0) throw new InvalidDataException(operation + " failed: " + result.Error);
}

static async Task<ProcessResult> Run(string executable, IEnumerable<string> arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = new Process { StartInfo = start };
    var timer = Stopwatch.StartNew();
    if (!process.Start()) throw new IOException("Process did not start.");
    var handle = process.Handle;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var output = ReadBounded(process.StandardOutput, timeout.Token);
    var error = ReadBounded(process.StandardError, timeout.Token);
    try
    {
        await process.WaitForExitAsync(timeout.Token);
        var counters = new ProcessMemory.Counters { Size = (uint)Marshal.SizeOf<ProcessMemory.Counters>() };
        long? peak = ProcessMemory.GetProcessMemoryInfo(handle, out counters, counters.Size) ? (long)counters.PeakWorkingSetSize : null;
        return new(process.ExitCode, await output, await error, timer.ElapsedMilliseconds, peak);
    }
    finally
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
    }
}

static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
{
    var text = new StringBuilder();
    var buffer = new char[4096];
    int count;
    while ((count = await reader.ReadAsync(buffer, token)) != 0)
    {
        if (text.Length + count > 1048576) throw new InvalidDataException("Engine diagnostic limit exceeded.");
        text.Append(buffer, 0, count);
    }
    return text.ToString();
}

internal sealed record AudioProbe(string Codec, int SampleRate, int Channels, Dictionary<string, string> Tags);
internal sealed record ProcessResult(int ExitCode, string Output, string Error, long ElapsedMs, long? PeakWorkingSetBytes);

internal static class ProcessMemory
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Counters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMemoryInfo(IntPtr process, out Counters counters, uint size);
}
