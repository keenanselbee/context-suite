using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static partial class WavePreservationContracts
{
    internal static async Task PicturesAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports, string independent)
    {
        var path = Path.Combine(fixtures, "source.wav"); var source = await File.ReadAllBytesAsync(path); var hash = SHA256.HashData(source);
        source = WithoutInfo(source);
        var pixels = new byte[32 * 32 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i / 4); pixels[i + 1] = (byte)(i / 128); pixels[i + 2] = 173; pixels[i + 3] = (byte)(i / 4 % 3 == 0 ? 128 : 255); }
        var png = FlacToOggArtworkContracts.Png(32, 32, pixels);
        var pngPath = Path.Combine(evidence, "authored.png"); await File.WriteAllBytesAsync(pngPath, png);
        byte[] jpeg;
        using (var file = File.OpenRead(pngPath))
            jpeg = await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", "png_pipe", "-i", "fd:",
                    "-frames:v", "1", "-c:v", "mjpeg", "-threads", "1", "-q:v", "2", "-f", "image2pipe", "pipe:1"], 1024 * 1024, default);
        var jpegPath = Path.Combine(evidence, "authored.jpg"); await File.WriteAllBytesAsync(jpegPath, jpeg);
        var jpegPixels = await Decode(jpegPath, "jpeg_pipe", 0);
        if (!(await Decode(pngPath, "png_pipe", 0)).AsSpan().SequenceEqual(pixels) || jpegPixels.Length != pixels.Length)
            throw new InvalidDataException("Independent cover fixture decoding failed.");
        checks.Add("WAV artwork: authored PNG RGBA and independently encoded/decoded JPEG reference");
        var scratch = Path.Combine(evidence, "wave-artwork-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var (version, encoding) in new[] { (2, 0), (2, 1), (3, 0), (3, 1), (4, 2), (4, 3) })
        foreach (var leading in new[] { true, false })
        foreach (var id in new[] { "id3 ", "ID3 " })
            await Accept(Mp3PreservationContracts.WaveTag(version, encoding, png, jpeg),
                "v" + version + "-e" + encoding + (leading ? "-before" : "-after") + (id == "id3 " ? "-lower" : "-upper"), leading, id);
        await Accept(Mp3PreservationContracts.WaveTag(4, 3, png, jpeg, true), "neutral", true, "id3 ", false);
        var large = new byte[512 * 512 * 4]; uint random = 52719;
        for (var index = 0; index < large.Length; index++) { random = unchecked(random * 1664525 + 1013904223); large[index] = (byte)(random >> 24); }
        await Accept(Mp3PreservationContracts.WaveTag(4, 3, FlacToOggArtworkContracts.Png(512, 512, large), jpeg, true), "large", true, "id3 ", false, large);
        var tagged = Insert(source, "id3 ", Mp3PreservationContracts.WaveTag(4, 3, png, jpeg), false);
        var invalids = new[] { Insert(tagged, "ID3 ", Mp3PreservationContracts.WaveTag(4, 3, png, jpeg), true),
            Append(tagged, "LIST", Info("INAM", Encoding.ASCII.GetBytes("Artwork title\0"))), Append(tagged, "cue ", new byte[28]) };
        for (var index = 0; index < invalids.Length; index++)
        {
            var rejectedPath = Path.Combine(evidence, "refused-" + index + ".wav"); await File.WriteAllBytesAsync(rejectedPath, invalids[index]);
            try { await adapter.ConvertAsync(invalids[index], AudioFormat.Flac, AudioConversionConsent.None, default); throw new Exception("Unsafe WAV information was accepted."); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { checks.Add("WAV artwork: duplicated ID3/INFO or cue data refused before encoding " + index); }
        }
        if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new IOException("Artwork source changed or scratch remains.");
        checks.Add("WAV artwork: original source hash and scratch cleanup");

        async Task Accept(byte[] tag, string name, bool leading, string id, bool multiple = true, byte[]? firstPixels = null)
        {
            Console.WriteLine("Checking WAV artwork " + name);
            var authored = Insert(source, id, tag, leading); var digest = SHA256.HashData(authored);
            var inputPath = Path.Combine(evidence, name + ".wav"); await File.WriteAllBytesAsync(inputPath, authored); var modified = File.GetLastWriteTimeUtc(inputPath);
            using var original = new MemoryStream(authored);
            var expected = (await WaveMetadata.ReadAsync(original, default, true)).Pictures;
            var same = await adapter.ConvertAsync(authored, AudioFormat.Wave, AudioConversionConsent.None, default);
            if (!same.Plan.AlreadyTarget || !same.Bytes.AsSpan().SequenceEqual(authored)) throw new InvalidDataException("WAV artwork no-op changed bytes.");
            checks.Add("WAV artwork: same-format bytes " + name);
            foreach (var target in multiple ? new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.Vorbis, AudioFormat.Opus } : new[] { AudioFormat.Flac, AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Vorbis, AudioFormat.Opus })
            {
                var result = await adapter.ConvertAsync(authored, target, consent, default);
                if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || result.DecodedFrames != 96000 ||
                    result.Facts.Streams.Count(stream => stream.AttachedPicture) != expected.Length)
                    throw new InvalidDataException("WAV artwork conversion lost inventory or audio extent.");
                var outputPath = Path.Combine(evidence, name + result.Plan.Extension); await File.WriteAllBytesAsync(outputPath, result.Bytes);
                ImmutableDictionary<string, string> tags;
                if (target == AudioFormat.Flac)
                {
                    var header = FlacMetadata.Parse(result.Bytes); var actual = header.Blocks.Where(block => block.Type == 6).ToArray();
                    if (actual.Length != expected.Length || actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(expected[i].Data.AsSpan())).Any())
                        throw new InvalidDataException("FLAC output changed a complete MP3 picture block.");
                    tags = FlacConversionMetadata.Read(header, true);
                }
                else if (target is AudioFormat.Mp3 or AudioFormat.M4a)
                {
                    using var output = new MemoryStream(result.Bytes);
                    var actual = target == AudioFormat.Mp3 ? (await Mp3Metadata.ReadAsync(output, default, true)).Pictures : (await M4aMetadata.ReadAsync(output, default, true)).Pictures;
                    if (actual.Length != expected.Length || actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(expected[i].Data.AsSpan())).Any())
                        throw new InvalidDataException("Output changed a complete WAV picture block.");
                    tags = target == AudioFormat.Mp3 ? (await Mp3Metadata.ReadAsync(output, default, true)).Tags : (await M4aMetadata.ReadAsync(output, default, true)).Tags;
                }
                else { using var output = new MemoryStream(result.Bytes); tags = (await OggMetadata.ReadAsync(output, default, true)).ConversionTags(expected); }
                if (multiple && (tags["title"] != "Artwork title" || tags["artist"] != "Fixture artist")) throw new InvalidDataException("Artwork overwrote descriptive tags.");
                checks.Add("WAV artwork: exact blocks, tags and 96000 audio frames " + name + "/" + target);
                for (var i = 0; i < expected.Length; i++)
                {
                    var reference = i == 0 ? firstPixels ?? pixels : jpegPixels;
                    var decoded = await Decode(outputPath, target switch { AudioFormat.Flac => "flac", AudioFormat.Mp3 => "mp3", AudioFormat.M4a => "mov", _ => "ogg" }, i, reference.Length);
                    if (!decoded.AsSpan().SequenceEqual(reference)) throw new InvalidDataException("Output cover RGBA disagrees with independent source decode.");
                    checks.Add("WAV artwork: independent cover RGBA " + name + "/" + target + "/" + i);
                }
                reports.Add(new { Name = name, Target = target.ToString(), Input = inputPath, Output = outputPath,
                    SourceSha256 = Convert.ToHexString(digest), OutputSha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)), result.DecodedFrames,
                    Blocks = expected.Select(block => Convert.ToHexString(SHA256.HashData(block.Data.AsSpan()))).ToArray() });
            }
            if (!SHA256.HashData(await File.ReadAllBytesAsync(inputPath)).AsSpan().SequenceEqual(digest) || File.GetLastWriteTimeUtc(inputPath) != modified) throw new IOException("Authored source changed.");
        }
        async Task<byte[]> Decode(string filePath, string format, int index, int count = 0)
        {
            using var file = File.OpenRead(filePath);
            return await SeekableMediaProcess.RunAsync(Path.Combine(independent, "ffmpeg.exe"), evidence, file,
                ["-v", "error", "-nostdin", "-protocol_whitelist", "fd", "-format_whitelist", format, "-i", "fd:",
                    "-map", "0:v:" + index, "-threads", "1", "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"], (count == 0 ? pixels.Length : count) + 1, default);
        }

    }

    internal static byte[] Insert(byte[] source, string id, byte[] tag, bool leading)
    {
        using var output = new MemoryStream(); output.Write(source.AsSpan(0, 12)); var inserted = false;
        for (var offset = 12; offset < source.Length;)
        {
            var size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4)));
            if (leading && !inserted && source.AsSpan(offset, 4).SequenceEqual("data"u8)) { WriteTag(); inserted = true; }
            var extent = 8 + size + (size & 1); output.Write(source.AsSpan(offset, extent)); offset += extent;
        }
        if (!inserted) WriteTag();
        var bytes = output.ToArray(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8); return bytes;
        void WriteTag() { output.Write(Encoding.ASCII.GetBytes(id)); output.Write(BitConverter.GetBytes(tag.Length)); output.Write(tag); if ((tag.Length & 1) != 0) output.WriteByte(0); }
    }
    private static byte[] WithoutInfo(byte[] source)
    {
        using var output = new MemoryStream(); output.Write(source.AsSpan(0, 12));
        for (var offset = 12; offset < source.Length;)
        {
            var size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4)));
            var extent = 8 + size + (size & 1);
            if (!source.AsSpan(offset, 4).SequenceEqual("LIST"u8)) output.Write(source.AsSpan(offset, extent));
            offset += extent;
        }
        var bytes = output.ToArray(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8); return bytes;
    }
}
