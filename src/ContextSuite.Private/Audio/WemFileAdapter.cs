using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// A narrow Wwise PCM16/Vorbis to WAV candidate. The application owns final
// publication; this adapter writes only to its empty reserved temporary file.
public sealed class WemFileAdapter : IDisposable
{
    private const long MaximumWaveBytes = AudioFileSource.MaximumFileBytes;
    private readonly WemDecoderRuntime _runtime;
    private readonly string _scratch;
    private readonly RuntimeDirectoryLeases _scratchDirectories = new();
    private bool _disposed;

    public WemFileAdapter(string binaryDirectory, string scratchDirectory)
    {
        _scratch = RuntimeDirectoryLeases.LocalPath(scratchDirectory);
        _runtime = new WemDecoderRuntime(binaryDirectory);
        try
        {
            Directory.CreateDirectory(_scratch);
            _scratchDirectories.Open(_scratch);
        }
        catch { _runtime.Dispose(); _scratchDirectories.Dispose(); throw; }
    }

    public async Task<AudioFileSource> ProbeConversionAsync(AudioFileProbe request, AudioFormat target, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing audio item identity.");
        if (target != AudioFormat.Wave) throw new NotSupportedException("WEM currently converts to WAV only.");
        using var deadline = Deadline(cancellationToken, TimeSpan.FromSeconds(30));
        using var source = MediaFiles.OpenRead(request.Path);
        return await InspectAsync(request.ItemId, request.Path, source, deadline.Token);
    }

    public async Task<AudioWorkResult> ConvertAsync(AudioConversionWork work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        if (work.Target != AudioFormat.Wave || work.Source.Facts.Container != "wem")
            throw new NotSupportedException("This adapter accepts WEM to WAV work only.");
        using var deadline = Deadline(cancellationToken, TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var actual = await InspectAsync(work.Source.ItemId, work.Source.Path, source, token);
        if (!SameSource(work.Source, actual)) throw new InvalidDataException("WEM changed after planning.");
        var header = await WemHeader.ReadAsync(source, token);
        var plan = AudioConversionPlan.Create(actual.Facts, AudioFormat.Wave);
        var bytesPerSample = header.Codec == WemCodec.Pcm16 ? 2 : 4;
        var sampleBytes = checked(header.SampleCount!.Value * header.Channels * bytesPerSample);
        if (sampleBytes <= 0 || sampleBytes > MaximumWaveBytes - 112)
            throw new NotSupportedException("Decoded WEM exceeds the WAV output budget.");

        var directory = Path.Combine(_scratch, "wem-decode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input.wem");
        var decodedPath = Path.Combine(directory, "decoded.wav");
        try
        {
            using var directoryLease = new RuntimeDirectoryLeases();
            directoryLease.Open(directory);
            source.Position = 0;
            await using (var writer = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
                await source.CopyToAsync(writer, 65536, token);
            using var snapshot = MediaFiles.OpenRead(inputPath);
            snapshot.Position = 0;
            if (snapshot.Length != actual.FileBytes || Convert.ToHexString(await SHA256.HashDataAsync(snapshot, token)) != actual.Sha256)
                throw new IOException("WEM input snapshot differs from the planned source.");
            _runtime.VerifyMembership();
            using var childDeadline = Deadline(token, TimeSpan.FromSeconds(90));
            try
            {
                await using var writer = new FileStream(decodedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    65536, FileOptions.Asynchronous);
                await SeekableMediaProcess.RunAsync(_runtime.Executable, directory, snapshot,
                    ["-i", "-L", "-W", bytesPerSample == 2 ? "1" : "4", "-p", inputPath],
                    async (stdout, childToken) => await CopyBoundedAsync(stdout, writer, MaximumWaveBytes, childToken),
                    childDeadline.Token);
                await writer.FlushAsync(childDeadline.Token);
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested && childDeadline.IsCancellationRequested)
            { throw new TimeoutException("WEM decoding exceeded its deadline.", error); }
            _runtime.VerifyMembership();
            using var decoded = MediaFiles.OpenRead(decodedPath);
            var waveBytes = await ValidateWaveAsync(decoded, snapshot, header, token);
            decoded.Position = 0;
            var expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(decoded, token));
            decoded.Position = 0;
            using var output = new ReservedMediaOutput(work.TemporaryPath, MaximumWaveBytes);
            await CopyToReservationAsync(decoded, output, token);
            output.Flush(true);
            output.Position = 0;
            if (output.Length != decoded.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != expectedHash)
                throw new IOException("Reserved WEM output changed while copying.");
            return new(new(work.Source.ItemId, expectedHash, true), actual.FileBytes, waveBytes,
                header.SampleCount.Value, actual.Sha256, plan.Policy, WemDecoderRuntime.Identity);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("WEM conversion exceeded its deadline.", error); }
        finally { Cleanup(directory, inputPath, decodedPath); }
    }

    private static async Task<AudioFileSource> InspectAsync(Guid itemId, string path, FileStream source, CancellationToken token)
    {
        if (source.Length is <= 0 or > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("WEM input exceeds its file budget.");
        var header = await WemHeader.ReadAsync(source, token);
        RequireSupported(header);
        var samples = header.SampleCount!.Value;
        var duration = (decimal)samples / header.SampleRate;
        var codec = header.Codec == WemCodec.Pcm16 ? "pcm_s16le" : "vorbis";
        var bits = header.Codec == WemCodec.Pcm16 ? 16 : (int?)null;
        var bitRate = header.Codec == WemCodec.Pcm16 ? (long)header.SampleRate * header.Channels * 16 : (long?)null;
        var stream = new AudioStreamFacts(0, "audio", codec, header.SampleRate, header.Channels, bits,
            bitRate, duration, header.Channels == 1 ? "mono" : "stereo", ImmutableDictionary<string, string>.Empty);
        var facts = new AudioProbeFacts("wem", duration, [stream], ImmutableDictionary<string, string>.Empty);
        source.Position = 0;
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        var result = new AudioFileSource(itemId, path, sha256, source.Length, facts);
        result.Validate();
        return result;
    }

    private static void RequireSupported(WemHeader header)
    {
        if (header.BigEndian || header.HasUnrecognizedChunks || header.Channels is not (1 or 2) ||
            header.SampleRate is not (44100 or 48000) || header.SampleCount is not > 0 ||
            header.Codec == WemCodec.Pcm16 && header.BitDepth != 16 ||
            header.Codec == WemCodec.Vorbis && header.Vorbis is null ||
            header.Codec is not (WemCodec.Pcm16 or WemCodec.Vorbis))
            throw new NotSupportedException("This WEM variant has no validated WAV decoding profile.");
    }

    private static bool SameSource(AudioFileSource expected, AudioFileSource actual)
    {
        if (expected.ItemId != actual.ItemId || expected.Path != actual.Path ||
            !string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase) ||
            expected.FileBytes != actual.FileBytes || expected.ConversionBlockReason != actual.ConversionBlockReason ||
            expected.OptimizationBlockReason != actual.OptimizationBlockReason ||
            expected.Facts.Container != actual.Facts.Container ||
            expected.Facts.ReportedDurationSeconds != actual.Facts.ReportedDurationSeconds ||
            !expected.Facts.Tags.IsEmpty || expected.Facts.Streams.Length != 1) return false;
        var before = expected.Facts.Streams[0]; var after = actual.Facts.Streams[0];
        return before.Tags.IsEmpty && before with { Tags = ImmutableDictionary<string, string>.Empty } == after;
    }

    private static CancellationTokenSource Deadline(CancellationToken token, TimeSpan maximum)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(maximum);
        return deadline;
    }

    private static async Task<long> ValidateWaveAsync(FileStream wave, FileStream source,
        WemHeader header, CancellationToken token)
    {
        if (wave.Length is < 44 or > MaximumWaveBytes) throw new InvalidDataException("Decoded WAV exceeds its file budget.");
        wave.Position = 0;
        var prefix = new byte[12]; await wave.ReadExactlyAsync(prefix, token);
        if (!prefix.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !prefix.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4)) != wave.Length - 8)
            throw new InvalidDataException("Decoded WAV RIFF framing is invalid.");
        byte[]? format = null, loop = null;
        long dataOffset = -1, dataSize = -1;
        var chunkHeader = new byte[8];
        for (var count = 0; wave.Position < wave.Length && count < 5; count++)
        {
            if (wave.Length - wave.Position < 8) throw new InvalidDataException("Decoded WAV chunk is truncated.");
            await wave.ReadExactlyAsync(chunkHeader, token);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4));
            var start = wave.Position;
            var end = start + size;
            if (end + (size & 1) > wave.Length) throw new InvalidDataException("Decoded WAV chunk exceeds its extent.");
            if (chunkHeader.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                if (format is not null || size != 16 || dataOffset >= 0) throw new InvalidDataException("Decoded WAV format is unexpected.");
                format = new byte[16]; await wave.ReadExactlyAsync(format, token);
            }
            else if (chunkHeader.AsSpan(0, 4).SequenceEqual("smpl"u8))
            {
                if (loop is not null || size != 60 || dataOffset >= 0) throw new InvalidDataException("Decoded WAV loop is unexpected.");
                loop = new byte[60]; await wave.ReadExactlyAsync(loop, token);
            }
            else if (chunkHeader.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                if (dataOffset >= 0 || format is null) throw new InvalidDataException("Decoded WAV data is duplicated or misplaced.");
                dataOffset = start; dataSize = size;
            }
            else throw new InvalidDataException("Decoded WAV contains an unvalidated chunk.");
            wave.Position = end + (size & 1);
        }
        if (wave.Position != wave.Length || format is null || dataOffset < 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(format) != (header.Codec == WemCodec.Pcm16 ? 1 : 3) ||
            BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2)) != header.Channels ||
            BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4)) != header.SampleRate)
            throw new InvalidDataException("Decoded WAV format or extent changed.");
        var width = header.Codec == WemCodec.Pcm16 ? 2 : 4;
        var block = header.Channels * width;
        if (BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(8)) != (long)header.SampleRate * block ||
            BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12)) != block ||
            BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14)) != width * 8 ||
            dataSize != checked(header.SampleCount!.Value * block))
            throw new InvalidDataException("Decoded WAV sample count or precision changed.");
        if (header.Loop is null && loop is not null || header.Loop is not null && loop is null)
            throw new InvalidDataException("Decoded WAV loop presence changed.");
        if (loop is not null && (BinaryPrimitives.ReadUInt32LittleEndian(loop.AsSpan(28)) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(loop.AsSpan(40)) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(loop.AsSpan(44)) != header.Loop!.StartSample ||
            (long)BinaryPrimitives.ReadUInt32LittleEndian(loop.AsSpan(48)) + 1 != header.Loop.EndSampleExclusive))
            throw new InvalidDataException("Decoded WAV loop points changed.");

        wave.Position = dataOffset;
        if (header.Codec == WemCodec.Pcm16)
        {
            source.Position = header.DataOffset;
            var actual = new byte[65536]; var expected = new byte[65536];
            for (long remaining = dataSize; remaining > 0;)
            {
                var take = (int)Math.Min(remaining, actual.Length);
                await wave.ReadExactlyAsync(actual.AsMemory(0, take), token);
                await source.ReadExactlyAsync(expected.AsMemory(0, take), token);
                if (!actual.AsSpan(0, take).SequenceEqual(expected.AsSpan(0, take)))
                    throw new InvalidDataException("Decoded WEM PCM differs from source samples.");
                remaining -= take;
            }
        }
        else
        {
            var samples = new byte[65536];
            for (long remaining = dataSize; remaining > 0;)
            {
                var take = (int)Math.Min(remaining, samples.Length);
                await wave.ReadExactlyAsync(samples.AsMemory(0, take), token);
                for (var offset = 0; offset < take; offset += 4)
                    if (!float.IsFinite(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(samples.AsSpan(offset)))))
                        throw new InvalidDataException("Decoded WEM Vorbis contains nonfinite samples.");
                remaining -= take;
            }
        }
        return wave.Length;
    }

    private static async Task CopyToReservationAsync(Stream source, ReservedMediaOutput output, CancellationToken token)
    {
        var buffer = new byte[65536]; int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
    }

    private static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long maximum, CancellationToken token)
    {
        var buffer = new byte[65536]; long total = 0; int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            total = checked(total + count);
            if (total > maximum) throw new InvalidDataException("Decoded WEM exceeds its output budget.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
        }
        return total;
    }

    private static void Cleanup(string directory, params string[] files)
    {
        foreach (var path in files)
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scratchDirectories.Dispose(); _runtime.Dispose();
    }
}
