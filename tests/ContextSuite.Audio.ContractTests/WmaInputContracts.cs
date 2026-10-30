using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class WmaInputContracts
{
    internal static async Task RunAsync(string binary, string fixtures, string evidence, string independent)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(evidence); Console.WriteLine("WMA adapter evidence: " + evidence);
        var scratch = Path.Combine(evidence, "scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        using var decoderLease = new AudioEncodingAdapter(independent, Path.Combine(evidence, "decoder-lease"));
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.PrecisionReduction | AudioConversionConsent.Resampling;
        var checks = new List<string>(); var results = new List<object>(); var failures = new List<string>();
        foreach (var (channels, layout) in new (int, string?)[] { (1, null), (2, null), (3, "2.1"), (4, "quad"), (4, "4.0"),
            (5, "5.0"), (5, "5.0(side)"), (6, "5.1"), (6, "5.1(side)"), (7, "6.1"), (8, "7.1") })
        {
            var samples = new byte[channels * 8];
            using var stream = new MemoryStream(DecodedAudioWave.Header(samples.Length, 48000, channels, layout).Concat(samples).ToArray());
            var wave = await WaveMetadata.ReadAsync(stream, default, true);
            Check(wave.SampleFrames == 1 && wave.Channels == channels && wave.SampleBits == 64 && wave.FloatingPoint,
                "Exact decoded transport extent and format " + channels + " / " + layout);
        }
        var maximumHeader = DecodedAudioWave.Header(AudioEncodingAdapter.MaximumDecodedBytes, 48000, 2, null);
        Check(maximumHeader.Length == 80 && BinaryPrimitives.ReadUInt32LittleEndian(maximumHeader.AsSpan(76)) == AudioEncodingAdapter.MaximumDecodedBytes,
            "Decoded transport maximum does not allocate samples");
        foreach (var bytes in new[] { 0L, 15L, AudioEncodingAdapter.MaximumDecodedBytes + 16 })
        {
            try { DecodedAudioWave.Header(bytes, 48000, 2, null); Check(false, "Invalid decoded extent admitted"); }
            catch (InvalidDataException) { Check(true, "Invalid decoded transport extent refused " + bytes); }
        }
        var tags = new Dictionary<string, string> { ["title"] = "Title \u00fc", ["artist"] = "Artist \u4e2d", ["album"] = "Album", ["date"] = "2026", ["track"] = "2" };
        var metadata = Descriptors(("Title", (ushort)0, Text(tags["title"])), ("Author", (ushort)0, Text(tags["artist"])),
            ("WM/AlbumTitle", (ushort)0, Text(tags["album"])), ("WM/Year", (ushort)0, Text(tags["date"])), ("WM/TrackNumber", (ushort)3, BitConverter.GetBytes(2u)));
        var pixels = new byte[] { 255, 0, 128, 255, 0, 255, 0, 128 };
        var png = FlacToOggArtworkContracts.Png(2, 1, pixels);
        var picture = new byte[] { 0 }.Concat(BitConverter.GetBytes((uint)png.Length)).Concat(Text("image/png")).Concat(Text("")).Concat(png).ToArray();
        foreach (var codec in new[] { "wmav1", "wmav2" })
        {
            var original = await File.ReadAllBytesAsync(Path.Combine(fixtures, codec + ".wma"));
            // The original generated file has an encoder-only Extended Content
            // object. Replace that provenance object, preserving source packets.
            var text = ReplaceExtended(original, metadata);
            var pictured = ReplaceExtended(original, metadata.AsSpan(2).ToArray(), picture);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                await Verify(original, codec + "-plain", target, null, null);
                await Verify(text, codec + "-text", target, tags, null);
                await Verify(pictured, codec + "-picture", target, tags, pixels);
            }
            foreach (var protection in new[] { "2211b3fb-bd23-11d2-b4b7-00a0c955fc6e", "298ae614-2622-4c17-b935-dae07ee9289c", "43058533-6981-49e6-9b74-ad12cb86d58c" })
            {
                var protectedBytes = AppendHeader(original, Object(protection, []));
                try { await adapter.ConvertAsync(protectedBytes, AudioFormat.Wave, consent, default); failures.Add("Protection admitted " + protection); }
                catch (NotSupportedException error) { Check(error.Message.Contains("Protected WMA"), "Managed protection refusal before native conversion " + codec + " / " + protection); }
                Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "Protected WMA cleanup " + codec);
            }
            foreach (var target in new[] { AudioFormat.Mp3, AudioFormat.Flac })
            {
                try { await adapter.ConvertAsync(original, target, AudioConversionConsent.None, default); failures.Add("Missing quality consent admitted " + codec + target); }
                catch (InvalidOperationException) { Check(true, "WMA quality consent remains required " + codec + " / " + target); }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new { checks, results, failures }, new JsonSerializerOptions { WriteIndented = true }));
        if (failures.Count != 0) throw new InvalidDataException(string.Join("\n", failures));
        Console.WriteLine($"Passed {checks.Count} WMA adapter checks with {results.Count} outputs: {evidence}");

        async Task Verify(byte[] bytes, string name, AudioFormat target, Dictionary<string, string>? expected, byte[]? expectedPixels)
        {
            var label = name + "-" + target;
            var path = Path.Combine(evidence, label + ".wma"); await File.WriteAllBytesAsync(path, bytes);
            var hash = SHA256.HashData(bytes); var time = File.GetLastWriteTimeUtc(path);
            try
            {
                using var source = File.OpenRead(path);
                var plan = await adapter.InspectConversionAsync(Guid.NewGuid(), path, source, target, default);
                Check(plan.ConversionBlockReason is null, "WMA inspection " + label);
                using var result = await adapter.ConvertFileAsync(source, target, consent, default);
                var info = result.Result;
                Check(info.SourceMetadataVerified && info.MissingTags.IsEmpty && !info.Plan.AlreadyTarget, "WMA metadata verification " + label);
                var output = Path.Combine(evidence, label + info.Plan.Extension);
                using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await result.Content.CopyToAsync(writer);
                if (expected is not null)
                {
                    var actual = info.Facts.Tags.Concat(info.Facts.Streams.Where(stream => stream.Kind == "audio").SelectMany(stream => stream.Tags));
                    Check(expected.All(tag => actual.Any(value => value.Key.Equals(tag.Key, StringComparison.OrdinalIgnoreCase) && value.Value == tag.Value)), "WMA text mapping " + label);
                }
                var reference = await Decode(path); var decoded = await Decode(output);
                Check(reference.Length > 0 && reference.Length == decoded.Length, "WMA supplier decoded extent " + label);
                double maximum = 0, sum = 0;
                for (var offset = 0; offset < reference.Length; offset += 8)
                {
                    var error = Math.Abs(BitConverter.ToDouble(reference, offset) - BitConverter.ToDouble(decoded, offset));
                    maximum = Math.Max(maximum, error); sum += error * error;
                }
                var rms = Math.Sqrt(sum / (reference.Length / 8));
                Check(double.IsFinite(maximum) && (target == AudioFormat.Wave ? maximum == 0 : target == AudioFormat.Flac ? maximum <= 1.0 / 8388608 : rms <= .02), "WMA supplier sample comparison " + label);
                if (expectedPixels is not null)
                {
                    using var input = File.OpenRead(output);
                    var actual = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, input,
                        ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:v:0", "-frames:v", "1", "-threads", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], 16, default);
                    Check(actual.AsSpan().SequenceEqual(expectedPixels), "WMA artwork pixels " + label);
                }
                source.Position = 0;
                Check((await SHA256.HashDataAsync(source)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(path) == time, "WMA original bytes/time " + label);
                results.Add(new { name, target = target.ToString(), decodedFrames = reference.Length / 16, maximum, rms });
            }
            catch (Exception error) { failures.Add(label + ": " + error.GetType().Name + ": " + error.Message); }
            Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "WMA scratch cleanup " + label);
            await File.WriteAllTextAsync(Path.Combine(evidence, "progress.json"), JsonSerializer.Serialize(new { checks, results, failures }));
        }
        async Task<byte[]> Decode(string path)
        {
            using var input = File.OpenRead(path);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, input,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-i", "fd:", "-map", "0:a:0", "-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"], 2 * 1024 * 1024, default);
        }
        void Check(bool passed, string message)
        { if (!passed) throw new InvalidDataException(message); checks.Add(message); }
    }

    internal static byte[] VoiceMetadataFixture(byte[] source)
    {
        var tags = Descriptors(("Title", (ushort)0, Text("Voice title \u00fc")), ("Author", (ushort)0, Text("Voice artist \u4e2d")),
            ("WM/AlbumTitle", (ushort)0, Text("Voice album")), ("WM/Year", (ushort)0, Text("2026")),
            ("WM/TrackNumber", (ushort)3, BitConverter.GetBytes(2u)));
        var png = FlacToOggArtworkContracts.Png(2, 1, [255, 0, 128, 255, 0, 255, 0, 128]);
        var picture = new byte[] { 0 }.Concat(BitConverter.GetBytes((uint)png.Length)).Concat(Text("image/png")).Concat(Text("")).Concat(png).ToArray();
        return ReplaceExtended(source, tags.AsSpan(2).ToArray(), picture);
    }

    private static byte[] ReplaceExtended(byte[] source, byte[] metadata, byte[]? picture = null)
    {
        var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(16)));
        var chunks = new List<byte[]>();
        for (var offset = 30; offset < end;)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(offset + 16)));
            if (new Guid(source.AsSpan(offset, 16)) != new Guid("d2d0a440-e307-11d2-97f0-00a0c95ea850")) chunks.Add(source.AsSpan(offset, size).ToArray());
            offset += size;
        }
        if (picture is not null)
            metadata = BitConverter.GetBytes((ushort)6).Concat(metadata).Concat(Descriptors(("WM/Picture", (ushort)1, picture)).AsSpan(2).ToArray()).ToArray();
        chunks.Add(Object("d2d0a440-e307-11d2-97f0-00a0c95ea850", metadata));
        var result = source.AsSpan(0, 30).ToArray().Concat(chunks.SelectMany(chunk => chunk)).Concat(source.AsSpan(end).ToArray()).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)(30 + chunks.Sum(chunk => chunk.Length)));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), (uint)chunks.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(70), (ulong)result.Length); return result;
    }
    private static byte[] AppendHeader(byte[] source, byte[] added)
    {
        var end = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(16)));
        var result = source.AsSpan(0, end).ToArray().Concat(added).Concat(source.AsSpan(end).ToArray()).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)(end + added.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24), BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(24)) + 1);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(70), (ulong)result.Length); return result;
    }
    private static byte[] Text(string value) => Encoding.Unicode.GetBytes(value + '\0');
    private static byte[] Descriptors(params (string Name, ushort Type, byte[] Value)[] values)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write((ushort)values.Length);
        foreach (var (name, type, data) in values)
        { var text = Text(name); writer.Write((ushort)text.Length); writer.Write(text); writer.Write(type); writer.Write((ushort)data.Length); writer.Write(data); }
        return output.ToArray();
    }
    private static byte[] Object(string id, byte[] data)
    {
        var result = new byte[24 + data.Length]; new Guid(id).TryWriteBytes(result);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)result.Length); data.CopyTo(result, 24); return result;
    }
}
