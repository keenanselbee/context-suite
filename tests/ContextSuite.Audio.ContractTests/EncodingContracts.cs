using System.Security.Cryptography;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class EncodingContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var scratch = Path.Combine(evidence, "encoding-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent all = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var source in new[] { "wav", "flac", "mp3", "m4a", "ogg", "opus" })
        {
            var input = Path.Combine(fixtures, "source." + source);
            var bytes = await File.ReadAllBytesAsync(input);
            var hash = SHA256.HashData(bytes);
            foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
            {
                var result = await adapter.ConvertAsync(bytes, target, all, CancellationToken.None);
                if (!result.Plan.AlreadyTarget && (result.DecodedFrames != 96000 || !result.MissingTags.IsEmpty))
                    throw new InvalidDataException($"Audio encoding candidate failed frame/tag checks: {source} to {target}: {string.Join(",", result.MissingTags)}");
                if (!SHA256.HashData(await File.ReadAllBytesAsync(input)).SequenceEqual(hash) || Directory.EnumerateFileSystemEntries(scratch).Any())
                    throw new InvalidDataException("Audio encoding changed an original or left owned scratch.");
                var name = source + "-to-" + target;
                await File.WriteAllBytesAsync(Path.Combine(evidence, name + result.Plan.Extension), result.Bytes);
                checks.Add("audio encoding candidate: " + name);
                reports.Add(new { name, result.Plan, result.DecodedFrames, result.ExactSamples, result.MaximumSampleError,
                    result.RootMeanSquareError, result.MissingTags, bytes = result.Bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(result.Bytes)) });
            }
        }
        foreach (var bits in new[] { 8, 16, 24, 32 })
        {
            var wave = Wave(bits, false, 48000, 2);
            await File.WriteAllBytesAsync(Path.Combine(evidence, "authored-pcm" + bits + ".wav"), wave);
            Console.WriteLine("Checking integer audio precision: " + bits);
            var encoded = await adapter.ConvertAsync(wave, AudioFormat.Flac, all, default);
            var decoded = await adapter.ConvertAsync(encoded.Bytes, AudioFormat.Wave, all, default);
            if (!encoded.ExactSamples || !decoded.ExactSamples || encoded.Facts.Streams[0].SampleBits != Math.Max(16, bits))
                throw new Exception("Integer precision round trip failed: " + bits);
            checks.Add("integer samples retained through FLAC/WAVE: " + bits + (bits == 8 ? " (16-bit FLAC storage)" : ""));
        }
        foreach (var bits in new[] { 32, 64 })
        {
            var wave = Wave(bits, true, 48000, 2);
            var encoded = await adapter.ConvertAsync(wave, AudioFormat.Flac, all, default);
            if (encoded.ExactSamples || encoded.MaximumSampleError > 1.0 / 8388608)
                throw new Exception("Float-to-integer precision policy failed: " + bits);
            checks.Add("floating-to-FLAC precision consent and measured quantization: " + bits);
        }
        var surround = await adapter.ConvertAsync(Wave(24, false, 96000, 6), AudioFormat.Flac, all, default);
        if (!surround.ExactSamples || surround.Facts.Streams[0].Channels != 6 || surround.Facts.Streams[0].SampleRate != 96000)
            throw new Exception("Surround rate/layout preservation failed.");
        checks.Add("96 kHz 24-bit surround retains exact samples and layout");
        foreach (var layout in new[] { (Channels: 5, Mask: 0x37u, Name: "5.0"), (Channels: 7, Mask: 0x70fu, Name: "6.1") })
        {
            var mapped = await adapter.ConvertAsync(RateLayoutMatrix.Wave(48000, layout.Channels, layout.Mask), AudioFormat.Opus, all, default);
            if (mapped.DecodedFrames != 48000 || mapped.Facts.Streams[0].ChannelLayout != layout.Name ||
                mapped.RootMeanSquareError is > .05 or null || !mapped.SourceMetadataVerified)
                throw new InvalidDataException("Opus changed the authored channel signals: " + layout.Name);
            checks.Add("Opus explicit mapping retains distinct channel signals: " + layout.Name);
        }
        foreach (var rate in new[] { 44100, 8000 })
        {
            var resampled = await adapter.ConvertAsync(Wave(16, false, rate, 1), AudioFormat.Opus, all, default);
            if (resampled.Facts.Streams[0].SampleRate != 48000 || resampled.ExactSamples || resampled.MaximumSampleError is null || resampled.RootMeanSquareError is > .15 or null)
                throw new Exception("Opus resampling was not reported explicitly: " + rate);
            checks.Add("mono Opus resampling explicitly planned: " + rate);
        }
        var originalFlac = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.flac"));
        var padding = new byte[4 + 262144]; padding[0] = 1; padding[1] = 4;
        var padded = originalFlac[..42].Concat(padding).Concat(originalFlac[42..]).ToArray();
        var paddedHash = SHA256.HashData(padded);
        var optimized = await adapter.OptimizeFlacAsync(padded, default);
        FlacMetadata.RequirePreservedMetadata(FlacMetadata.Parse(padded), FlacMetadata.Parse(optimized.Bytes));
        if (!optimized.IsOptimization || !optimized.ExactSamples || optimized.Bytes.Length >= padded.Length || !SHA256.HashData(padded).SequenceEqual(paddedHash))
            throw new Exception("FLAC optimization did not preserve the source and reduce its size.");
        await File.WriteAllBytesAsync(Path.Combine(evidence, "optimized.flac"), optimized.Bytes);
        checks.Add("FLAC optimization: smaller result, exact decoded samples and original metadata bytes");
        reports.Add(new { operation = "flac-optimization", sourceBytes = padded.Length, outputBytes = optimized.Bytes.Length,
            optimized.DecodedFrames, optimized.ExactSamples, metadataBytesPreserved = true });
        var second = await adapter.OptimizeFlacAsync(optimized.Bytes, default);
        if (second.Bytes.Length > optimized.Bytes.Length || !second.ExactSamples) throw new Exception("FLAC optimization enlarged the file.");
        checks.Add("FLAC optimization: already-compressed result never grows");
        var application = originalFlac[..42].Concat(new byte[] { 2, 0, 0, 4, 67, 83, 84, 70 }).Concat(originalFlac[42..]).ToArray();
        try { await adapter.OptimizeFlacAsync(application, default); throw new Exception("Unknown application metadata was dropped."); }
        catch (NotSupportedException) { checks.Add("FLAC optimization: unsupported application block prevents rewriting"); }
        if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Audio operation left scratch behind.");
        checks.Add("audio scratch cleaned after conversion, optimization and refusal");
        try { await adapter.ConvertAsync(await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.mp3")), AudioFormat.M4a, AudioConversionConsent.None, default);
            throw new Exception("Missing consent was accepted."); }
        catch (InvalidOperationException) { checks.Add("encoding rejects missing quality consent before creating scratch"); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await adapter.ConvertAsync(new byte[10], AudioFormat.Flac, all, cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { checks.Add("encoding pre-cancellation"); }
        try { using var writer = new FileStream(Path.Combine(binary, "ffmpeg.exe"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Encoder write allowed."); }
        catch (IOException) { checks.Add("encoder hash lease excludes mutation"); }
        adapter.Dispose();
        try { await adapter.ConvertAsync(new byte[10], AudioFormat.Flac, all, default); throw new Exception("Disposed encoding allowed."); }
        catch (ObjectDisposedException) { checks.Add("disposed encoder rejected"); }
    }

    internal static byte[] Wave(int bits, bool floating, int rate, int channels, int? frameCount = null, bool lowFrequencyEffects = false)
    {
        var frames = frameCount ?? rate / 5;
        var frameBytes = channels * bits / 8;
        var extensible = channels > 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write((extensible ? 60 : 36) + frames * frameBytes);
        writer.Write("WAVEfmt "u8); writer.Write(extensible ? 40 : 16);
        writer.Write((ushort)(extensible ? 0xfffe : floating ? 3 : 1)); writer.Write((ushort)channels);
        writer.Write(rate); writer.Write(rate * frameBytes); writer.Write((ushort)frameBytes); writer.Write((ushort)bits);
        if (extensible)
        {
            writer.Write((ushort)22); writer.Write((ushort)bits); writer.Write(0x3f);
            writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
        }
        writer.Write("data"u8); writer.Write(frames * frameBytes);
        for (var frame = 0; frame < frames; frame++)
        for (var channel = 0; channel < channels; channel++)
        {
            var value = Math.Sin((frame + channel * 13) * (lowFrequencyEffects && channels == 6 && channel == 3 ? .007 : .07)) * .7;
            if (floating) { if (bits == 32) writer.Write((float)value); else writer.Write(value); }
            else if (bits == 8) writer.Write((byte)Math.Round(value * 127 + 128));
            else
            {
                var sample = (int)Math.Round(value * ((1L << (bits - 1)) - 1));
                if (frame == 0) sample = bits == 32 ? int.MinValue : -(1 << (bits - 1));
                if (frame == 1) sample = bits == 32 ? int.MaxValue : (1 << (bits - 1)) - 1;
                for (var index = 0; index < bits / 8; index++) writer.Write((byte)(sample >> (index * 8)));
            }
        }
        return stream.ToArray();
    }
}
