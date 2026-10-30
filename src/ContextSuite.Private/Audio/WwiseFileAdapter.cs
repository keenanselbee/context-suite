using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Xml.Linq;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// Uses the customer's inspected Wwise authoring installation. No Wwise runtime
// is bundled, and a validated WEM is copied only to the application's reservation.
public sealed class WwiseFileAdapter : IDisposable
{
    private const long MaximumInputBytes = 64L * 1024 * 1024;
    private const long MaximumWemBytes = MaximumInputBytes + 2L * 1024 * 1024;
    private readonly WemFileAdapter _decoder;
    private AudioEncodingAdapter? _audio;
    private readonly string? _audioEngineDirectory;
    private readonly object _audioGate = new();
    private readonly RuntimeDirectoryLeases _scratchDirectories = new();
    private readonly string _scratch;
    private bool _disposed;

    public WwiseFileAdapter(string decoderDirectory, string scratchDirectory, string? audioEngineDirectory = null)
    {
        _scratch = RuntimeDirectoryLeases.LocalPath(scratchDirectory);
        _audioEngineDirectory = audioEngineDirectory;
        _decoder = new WemFileAdapter(decoderDirectory, scratchDirectory);
        try
        {
            Directory.CreateDirectory(_scratch);
            _scratchDirectories.Open(_scratch);
        }
        catch { _audio?.Dispose(); _decoder.Dispose(); _scratchDirectories.Dispose(); throw; }
    }

    public async Task<AudioFileSource> ProbeConversionAsync(AudioFileProbe request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing audio item identity.");
        using var deadline = Deadline(cancellationToken, TimeSpan.FromSeconds(30));
        using var source = MediaFiles.OpenRead(request.Path);
        return await InspectAsync(request.ItemId, request.Path, source, deadline.Token);
    }

    public async Task<AudioWorkResult> ConvertAsync(AudioConversionWork work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        if (work.Target != AudioFormat.Wem || work.Wem is null || work.Source.Facts.Container is not ("wav" or "mp3"))
            throw new NotSupportedException("Wwise encoding requires explicit WEM options and supported WAV or MP3 audio.");
        using var deadline = Deadline(cancellationToken, TimeSpan.FromSeconds(240));
        var token = deadline.Token;
        var options = work.Wem;
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var actual = await InspectAsync(work.Source.ItemId, work.Source.Path, source, token);
        if (!SameSource(work.Source, actual)) throw new InvalidDataException("Audio changed after planning.");
        var originalWave = actual.Facts.Container == "wav" ? await WwiseWaveInput.ReadAsync(source, token) : null;
        var plan = AudioConversionPlan.Create(actual.Facts, AudioFormat.Wem, options);
        plan.RequireConsent(work.AcceptedConsent);

        using var installedDirectories = new RuntimeDirectoryLeases();
        var consolePath = RuntimeDirectoryLeases.LocalPath(options.Installation.ConsolePath);
        using var console = installedDirectories.OpenFileRead(consolePath);
        var inspectedInstallation = await WwiseInstallation.InspectAsync(consolePath, token);
        if (inspectedInstallation != options.Installation || console.Length > WwiseInstallation.MaximumExecutableBytes)
            throw new InvalidDataException("The selected Wwise installation changed after inspection.");
        using var referenceLease = options.Reference is { } selectedReference ? MediaFiles.OpenRead(selectedReference.Path) : null;
        if (options.Reference is { } reference)
        {
            var inspectedReference = await WemReference.InspectAsync(reference.Path, token);
            if (inspectedReference != reference || inspectedReference.Codec != options.Codec ||
                inspectedReference.SampleRate != plan.SampleRate || inspectedReference.Channels != plan.Channels ||
                referenceLease!.Length != reference.FileBytes)
                throw new InvalidDataException("The selected original WEM changed or differs from this WAV.");
        }

        var directory = Path.Combine(_scratch, "wwise-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var directoryLease = new RuntimeDirectoryLeases();
            directoryLease.Open(directory);
            var snapshotPath = Path.Combine(directory, originalWave is { Floating: false } ? "input.wav" :
                actual.Facts.Container == "mp3" ? "source.mp3" : "source.wav");
            source.Position = 0;
            await using (var writer = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
                await source.CopyToAsync(writer, 65536, token);
            using var snapshot = MediaFiles.OpenRead(snapshotPath);
            snapshot.Position = 0;
            if (snapshot.Length != actual.FileBytes || Convert.ToHexString(await SHA256.HashDataAsync(snapshot, token)) != actual.Sha256)
                throw new IOException("Audio input snapshot differs from the planned source.");

            var inputPath = Path.Combine(directory, "input.wav");
            WwiseWaveInput input;
            if (originalWave is { Floating: false }) input = originalWave;
            else if (originalWave is { Floating: true })
                input = await WwiseSourcePreparation.NormalizeAsync(snapshot, originalWave, inputPath, token);
            else
            {
                var audio = GetAudio();
                snapshot.Position = 0;
                using var decoded = await audio.ConvertFileAsync(snapshot, AudioFormat.Wave, AudioConversionConsent.None, token);
                if (!decoded.Result.SourceMetadataVerified || !decoded.Result.MissingTags.IsEmpty ||
                    decoded.Result.SourceSha256 != actual.Sha256 || decoded.Result.SourceBytes != actual.FileBytes ||
                    decoded.Result.DecodedFrames is not > 0)
                    throw new InvalidDataException("MP3 decoding did not validate its source or metadata.");
                var floating = await WwiseWaveInput.ReadAsync(decoded.Content, token, allowGeneratedEncoderMarker: true);
                if (!floating.Floating || floating.SampleRate != plan.SampleRate || floating.Channels != plan.Channels ||
                    floating.Frames != decoded.Result.DecodedFrames)
                    throw new InvalidDataException("Decoded MP3 changed its rate, channels or sample count.");
                input = await WwiseSourcePreparation.NormalizeAsync(decoded.Content, floating, inputPath, token);
            }
            if (input.SampleRate != plan.SampleRate || input.Channels != plan.Channels || input.Floating)
                throw new InvalidDataException("Wwise preparation changed the planned audio layout.");
            using var prepared = originalWave is { Floating: false } ? snapshot : MediaFiles.OpenRead(inputPath);

            var project = Path.Combine(directory, "Project", "Project.wproj");
            await RunConsoleAsync(consolePath, directory, prepared,
                ["create-new-project", project, "--platform", "Windows"],
                checked(actual.FileBytes + prepared.Length + 64L * 1024 * 1024), token);
            if (!File.Exists(project)) throw new InvalidDataException("Wwise did not create its scratch project.");
            var list = Path.Combine(directory, "input.wsources");
            var preset = options.Codec == WemCodec.Pcm16 ? "PCM As Input" : "Vorbis Quality High";
            var xml = new XDocument(new XElement("ExternalSourcesList",
                new XAttribute("SchemaVersion", "1"), new XAttribute("Root", directory),
                new XElement("Source", new XAttribute("Path", "input.wav"), new XAttribute("Conversion", preset))));
            await using (var writer = new FileStream(list, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
                await xml.SaveAsync(writer, SaveOptions.None, token);
            var outputDirectory = Path.Combine(directory, "output");
            await RunConsoleAsync(consolePath, directory, prepared,
                ["convert-external-source", project, "--source-file", list,
                    "--platform", "Windows", "--output", outputDirectory],
                checked(actual.FileBytes + prepared.Length + MaximumWemBytes + 64L * 1024 * 1024), token);
            var wemPath = Path.Combine(outputDirectory, "Windows", "input.wem");
            if (!File.Exists(wemPath) || Directory.EnumerateFiles(Path.GetDirectoryName(wemPath)!, "*.wem").Count() != 1)
                throw new InvalidDataException("Wwise output does not match the requested source.");
            using var wem = MediaFiles.OpenRead(wemPath);
            if (wem.Length is <= 0 or > MaximumWemBytes)
                throw new InvalidDataException("Wwise output exceeds its WEM file budget.");
            var header = await WemHeader.ReadAsync(wem, token);
            if (header.BigEndian || header.HasUnrecognizedChunks || header.Codec != options.Codec ||
                header.SampleRate != input.SampleRate || header.Channels != input.Channels ||
                header.SampleCount != input.Frames || header.Loop != input.Loop)
                throw new InvalidDataException("Wwise changed the requested codec, frames or forward loop.");
            if (options.Codec == WemCodec.Pcm16) await RequireExactPcmAsync(prepared, input, wem, header, token);
            else await RequireDecodedVorbisAsync(wemPath, directory, prepared, input, token);

            wem.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(wem, token));
            wem.Position = 0;
            using var output = new ReservedMediaOutput(work.TemporaryPath, MaximumWemBytes);
            await CopyBoundedAsync(wem, output, MaximumWemBytes, token);
            output.Flush(true); output.Position = 0;
            if (output.Length != wem.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != hash)
                throw new IOException("Reserved WEM output changed while copying.");
            return new(new(work.Source.ItemId, hash, true), actual.FileBytes, wem.Length,
                input.Frames, actual.Sha256, plan.Policy,
                $"Wwise {inspectedInstallation.ProductVersion} {preset}; pinned vgmstream validation");
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new TimeoutException("Wwise conversion exceeded its deadline.", error); }
        finally { Cleanup(directory); }
    }

    private async Task<AudioFileSource> InspectAsync(Guid itemId, string path, FileStream source, CancellationToken token)
    {
        if (source.Length is <= 0 or > MaximumInputBytes)
            throw new NotSupportedException("Wwise input exceeds its file budget.");
        source.Position = 0;
        var marker = new byte[12];
        await source.ReadExactlyAsync(marker, token);
        AudioProbeFacts facts;
        if (marker.AsSpan(0, 4).SequenceEqual("RIFF"u8) || marker.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            var input = await WwiseWaveInput.ReadAsync(source, token);
            var duration = (decimal)input.Frames / input.SampleRate;
            var stream = new AudioStreamFacts(0, "audio", input.Floating ? "pcm_f32le" : "pcm_s16le",
                input.SampleRate, input.Channels, input.Floating ? 32 : 16,
                (long)input.SampleRate * input.Channels * (input.Floating ? 32 : 16), duration,
                input.Channels == 1 ? "mono" : "stereo", ImmutableDictionary<string, string>.Empty);
            facts = new AudioProbeFacts("wav", duration, [stream], ImmutableDictionary<string, string>.Empty);
        }
        else
        {
            var audio = GetAudio();
            var inventory = await Mp3Metadata.ReadAsync(source, token, true);
            if (!inventory.Tags.IsEmpty || !inventory.Pictures.IsEmpty)
                throw new NotSupportedException("MP3 descriptive tags or artwork cannot be preserved in WEM.");
            var inspected = await audio.InspectConversionAsync(itemId, path, source, AudioFormat.Wave, token);
            if (inspected.Facts.Container != "mp3" || inspected.Facts.Streams.Length != 1 ||
                inspected.Facts.Streams[0].Codec != "mp3" ||
                inspected.Facts.Streams[0].SampleRate != inventory.SampleRate ||
                inspected.Facts.Streams[0].Channels != inventory.Channels)
                throw new InvalidDataException("MP3 inventory and decoder interpretation disagree.");
            // FFprobe may expose a technical encoder marker from MP3 framing.
            // The verified ID3/legacy inventory above excludes user metadata.
            facts = inspected.Facts with
            {
                Tags = FilterTechnicalEncoder(inspected.Facts.Tags),
                Streams = [inspected.Facts.Streams[0] with { Tags = FilterTechnicalEncoder(inspected.Facts.Streams[0].Tags) }]
            };
            if (!facts.Tags.IsEmpty || !facts.Streams[0].Tags.IsEmpty)
                throw new NotSupportedException("MP3 metadata cannot be preserved in WEM.");
        }
        source.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        var result = new AudioFileSource(itemId, path, hash, source.Length, facts);
        result.Validate();
        return result;
    }

    private static ImmutableDictionary<string, string> FilterTechnicalEncoder(ImmutableDictionary<string, string> tags) =>
        tags.Remove("encoder");

    private AudioEncodingAdapter GetAudio()
    {
        if (_audioEngineDirectory is null)
            throw new NotSupportedException("MP3 WEM encoding requires the pinned audio engine.");
        lock (_audioGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _audio ??= new AudioEncodingAdapter(_audioEngineDirectory, _scratch);
        }
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
        return before.Tags.IsEmpty && after.Tags.IsEmpty &&
            before with { Tags = ImmutableDictionary<string, string>.Empty } ==
            after with { Tags = ImmutableDictionary<string, string>.Empty };
    }

    private async Task RunConsoleAsync(string executable, string directory, FileStream snapshot,
        string[] arguments, long maximumArtifacts, CancellationToken token)
    {
        using var stage = Deadline(token, TimeSpan.FromSeconds(90));
        using var stopWatch = new CancellationTokenSource();
        var watch = WatchArtifactsAsync(directory, maximumArtifacts, stage, stopWatch.Token);
        Exception? processFailure = null;
        try
        {
            snapshot.Position = 0;
            await SeekableMediaProcess.RunAsync(executable, directory, snapshot, arguments,
                65536, stage.Token, code => new InvalidDataException($"WwiseConsole failed with exit code {code}."));
        }
        catch (Exception error) { processFailure = error; }
        finally { stopWatch.Cancel(); }
        var artifactFailure = await watch;
        if (artifactFailure is not null) throw artifactFailure;
        if (processFailure is not null)
        {
            if (processFailure is OperationCanceledException && !token.IsCancellationRequested && stage.IsCancellationRequested)
                throw new TimeoutException("WwiseConsole exceeded its execution deadline.", processFailure);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(processFailure).Throw();
        }
        CheckArtifacts(directory, maximumArtifacts);
    }

    private static async Task<Exception?> WatchArtifactsAsync(string root, long maximum, CancellationTokenSource stage,
        CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stop)) CheckArtifacts(root, maximum);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { stage.Cancel(); return error; }
        return null;
    }

    private static void CheckArtifacts(string root, long maximum)
    {
        var pending = new Stack<string>(); pending.Push(root);
        long bytes = 0; var entries = 0;
        while (pending.TryPop(out var directory))
        {
            string[] paths;
            try { paths = Directory.GetFileSystemEntries(directory); }
            catch (DirectoryNotFoundException) { continue; } // Wwise renames temporary project entries.
            foreach (var path in paths)
            {
                if (++entries > 1024) throw new InvalidDataException("Wwise scratch file count exceeds its budget.");
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Linked Wwise scratch artifacts are not supported.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                    else if ((bytes = checked(bytes + new FileInfo(path).Length)) > maximum)
                        throw new InvalidDataException("Wwise scratch data exceeds its budget.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
    }

    private async Task RequireDecodedVorbisAsync(string wemPath, string directory, FileStream source,
        WwiseWaveInput input, CancellationToken token)
    {
        var itemId = Guid.NewGuid();
        var probed = await _decoder.ProbeConversionAsync(new(itemId, wemPath), AudioFormat.Wave, token);
        var decodedPath = Path.Combine(directory, $".context-suite-{itemId:N}.tmp");
        await using (File.Create(decodedPath)) { }
        var result = await _decoder.ConvertAsync(new(probed, decodedPath, AudioFormat.Wave, AudioConversionConsent.None), token);
        if (result.DecodedFrames != input.Frames || result.SourceSha256 != probed.Sha256)
            throw new InvalidDataException("Wwise Vorbis did not decode to the expected frame count.");
        using var decoded = MediaFiles.OpenRead(decodedPath);
        await RequireRelatedVorbisSamplesAsync(source, input, decoded, token);
    }

    // Compare channels separately so an output with a missing or swapped channel
    // cannot hide behind the other channel's energy. The absolute RMS limit is
    // .15 full-scale; when source RMS exceeds .0001, relative RMS must be <= .5.
    // Buffers are frame-aligned and bounded independent of input duration.
    internal static async Task RequireRelatedVorbisSamplesAsync(FileStream source, WwiseWaveInput input,
        FileStream decoded, CancellationToken token)
    {
        decoded.Position = 0;
        if (decoded.Length < 44 || decoded.Length > AudioFileSource.MaximumFileBytes)
            throw new InvalidDataException("Decoded Wwise WAV exceeds its file budget.");
        var riff = new byte[12]; await decoded.ReadExactlyAsync(riff, token);
        if (!riff.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !riff.AsSpan(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(riff.AsSpan(4)) != decoded.Length - 8)
            throw new InvalidDataException("Decoded Wwise WAV framing is invalid.");
        var sawFormat = false; long dataOffset = -1, dataBytes = -1;
        for (var chunks = 0; decoded.Position < decoded.Length && chunks < 5; chunks++)
        {
            if (decoded.Length - decoded.Position < 8)
                throw new InvalidDataException("Decoded Wwise WAV chunk is truncated.");
            var chunk = new byte[8]; await decoded.ReadExactlyAsync(chunk, token);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            var begin = decoded.Position;
            var end = begin + size;
            if (end + (size & 1) > decoded.Length)
                throw new InvalidDataException("Decoded Wwise WAV chunk exceeds its extent.");
            if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                if (sawFormat || dataOffset >= 0 || size != 16)
                    throw new InvalidDataException("Decoded Wwise WAV format is unexpected.");
                var format = new byte[16]; await decoded.ReadExactlyAsync(format, token);
                if (BinaryPrimitives.ReadUInt16LittleEndian(format) != 3 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2)) != input.Channels ||
                    BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4)) != input.SampleRate ||
                    BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(8)) != input.SampleRate * input.Channels * 4 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(12)) != input.Channels * 4 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14)) != 32)
                    throw new InvalidDataException("Decoded Wwise WAV is not the planned float32 layout.");
                sawFormat = true;
            }
            else if (chunk.AsSpan(0, 4).SequenceEqual("smpl"u8))
            {
                if (size != 60 || dataOffset >= 0)
                    throw new InvalidDataException("Decoded Wwise WAV loop chunk is unexpected.");
            }
            else if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                if (!sawFormat || dataOffset >= 0)
                    throw new InvalidDataException("Decoded Wwise WAV data is unexpected.");
                dataOffset = begin; dataBytes = size;
            }
            else throw new InvalidDataException("Decoded Wwise WAV contains an unvalidated chunk.");
            decoded.Position = end + (size & 1);
        }
        if (decoded.Position != decoded.Length || dataOffset < 0 ||
            dataBytes != checked(input.Frames * input.Channels * 4))
            throw new InvalidDataException("Decoded Wwise WAV sample extent changed.");

        source.Position = input.DataOffset; decoded.Position = dataOffset;
        var sourceBuffer = new byte[65536]; var decodedBuffer = new byte[131072];
        var errorSquares = new double[input.Channels];
        var sourceSquares = new double[input.Channels];
        var framesPerBuffer = sourceBuffer.Length / (input.Channels * 2);
        for (long remaining = input.Frames; remaining > 0;)
        {
            var frames = (int)Math.Min(remaining, framesPerBuffer);
            await source.ReadExactlyAsync(sourceBuffer.AsMemory(0, frames * input.Channels * 2), token);
            await decoded.ReadExactlyAsync(decodedBuffer.AsMemory(0, frames * input.Channels * 4), token);
            for (var frame = 0; frame < frames; frame++)
                for (var channel = 0; channel < input.Channels; channel++)
                {
                    var sample = frame * input.Channels + channel;
                    var original = BinaryPrimitives.ReadInt16LittleEndian(sourceBuffer.AsSpan(sample * 2)) / 32768.0;
                    var actual = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(decodedBuffer.AsSpan(sample * 4)));
                    if (!float.IsFinite(actual))
                        throw new InvalidDataException("Decoded Wwise Vorbis contains nonfinite samples.");
                    var difference = original - actual;
                    sourceSquares[channel] += original * original;
                    errorSquares[channel] += difference * difference;
                }
            remaining -= frames;
        }
        for (var channel = 0; channel < input.Channels; channel++)
        {
            var sourceRms = Math.Sqrt(sourceSquares[channel] / input.Frames);
            var errorRms = Math.Sqrt(errorSquares[channel] / input.Frames);
            if (errorRms > .15 || sourceRms > .0001 && errorRms / sourceRms > .5)
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
    }

    private static async Task RequireExactPcmAsync(FileStream wave, WwiseWaveInput input, FileStream wem,
        WemHeader header, CancellationToken token)
    {
        if (header.DataSize != input.DataBytes) throw new InvalidDataException("Wwise PCM sample extent changed.");
        wave.Position = input.DataOffset; wem.Position = header.DataOffset;
        var source = new byte[65536]; var encoded = new byte[65536];
        for (long remaining = input.DataBytes; remaining > 0;)
        {
            var count = (int)Math.Min(remaining, source.Length);
            await wave.ReadExactlyAsync(source.AsMemory(0, count), token);
            await wem.ReadExactlyAsync(encoded.AsMemory(0, count), token);
            if (!source.AsSpan(0, count).SequenceEqual(encoded.AsSpan(0, count)))
                throw new InvalidDataException("Wwise PCM differs from WAV samples.");
            remaining -= count;
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream output, long maximum, CancellationToken token)
    {
        var buffer = new byte[65536]; long bytes = 0; int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            bytes = checked(bytes + count);
            if (bytes > maximum) throw new InvalidDataException("WEM output exceeds its publication budget.");
            output.Write(buffer, 0, count);
        }
    }

    private static CancellationTokenSource Deadline(CancellationToken token, TimeSpan limit)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(limit);
        return source;
    }

    private void Cleanup(string directory)
    {
        if (!Path.GetFullPath(directory).StartsWith(_scratch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(directory).StartsWith("wwise-encode-", StringComparison.Ordinal) ||
            !Directory.Exists(directory)) return;
        DeleteTree(directory);
    }

    private static void DeleteTree(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) == 0) DeleteTree(path);
                else Directory.Delete(path);
            }
            else File.Delete(path);
        }
        Directory.Delete(directory);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_audioGate) { _audio?.Dispose(); }
        _decoder.Dispose(); _scratchDirectories.Dispose();
    }
}
