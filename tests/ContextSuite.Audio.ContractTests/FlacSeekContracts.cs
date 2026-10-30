using ContextSuite.Private.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FlacSeekContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var scratch = Path.Combine(evidence, "seek-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        var path = Path.Combine(evidence, "authored-seek-base.flac");
        using (var fixture = new FileStream(Path.Combine(fixtures, "source.flac"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await SeekableMediaProcess.RunAsync(Path.Combine(binary, "ffmpeg.exe"), evidence, fixture,
                ["-v", "error", "-nostdin", "-n", "-protocol_whitelist", "fd", "-format_whitelist", "flac", "-i", "fd:",
                    "-map", "0:a:0", "-threads", "1", "-c:a", "flac", "-compression_level", "0", "-frame_size", "1024", "-f", "flac", path], 65536, default);
        var original = await File.ReadAllBytesAsync(path);
        var header = FlacMetadata.Parse(original);
        var packetJson = "{\"streams\":[{\"codec_name\":\"flac\",\"time_base\":\"1/48000\"}],\"packets\":[" +
            "{\"pts\":0,\"duration\":48000,\"pos\":\"" + header.AudioOffset + "\",\"size\":\"20\"}," +
            "{\"pts\":48000,\"duration\":48000,\"pos\":\"" + (header.AudioOffset + 20) + "\",\"size\":\"20\"}]}";
        var parsed = FlacFrameProbe.Parse(System.Text.Encoding.UTF8.GetBytes(packetJson), header, header.AudioOffset + 40);
        if (parsed.Length != 2 || parsed[1].Sample != 48000) throw new Exception("Packet index did not retain sample-clock facts.");
        checks.Add("FLAC packet index: mixed numeric/string fields retain sample-clock positions");
        foreach (var malformedJson in new[] { "{}", "[]", "{", packetJson.Replace("1/48000", "1/1000"), packetJson.Replace("\"flac\"", "\"mp3\""),
            packetJson.Replace("\"pts\":48000", "\"pts\":48001"), packetJson.Replace("\"duration\":48000", "\"duration\":1.5"),
            packetJson.Replace("\"size\":\"20\"", "\"size\":\"9223372036854775808\""), packetJson.Replace("\"pts\":0", "\"pts\":-1") })
        {
            try { FlacFrameProbe.Parse(System.Text.Encoding.UTF8.GetBytes(malformedJson), header, header.AudioOffset + 40); throw new Exception("Malformed packet index accepted."); }
            catch (InvalidDataException) { checks.Add("FLAC packet index: malformed clock/number/structure rejected"); }
        }
        try { FlacFrameProbe.Parse(new byte[FlacFrameProbe.MaximumJsonBytes + 1], header, header.AudioOffset + 40); throw new Exception("Oversized packet index accepted."); }
        catch (InvalidDataException) { checks.Add("FLAC packet index: byte budget enforced"); }
        using var probe = new AudioProbeAdapter(binary);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var frames = await probe.ReadFlacFramesAsync(input, header, evidence, default);
        var selected = frames.Where((_, index) => index % 3 == 0).ToArray();
        var table = new byte[(selected.Length + 2) * 18];
        for (var index = 0; index < selected.Length; index++)
        {
            var frame = selected[index];
            BinaryPrimitives.WriteUInt64BigEndian(table.AsSpan(index * 18), (ulong)frame.Sample);
            BinaryPrimitives.WriteUInt64BigEndian(table.AsSpan(index * 18 + 8), (ulong)frame.Offset);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(index * 18 + 16), (ushort)frame.Samples);
        }
        for (var index = selected.Length; index < selected.Length + 2; index++) BinaryPrimitives.WriteUInt64BigEndian(table.AsSpan(index * 18), ulong.MaxValue);
        var seekBlock = new byte[4 + table.Length]; seekBlock[0] = 3; seekBlock[2] = (byte)(table.Length >> 8); seekBlock[3] = (byte)table.Length; table.CopyTo(seekBlock, 4);
        var padding = new byte[4 + 262144]; padding[0] = 1; padding[1] = 4;
        var source = original[..42].Concat(seekBlock).Concat(padding).Concat(original[42..]).ToArray();
        var sourcePath = Path.Combine(evidence, "authored-seektable.flac");
        await File.WriteAllBytesAsync(sourcePath, source);
        var sourceHash = SHA256.HashData(source);
        var result = await adapter.OptimizeFlacAsync(source, default);
        var outputPath = Path.Combine(evidence, "optimized-seektable.flac");
        await File.WriteAllBytesAsync(outputPath, result.Bytes);
        using var output = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var outputHeader = FlacMetadata.Parse(result.Bytes);
        var outputFrames = await probe.ReadFlacFramesAsync(output, outputHeader, evidence, default);
        var outputTable = outputHeader.Blocks.Single(block => block.Type == 3).Data;
        FlacSeekTable.RequireMatches(outputTable.AsSpan(), outputFrames);
        FlacMetadata.RequirePreservedMetadata(FlacMetadata.Parse(source), outputHeader, outputFrames);
        if (!result.ExactSamples || result.Bytes.Length >= source.Length || outputTable.AsSpan().SequenceEqual(table))
            throw new Exception("FLAC seek table did not rebuild offsets while retaining smaller exact audio.");
        checks.Add("FLAC seeking: rebuilt table matches independently reprobed output frames and retained metadata");
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reference = await Decode(sourceStream, null);
        foreach (var sample in new[] { 0, 6000, 24000, 86400 })
        {
            var actual = await Decode(output, sample);
            var expected = reference.AsSpan(sample * 16, 2400 * 16).ToArray();
            if (!actual.SequenceEqual(expected)) throw new Exception("Seeked output does not match the linear sample reference: " + sample);
            checks.Add("FLAC seeking: seeked decode matches linear source at sample " + sample);
        }
        var stale = source.ToArray(); stale[42 + 4 + 18 + 15] ^= 1;
        try { await adapter.OptimizeFlacAsync(stale, default); throw new Exception("Stale source seek offset was accepted."); }
        catch (InvalidDataException) { checks.Add("FLAC seeking: stale source offset prevents rewriting"); }
        var corrupt = source.ToArray(); corrupt[^1] ^= 1;
        try { await adapter.OptimizeFlacAsync(corrupt, default); throw new Exception("Frame CRC corruption was accepted."); }
        catch (InvalidDataException) { checks.Add("FLAC seeking: corrupted frame checksum prevents rewriting"); }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)).SequenceEqual(sourceHash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new Exception("FLAC seek work changed its original or left scratch.");
        checks.Add("FLAC seeking: source digest and cleanup survive successful and refused operations");
        reports.Add(new { operation = "flac-seektable", sourceBytes = source.Length, outputBytes = result.Bytes.Length,
            sourceFrames = frames.Length, outputFrames = outputFrames.Length, seekSlots = table.Length / 18,
            samplesVerified = new[] { 0, 6000, 24000, 86400 }, result.ExactSamples });

        async Task<byte[]> Decode(FileStream file, int? sample)
        {
            file.Position = 0;
            var arguments = new List<string> { "-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "flac" };
            if (sample is not null) arguments.AddRange(["-ss", (sample.Value / 48000m).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            arguments.AddRange(["-i", "fd:", "-map", "0:a:0", "-threads", "1"]);
            if (sample is not null) arguments.AddRange(["-t", "0.05"]);
            arguments.AddRange(["-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"]);
            return await SeekableMediaProcess.RunAsync(Path.Combine(binary, "ffmpeg.exe"), evidence, file, arguments, 2 * 1024 * 1024, default);
        }
    }
}
