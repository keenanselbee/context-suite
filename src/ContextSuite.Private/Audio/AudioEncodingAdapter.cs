using ContextSuite.Private.IO;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Audio;

// A private integration candidate, not a publication receipt. Tag inventory,
// artwork, arbitrary container metadata and production payload review remain
// separate gates. Publication requires the file adapter's verified-source and
// sample/metadata checks, a confirmed batch and application-owned reservation.
public sealed record AudioEncodingCandidate(byte[] Bytes, AudioConversionPlan Plan, AudioProbeFacts Facts,
    long? DecodedFrames, bool ExactSamples, double? MaximumSampleError, double? RootMeanSquareError,
    ImmutableArray<string> MissingTags, bool IsOptimization = false, bool SourceMetadataVerified = false);

public sealed class AudioEncodingAdapter : IDisposable
{
    public const int MaximumEncodedBytes = 64 * 1024 * 1024;
    public const long MaximumDecodedBytes = 1024L * 1024 * 1024;
    public const long MaximumFileBytes = AudioFileSource.MaximumFileBytes;
    private readonly AudioProbeAdapter _probe;
    private readonly FileStream _encoderLease;
    private readonly string _executable;
    private readonly string _scratch;
    private readonly WindowsVoiceDecoder? _voice;
    private bool _disposed;

    public AudioEncodingAdapter(string binaryDirectory, string scratchDirectory, string? windowsVoiceDirectory = null)
    {
        RequireOrdinaryPath(binaryDirectory);
        RequireOrdinaryPath(scratchDirectory);
        _executable = Path.Combine(binaryDirectory, "ffmpeg.exe");
        _scratch = Path.GetFullPath(scratchDirectory);
        _probe = new(binaryDirectory);
        try
        {
            RequireOrdinaryPath(_executable);
            _encoderLease = new(_executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(_encoderLease)) != _probe.EncoderSha256)
            { _encoderLease.Dispose(); throw new InvalidDataException("Audio encoder identity mismatch."); }
            Directory.CreateDirectory(_scratch);
            if (windowsVoiceDirectory is not null) _voice = new(windowsVoiceDirectory);
        }
        catch { _encoderLease?.Dispose(); _probe.Dispose(); throw; }
    }

    public Task<AudioEncodingCandidate> ConvertAsync(ReadOnlyMemory<byte> source, AudioFormat target,
        AudioConversionConsent accepted, CancellationToken cancellationToken) => ProcessBytesAsync(source, target, accepted, false, cancellationToken);

    public Task<AudioEncodingCandidate> OptimizeFlacAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken) =>
        ProcessBytesAsync(source, AudioFormat.Flac, AudioConversionConsent.None, true, cancellationToken);

    public Task<AudioEncodingArtifact> ConvertFileAsync(FileStream source, AudioFormat target,
        AudioConversionConsent accepted, CancellationToken cancellationToken, VideoAudioSelection? video = null) =>
        ProcessFileAsync(source, target, accepted, false, cancellationToken, video);

    public Task<AudioEncodingArtifact> OptimizeFlacFileAsync(FileStream source, CancellationToken cancellationToken) =>
        ProcessFileAsync(source, AudioFormat.Flac, AudioConversionConsent.None, true, cancellationToken);

    internal async Task<AudioFileSource> InspectVideoAsync(Guid itemId, string path, FileStream source,
        CancellationToken cancellationToken, VideoAudioSelection? selected = null, AudioFormat? target = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_probe.HasVideoAudio || source.Length is <= 0 or > MaximumFileBytes)
            throw new NotSupportedException("The bounded video audio engine is unavailable or the source exceeds its file limit.");
        var position = source.Position;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90)); var token = deadline.Token;
        try
        {
            var facts = await _probe.ProbeVideoSeekableAsync(source, _scratch, token);
            VideoAudioMetadata.RequireContainer(path, facts);
            if (!facts.Streams.Any(stream => stream.Kind == "video" && !stream.AttachedPicture))
                throw new InvalidDataException("The selected file has no video stream.");
            string? reason = null;
            if (selected is not null)
            {
                var audio = selected.Validate(facts);
                _ = VideoAudioMetadata.DescriptiveTags(facts, audio);
                _ = await _probe.InspectVideoAudioAsync(source, _scratch, audio, token);
                if (target is not null)
                    try { _ = AudioConversionPlan.Create(facts, target.Value, video: selected); }
                    catch (NotSupportedException error) { reason = error.Message; }
            }
            source.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
            var result = new AudioFileSource(itemId, path, hash, source.Length, facts,
                ConversionBlockReason: reason, Video: selected);
            result.Validate();
            return result;
        }
        finally { source.Position = position; }
    }

    internal async Task<AudioFileSource> InspectFlacAsync(Guid itemId, string path, FileStream source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException("FLAC input exceeds its file budget.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var header = await ReadFlacHeaderAsync(source, token);
        var restrictions = FlacMetadata.RecompressionRestrictions(header, rebuildSeekTable: true);
        var descriptions = FlacDescriptiveMetadata.Read(header);
        var reason = restrictions.FirstOrDefault();
        if (descriptions.Pictures.Any(picture => picture.IsLinked)) reason ??= "Linked artwork needs a location-preservation policy before optimization.";
        var facts = await _probe.ProbeSeekableAsync(source, _scratch, token);
        try { AudioConversionPlan.CreateFlacOptimization(facts); }
        catch (NotSupportedException) { reason ??= "This FLAC stream is outside the current optimization policy."; }
        source.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        var result = new AudioFileSource(itemId, path, hash, source.Length, facts, reason);
        result.Validate();
        return result;
    }

    internal async Task<AudioFileSource> InspectConversionAsync(Guid itemId, string path, FileStream source, AudioFormat target,
        CancellationToken cancellationToken, VideoAudioSelection? video = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (video is not null) return await InspectVideoAsync(itemId, path, source, cancellationToken, video, target);
        if (!Enum.IsDefined(target) || source.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException("Audio probe target or file budget is invalid.");
        var position = source.Position;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); var token = deadline.Token;
        try
        {
            if (target != AudioFormat.Wave && source.Length >= 12)
            {
                source.Position = 0; var header = new byte[12]; await source.ReadExactlyAsync(header, token);
                if (header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                {
                    var inventory = await WaveMetadata.ReadAsync(source, token, true);
                    if (!inventory.UnsupportedChunks.IsEmpty) throw new NotSupportedException("WAV information needs a preservation handler.");
                    if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(inventory.Pictures);
                    if (target == AudioFormat.M4a) M4aPictureAtoms.ForOutput(inventory.Pictures);
                }
            }
            if (target != AudioFormat.Flac && source.Length >= 4)
            {
                source.Position = 0; var marker = new byte[4]; await source.ReadExactlyAsync(marker, token);
                if (marker.AsSpan().SequenceEqual("fLaC"u8))
                {
                    var header = await ReadFlacHeaderAsync(source, token);
                    var tags = FlacConversionMetadata.Read(header, target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Vorbis or AudioFormat.Opus);
                    if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(tags);
                    if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(header.Blocks.Where(block => block.Type == 6).ToImmutableArray());
                    if (target == AudioFormat.M4a) M4aPictureAtoms.ForOutput(header.Blocks.Where(block => block.Type == 6).ToImmutableArray());
                }
            }
            if (target != AudioFormat.M4a && source.Length >= 8)
            {
                source.Position = 0; var prefix = new byte[8]; await source.ReadExactlyAsync(prefix, token);
                if (prefix.AsSpan(4).SequenceEqual("ftyp"u8))
                {
                    var inventory = await M4aMetadata.ReadAsync(source, token, target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac or AudioFormat.Vorbis or AudioFormat.Opus);
                    if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(inventory.Tags);
                    if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(inventory.Pictures);
                }
            }
            var asf = await ReadAsfAsync(source, token);
            RequireWmaProvider(asf, source.Length);
            if (asf?.Codec == "wmavoice") await _voice!.EnsureAvailableAsync(source, _scratch, token);
            if (asf is not null)
            {
                if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(asf.Tags);
                if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(asf.Pictures);
                if (target == AudioFormat.M4a) M4aPictureAtoms.ForOutput(asf.Pictures);
            }
            var facts = await _probe.ProbeSeekableAsync(source, _scratch, token);
            facts = PreserveAsfPrecision(facts, asf);
            if (facts.Container == "asf" && asf is null) throw new InvalidDataException("WMA requires a complete ASF inventory.");
            if (asf is not null)
            {
                var streams = facts.Streams.Where(stream => stream.Kind == "audio").ToArray();
                if (facts.Container != "asf" || streams.Length != 1 || streams[0].Codec != asf.Codec ||
                    streams[0].SampleRate != asf.SampleRate || streams[0].Channels != asf.Channels)
                    throw new InvalidDataException("WMA inventory and native audio interpretation disagree.");
            }
            if (facts.Container == "aiff")
            {
                var inventory = await AiffMetadata.ReadAsync(source, token);
                inventory.RequireConversionSupport();
                if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(inventory.Tags);
                if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(inventory.Pictures);
                if (target == AudioFormat.M4a) M4aPictureAtoms.ForOutput(inventory.Pictures);
            }
            if (target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a && facts.Container == "ogg")
            {
                var inventory = await OggMetadata.ReadAsync(source, token, true);
                var pictures = OggPictureComments.Read(inventory.Descriptions);
                if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(inventory.ConversionTags(pictures));
                if (target is AudioFormat.Wave or AudioFormat.Mp3) Mp3PictureFrames.ForOutput(pictures);
                else M4aPictureAtoms.ForOutput(pictures);
            }
            if (target is AudioFormat.Wave or AudioFormat.M4a && facts.Container == "mp3")
            {
                var inventory = await Mp3Metadata.ReadAsync(source, token, true);
                if (target == AudioFormat.Wave) WaveTextFrames.ForOutput(inventory.Tags);
                if (target == AudioFormat.Wave) Mp3PictureFrames.ForOutput(inventory.Pictures);
                else M4aPictureAtoms.ForOutput(inventory.Pictures);
            }
            string? reason = null;
            try { AudioConversionPlan.Create(facts, target); }
            catch (NotSupportedException) { reason = "This audio cannot use the selected format without changing an unsupported feature."; }
            source.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
            var result = new AudioFileSource(itemId, path, hash, source.Length, facts, ConversionBlockReason: reason);
            result.Validate(); return result;
        }
        finally { source.Position = position; }
    }

    // Compatibility for small isolated fixtures. The worker uses the file API
    // rather than serializing complete media in an IPC frame.
    private async Task<AudioEncodingCandidate> ProcessBytesAsync(ReadOnlyMemory<byte> source, AudioFormat target,
        AudioConversionConsent accepted, bool optimize, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (source.IsEmpty || source.Length > MaximumEncodedBytes) throw new InvalidDataException("Use the file API for larger audio inputs.");
        RequireOrdinaryPath(_scratch);
        var path = Path.Combine(_scratch, "audio-bytes-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await writer.WriteAsync(source.ToArray(), token);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var artifact = await ProcessFileAsync(input, target, accepted, optimize, token);
            if (artifact.Content.Length > MaximumEncodedBytes) throw new InvalidDataException("Use the file API for larger audio outputs.");
            var bytes = new byte[checked((int)artifact.Content.Length)];
            await artifact.Content.ReadExactlyAsync(bytes, token);
            var result = artifact.Result;
            return new(bytes, result.Plan, result.Facts, result.DecodedFrames, result.ExactSamples,
                result.MaximumSampleError, result.RootMeanSquareError, result.MissingTags, result.IsOptimization, result.SourceMetadataVerified);
        }
        finally { if (File.Exists(path)) { RequireOrdinaryPath(path); File.Delete(path); } }
    }

    private async Task<AudioEncodingArtifact> ProcessFileAsync(FileStream source, AudioFormat target,
        AudioConversionConsent accepted, bool optimize, CancellationToken cancellationToken, VideoAudioSelection? video = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (video is not null && optimize) throw new InvalidDataException("Video extraction cannot optimize FLAC in place.");
        if (!source.CanRead || source.CanWrite || !source.CanSeek || source.Length is <= 0 or > MaximumFileBytes ||
            ((uint)File.GetAttributes(source.SafeFileHandle) & (0x10 | 0x40 | 0x400 | 0x1000 | 0x40000 | 0x400000)) != 0)
            throw new InvalidDataException("Audio requires a bounded, read-only regular file handle without placeholder attributes.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        RequireOrdinaryPath(_scratch);
        var directory = Path.Combine(_scratch, "audio-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var inputPath = Path.Combine(directory, "input.snapshot");
        var outputPath = Path.Combine(directory, "encoded.candidate");
        var reconciledPath = Path.Combine(directory, "reconciled.flac");
        var referencePath = Path.Combine(directory, "reference.f64le");
        var extendedPath = Path.Combine(directory, "finite-extended.wav");
        var finiteEncodingPath = Path.Combine(directory, "finite-encoding.wav");
        var voicePath = Path.Combine(directory, "voice-decoded.wav");
        var ownedPaths = new[] { inputPath, outputPath, reconciledPath, referencePath, extendedPath, finiteEncodingPath, voicePath };
        FileStream? input = null, output = null;
        FileStream? voiceInput = null;
        var retained = false;
        var position = source.Position;
        try
        {
            source.Position = 0;
            var sourceLength = source.Length;
            (long Bytes, string Sha256) copied;
            await using (var writer = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                copied = await CopyBoundedAsync(source, writer, MaximumFileBytes, token);
            if (copied.Bytes != sourceLength || source.Length != sourceLength) throw new IOException("The audio source changed while copying.");
            input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(await SHA256.HashDataAsync(input, token)) != copied.Sha256) throw new IOException("The audio snapshot changed.");
            var sourceHeader = optimize ? await ReadFlacHeaderAsync(input, token) : null;
            if (sourceHeader is not null && !FlacMetadata.RecompressionRestrictions(sourceHeader, rebuildSeekTable: true).IsEmpty)
                throw new NotSupportedException("This FLAC metadata needs a preservation handler before optimization.");
            if (sourceHeader is not null && FlacDescriptiveMetadata.Read(sourceHeader).Pictures.Any(picture => picture.IsLinked))
                throw new NotSupportedException("Linked FLAC artwork needs a location-preservation policy before optimization.");
            var seekTable = sourceHeader?.Blocks.SingleOrDefault(block => block.Type == 3);
            if (sourceHeader is not null)
            {
                var sourceFrames = await _probe.ReadFlacFramesAsync(input, sourceHeader!, directory, token);
                if (seekTable is not null) FlacSeekTable.RequireMatches(seekTable.Data.AsSpan(), sourceFrames);
            }
            ImmutableArray<FlacFrame> encodedFrames = default;
            FlacMetadataHeader? convertingFlac = null;
            ImmutableDictionary<string, string>? flacTags = null;
            if (!optimize && target != AudioFormat.Flac && input.Length >= 4)
            {
                input.Position = 0; var marker = new byte[4]; await input.ReadExactlyAsync(marker, token);
                if (marker.AsSpan().SequenceEqual("fLaC"u8))
                {
                    convertingFlac = await ReadFlacHeaderAsync(input, token);
                    flacTags = FlacConversionMetadata.Read(convertingFlac, target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Vorbis or AudioFormat.Opus);
                }
                input.Position = 0;
            }
            M4aMetadataInventory? m4a = null;
            if (video is null && !optimize && target != AudioFormat.M4a && input.Length >= 8)
            {
                input.Position = 0;
                var prefix = new byte[8]; await input.ReadExactlyAsync(prefix, token);
                if (prefix.AsSpan(4).SequenceEqual("ftyp"u8)) m4a = await M4aMetadata.ReadAsync(input, token, target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac or AudioFormat.Vorbis or AudioFormat.Opus);
                input.Position = 0;
            }
            var asf = await ReadAsfAsync(input, token);
            RequireWmaProvider(asf, input.Length);
            var facts = video is null ? await _probe.ProbeSeekableAsync(input, directory, token) :
                await _probe.ProbeVideoSeekableAsync(input, directory, token);
            facts = PreserveAsfPrecision(facts, asf);
            if (asf is not null && facts.Container != "asf")
                throw new InvalidDataException("WMA inventory and native container interpretation disagree.");
            var plan = optimize ? AudioConversionPlan.CreateFlacOptimization(facts) : AudioConversionPlan.Create(facts, target, video: video);
            var sourceAudio = video is null ? facts.Streams.Single(stream => stream.Kind == "audio") : video.Validate(facts);
            plan.RequireConsent(accepted);
            var videoInventory = video is null ? null : await _probe.InspectVideoAudioAsync(input, directory, sourceAudio, token);
            var originalTags = video is null ? DescriptiveTags(facts with { Streams = [sourceAudio] }) :
                VideoAudioMetadata.DescriptiveTags(facts, sourceAudio);
            WaveMetadataInventory? wave = null;
            AiffMetadataInventory? aiff = null;
            ImmutableDictionary<string, string>? commentTags = null;
            ImmutableArray<FlacMetadataBlock> pictures = [];
            if (facts.Container == "asf")
            {
                if (asf is null || sourceAudio.Codec != asf.Codec || sourceAudio.SampleRate != asf.SampleRate || sourceAudio.Channels != asf.Channels)
                    throw new InvalidDataException("WMA inventory and native audio interpretation disagree.");
                commentTags = asf.Tags; pictures = asf.Pictures;
                originalTags = new Dictionary<string, string>(asf.Tags, StringComparer.OrdinalIgnoreCase);
            }
            if (facts.Container == "aiff")
            {
                aiff = await AiffMetadata.ReadAsync(input, token);
                aiff.RequireConversionSupport();
                if (sourceAudio.Codec != aiff.Codec || sourceAudio.SampleRate != aiff.SampleRate || sourceAudio.Channels != aiff.Channels)
                    throw new InvalidDataException("AIFF inventory and native audio interpretation disagree.");
                commentTags = aiff.Tags;
                pictures = aiff.Pictures;
                // The complete container inventory owns tags, including values
                // after SSND and ID3 values that native probing can truncate.
                originalTags = new Dictionary<string, string>(aiff.Tags, StringComparer.OrdinalIgnoreCase);
            }
            if (!plan.AlreadyTarget && facts.Container == "wav")
            {
                wave = await WaveMetadata.ReadAsync(input, token, true);
                wave.RequireConversionSupport(originalTags);
                pictures = wave.Pictures;
                commentTags = AudioCommentConversion.Read(wave.Tags.Select(tag => new AudioComment(tag.Name, tag.Value)), mapVorbisAliases: false);
                var codec = wave.FloatingPoint ? $"pcm_f{wave.SampleBits}le" : wave.SampleBits == 8 ? "pcm_u8" : $"pcm_s{wave.SampleBits}le";
                if (sourceAudio.Codec != codec || sourceAudio.SampleRate != wave.SampleRate || sourceAudio.Channels != wave.Channels)
                    throw new InvalidDataException("WAV inventory and native audio interpretation disagree.");
            }
            if (!optimize && !plan.AlreadyTarget && facts.Container == "flac")
            {
                convertingFlac ??= await ReadFlacHeaderAsync(input, token);
                commentTags = flacTags ?? FlacConversionMetadata.Read(convertingFlac, target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Vorbis or AudioFormat.Opus);
                pictures = convertingFlac.Blocks.Where(block => block.Type == 6).ToImmutableArray();
                var frames = await _probe.ReadFlacFramesAsync(input, convertingFlac, directory, token);
                var seek = convertingFlac.Blocks.SingleOrDefault(block => block.Type == 3);
                if (seek is not null) FlacSeekTable.RequireMatches(seek.Data.AsSpan(), frames);
            }
            if (!plan.AlreadyTarget && facts.Container == "ogg")
            {
                var preservePictures = target is AudioFormat.Wave or AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Flac or AudioFormat.Vorbis or AudioFormat.Opus;
                var ogg = await OggMetadata.ReadAsync(input, token, preservePictures);
                if (sourceAudio.Codec != ogg.Codec || sourceAudio.SampleRate != ogg.SampleRate || sourceAudio.Channels != ogg.Channels)
                    throw new InvalidDataException("Ogg inventory and native audio interpretation disagree.");
                pictures = preservePictures ? OggPictureComments.Read(ogg.Descriptions) : [];
                commentTags = preservePictures ? ogg.ConversionTags(pictures) : ogg.ConversionTags();
            }
            if (!plan.AlreadyTarget && facts.Container == "mp3")
            {
                var mp3 = await Mp3Metadata.ReadAsync(input, token, target is AudioFormat.Wave or AudioFormat.M4a or AudioFormat.Flac or AudioFormat.Vorbis or AudioFormat.Opus);
                if (sourceAudio.SampleRate != mp3.SampleRate || sourceAudio.Channels != mp3.Channels)
                    throw new InvalidDataException("MP3 inventory and native audio interpretation disagree.");
                commentTags = mp3.Tags;
                pictures = mp3.Pictures;
                // The pinned probe truncates some v2.3 unsynchronised values.
                // The complete managed inventory owns these tags; encoding below
                // disables inherited metadata and supplies every admitted value.
                originalTags = new Dictionary<string, string>(mp3.Tags, StringComparer.OrdinalIgnoreCase);
            }
            if (video is null && !plan.AlreadyTarget && facts.Container == "mov,mp4,m4a,3gp,3g2,mj2")
            {
                if (m4a is null) throw new NotSupportedException("M4A conversion requires the reviewed local AAC container profile.");
                if (sourceAudio.Codec != "aac" || sourceAudio.SampleRate != m4a.SampleRate || sourceAudio.Channels != m4a.Channels)
                    throw new InvalidDataException("M4A inventory and native audio interpretation disagree.");
                commentTags = m4a.Tags;
                pictures = m4a.Pictures;
            }
            if (video is not null)
                commentTags = originalTags.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
            if (commentTags is not null)
            {
                AudioCommentConversion.RequireTags(commentTags, originalTags);
            }
            var waveId3Tags = target == AudioFormat.Wave && !plan.AlreadyTarget ?
                WaveTextFrames.ForOutput(originalTags) :
                ImmutableDictionary<string, string>.Empty;
            var m4aLanguage = target == AudioFormat.M4a && !plan.AlreadyTarget ? originalTags.GetValueOrDefault("language") : null;
            if (target is AudioFormat.Wave or AudioFormat.Mp3 && !pictures.IsEmpty) pictures = Mp3PictureFrames.ForOutput(pictures);
            if (target == AudioFormat.M4a && !pictures.IsEmpty) pictures = M4aPictureAtoms.ForOutput(pictures);
            AudioSampleComparison? comparison = null;
            ImmutableArray<string> missing = [];
            var outputFacts = facts;
            var useOriginal = plan.AlreadyTarget;
            FlacMetadataHeader? picturedFlac = null;
            if (!plan.AlreadyTarget)
            {
                long? voiceFrames = null;
                if (asf?.Codec == "wmavoice")
                {
                    voiceFrames = await _voice!.DecodeWaveAsync(input, asf, voicePath, token);
                    voiceInput = File.OpenRead(voicePath);
                }
                var decodedInput = voiceInput ?? input;
                long? expectedResampledFrames = null;
                long minimumResampledFrames = 0;
                byte[]? finiteSource = null;
                if (plan.SampleRate != sourceAudio.SampleRate)
                {
                    // Count decoded source frames before resampling. A shared
                    // resampler defect must not shorten both encoding and its
                    // reference and thereby pass the signal comparison.
                    using var prefix = new ShortAudioResampling.Prefix(plan.Channels);
                    var original = await RunFileAsync(decodedInput, DecodeArguments(sourceAudio.SampleRate!.Value,
                            video is null ? null : sourceAudio.Index, video is not null && plan.Channels < sourceAudio.Channels, videoInventory?.PresentedFrames),
                        (decoded, readToken) => AudioSampleValidation.CaptureAsync(decoded, prefix,
                            plan.Channels, MaximumDecodedBytes, readToken), token);
                    if (aiff is not null && original.Frames != aiff.SampleFrames ||
                        wave is not null && original.Frames != wave.SampleFrames ||
                        convertingFlac is { SampleFrames: > 0 } && original.Frames != convertingFlac.SampleFrames)
                        throw new InvalidDataException("Decoded source length differs from its inventoried sample extent.");
                    expectedResampledFrames = checked((original.Frames * plan.SampleRate + sourceAudio.SampleRate.Value - 1) /
                        sourceAudio.SampleRate.Value);
                    minimumResampledFrames = Math.Max(1, checked(original.Frames * plan.SampleRate / sourceAudio.SampleRate.Value));
                    if (original.Frames <= ShortAudioResampling.MaximumSourceFrames) finiteSource = prefix.ToArray();
                }
                // Capture a reference at the planned rate before encoding. This
                // gives resampled output a time-aligned signal comparison too.
                AudioSampleSummary captured;
                await using (var reference = new FileStream(referencePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    if (finiteSource is not null)
                    {
                        var extended = ShortAudioResampling.Wave(finiteSource, sourceAudio.SampleRate!.Value,
                            plan.Channels, sourceAudio.ChannelLayout, true);
                        await using (var extendedWriter = new FileStream(extendedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await extendedWriter.WriteAsync(extended, token);
                        byte[] decoded;
                        using (var extendedInput = File.OpenRead(extendedPath))
                            decoded = await RunFileAsync(extendedInput, DecodeArguments(plan.SampleRate), ShortAudioResampling.MaximumNativeBytes, token);
                        var extendedFrames = finiteSource.Length / (plan.Channels * 8) + ShortAudioResampling.ExtensionFrames;
                        var extendedNumerator = (long)extendedFrames * plan.SampleRate;
                        var extendedFloor = extendedNumerator / sourceAudio.SampleRate.Value;
                        var extendedCeiling = (extendedNumerator + sourceAudio.SampleRate.Value - 1) / sourceAudio.SampleRate.Value;
                        if (decoded.LongLength < extendedFloor * plan.Channels * 8 || decoded.LongLength > extendedCeiling * plan.Channels * 8)
                            throw new InvalidDataException("Finite resampling did not preserve its extended sample extent.");
                        using var complete = new MemoryStream(decoded, false);
                        await AudioSampleValidation.CaptureAsync(complete, Stream.Null, plan.Channels, ShortAudioResampling.MaximumNativeBytes, token);
                        var bytes = checked((int)expectedResampledFrames!.Value * plan.Channels * 8);
                        using var interval = new MemoryStream(decoded, 0, bytes, false);
                        captured = await AudioSampleValidation.CaptureAsync(interval, reference, plan.Channels, MaximumDecodedBytes, token);
                        var waveBytes = ShortAudioResampling.Wave(decoded.AsSpan(0, bytes), plan.SampleRate,
                            plan.Channels, sourceAudio.ChannelLayout, false);
                        await using var finiteWriter = new FileStream(finiteEncodingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await finiteWriter.WriteAsync(waveBytes, token);
                    }
                    else
                        captured = await RunFileAsync(decodedInput, DecodeArguments(plan.SampleRate,
                                video is null ? null : sourceAudio.Index, video is not null && plan.Channels < sourceAudio.Channels, videoInventory?.PresentedFrames),
                            (decoded, readToken) => AudioSampleValidation.CaptureAsync(decoded, reference, plan.Channels, MaximumDecodedBytes, readToken), token);
                }
                // Fractional duration permits the two neighboring whole-sample
                // counts. This is less than one output sample, not a tolerance
                // for a resampler dropping several samples during flushing.
                if (expectedResampledFrames is not null && (captured.Frames < minimumResampledFrames || captured.Frames > expectedResampledFrames))
                    throw new InvalidDataException("Resampled audio length differs from the source duration. No shortened output can be accepted.");
                if (wave is not null && plan.SampleRate == wave.SampleRate && captured.Frames != wave.SampleFrames)
                    throw new InvalidDataException("Decoded WAV length differs from its inventoried sample extent.");
                if (aiff is not null && plan.SampleRate == aiff.SampleRate && captured.Frames != aiff.SampleFrames)
                    throw new InvalidDataException("Decoded AIFF length differs from its inventoried sample extent.");
                if (voiceFrames is not null && plan.SampleRate == sourceAudio.SampleRate && captured.Frames != voiceFrames)
                    throw new InvalidDataException("WMA Voice sample transport changed its decoded extent.");
                if (convertingFlac is { SampleFrames: > 0 } && plan.SampleRate == convertingFlac.SampleRate && captured.Frames != convertingFlac.SampleFrames)
                    throw new InvalidDataException("Decoded FLAC length differs from its validated sample extent.");
                if (videoInventory is not null && captured.Frames != videoInventory.PresentedFrames)
                    throw new InvalidDataException("Selected video audio length differs from its packet and presentation extent.");
                // ASF millisecond packet timestamps cannot express each sample
                // boundary. Encode the validated decoded sequence at the exact
                // sample clock, without adding, dropping or changing samples.
                if (asf is not null && finiteSource is null)
                {
                    using var reference = File.OpenRead(referencePath);
                    await using var writer = new FileStream(finiteEncodingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await writer.WriteAsync(DecodedAudioWave.Header(reference.Length, plan.SampleRate, plan.Channels, sourceAudio.ChannelLayout), token);
                    await CopyBoundedAsync(reference, writer, MaximumDecodedBytes, token);
                }
                using (var finiteInput = finiteSource is null && asf is null ? null : File.OpenRead(finiteEncodingPath))
                    await RunFileAsync(finiteInput ?? input, EncodingArguments(plan, facts, originalTags, outputPath, optimize,
                        captured.Frames, video, videoInventory?.PresentedFrames, video is not null && plan.Channels < sourceAudio.Channels), 65536, token);
                RequireOrdinaryPath(outputPath);
                if (target == AudioFormat.Mp3 && captured.Frames < 47)
                {
                    using var mp3Candidate = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    await Mp3GaplessPadding.ReconcileAsync(mp3Candidate, captured.Frames, token);
                }
                if (sourceHeader is not null)
                {
                    using var raw = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var encodedHeader = await ReadFlacHeaderAsync(raw, token);
                    encodedFrames = await _probe.ReadFlacFramesAsync(raw, encodedHeader, directory, token);
                    var header = FlacMetadata.CreateRecompressionHeader(sourceHeader, encodedHeader, encodedFrames);
                    await using var writer = new FileStream(reconciledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                    await writer.WriteAsync(header, token);
                    raw.Position = encodedHeader.AudioOffset;
                    await CopyBoundedAsync(raw, writer, MaximumFileBytes - header.Length, token);
                }
                if (!pictures.IsEmpty || !waveId3Tags.IsEmpty || m4aLanguage is not null)
                {
                    using var raw = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var writer = new FileStream(reconciledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                    if (target == AudioFormat.Flac)
                    {
                        var encodedHeader = await ReadFlacHeaderAsync(raw, token);
                        if (encodedHeader.Blocks.Any(block => block.Type is not (0 or 1 or 4)))
                            throw new InvalidDataException("Encoded FLAC has unexpected metadata before adding pictures.");
                        picturedFlac = encodedHeader with { Blocks = encodedHeader.Blocks.AddRange(pictures) };
                        var header = FlacMetadata.CreateRecompressionHeader(picturedFlac, encodedHeader);
                        await writer.WriteAsync(header, token);
                        raw.Position = encodedHeader.AudioOffset;
                        await CopyBoundedAsync(raw, writer, MaximumFileBytes - header.Length, token);
                    }
                    else if (target == AudioFormat.Wave) await WavePictureWriter.RewriteAsync(raw, writer, pictures, token, waveId3Tags);
                    else if (target == AudioFormat.Mp3) await Mp3PictureWriter.RewriteAsync(raw, writer, pictures, token);
                    else if (target == AudioFormat.M4a) await M4aPictureWriter.RewriteAsync(raw, writer, pictures, token, m4aLanguage);
                    else await OggPictureWriter.RewriteAsync(raw, writer, pictures, token);
                }
                var candidate = sourceHeader is null && pictures.IsEmpty && waveId3Tags.IsEmpty && m4aLanguage is null ? outputPath : reconciledPath;
                RequireOrdinaryPath(candidate);
                output = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (output.Length is <= 0 or >= MaximumFileBytes) throw new InvalidDataException("Encoded audio reached its output limit.");
                if (sourceHeader is not null) FlacMetadata.RequirePreservedMetadata(sourceHeader, await ReadFlacHeaderAsync(output, token), encodedFrames);
                if (picturedFlac is not null) FlacMetadata.RequirePreservedMetadata(picturedFlac, await ReadFlacHeaderAsync(output, token));
                outputFacts = video is null ? await _probe.ProbeSeekableAsync(output, directory, token) :
                    await _probe.ProbeVideoSeekableAsync(output, directory, token);
                var outputPlan = optimize ? AudioConversionPlan.CreateFlacOptimization(outputFacts) : AudioConversionPlan.Create(outputFacts, target);
                var outputAudio = outputFacts.Streams.Single(stream => stream.Kind == "audio");
                if ((!optimize && !outputPlan.AlreadyTarget) || outputAudio.Codec != plan.OutputCodec ||
                    outputAudio.SampleRate != plan.SampleRate || outputAudio.Channels != plan.Channels ||
                    video is null && !facts.Streams.Where(stream => stream.AttachedPicture).Select(stream => stream.Codec)
                        .SequenceEqual(outputFacts.Streams.Where(stream => stream.AttachedPicture).Select(stream => stream.Codec)))
                    throw new InvalidDataException("Encoded audio differs from its planned container, codec, rate or channels.");
                if (plan.Channels > 2 && outputAudio.ChannelLayout != sourceAudio.ChannelLayout)
                    throw new InvalidDataException("Encoded audio changed its channel layout.");
                if (video is not null && (!outputFacts.Chapters.IsDefaultOrEmpty ||
                    outputFacts.Streams.Any(stream => stream.Kind != "audio")))
                    throw new InvalidDataException("Extracted audio retained an unapproved chapter or non-audio stream.");
                if (target == AudioFormat.Flac && outputAudio.SampleBits != plan.SampleBits)
                    throw new InvalidDataException("Encoded FLAC changed its planned precision.");
                var outputTags = DescriptiveTags(outputFacts with { Streams = [outputAudio] });
                if (target == AudioFormat.Wave)
                {
                    var encodedWave = await WaveMetadata.ReadAsync(output, token, true);
                    encodedWave.RequireConversionSupport(outputTags);
                    if (encodedWave.Pictures.Length != pictures.Length || encodedWave.Pictures.Where((block, index) =>
                        !block.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any())
                        throw new InvalidDataException("Encoded WAV did not preserve its planned pictures.");
                }
                if (target == AudioFormat.Mp3 && !pictures.IsEmpty)
                {
                    var encodedMp3 = await Mp3Metadata.ReadAsync(output, token, true);
                    if (encodedMp3.Pictures.Length != pictures.Length || encodedMp3.Pictures.Where((block, index) =>
                        !block.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any())
                        throw new InvalidDataException("Encoded MP3 did not preserve its planned pictures.");
                    AudioCommentConversion.RequireTags(encodedMp3.Tags, outputTags);
                }
                if (target == AudioFormat.M4a && (!pictures.IsEmpty || m4aLanguage is not null))
                {
                    var encodedM4a = await M4aMetadata.ReadAsync(output, token, true);
                    if (encodedM4a.Pictures.Length != pictures.Length || encodedM4a.Pictures.Where((block, index) =>
                        !block.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any())
                        throw new InvalidDataException("Encoded M4A did not preserve its planned pictures.");
                    AudioCommentConversion.RequireTags(encodedM4a.Tags, outputTags);
                }
                if (target is AudioFormat.Vorbis or AudioFormat.Opus)
                {
                    var encodedOgg = await OggMetadata.ReadAsync(output, token, !pictures.IsEmpty);
                    if (encodedOgg.Codec != outputAudio.Codec || encodedOgg.SampleRate != plan.SampleRate || encodedOgg.Channels != plan.Channels)
                        throw new InvalidDataException("Encoded Ogg framing differs from its planned audio properties.");
                    AudioCommentConversion.RequireTags(pictures.IsEmpty ? encodedOgg.ConversionTags() : encodedOgg.ConversionTags(pictures), outputTags);
                }
                missing = originalTags.Where(tag => !outputTags.TryGetValue(tag.Key, out var value) || value != tag.Value).Select(tag => tag.Key).ToImmutableArray();
                if (wave is not null && !missing.IsEmpty) throw new InvalidDataException("Converted audio did not retain the admitted WAV information.");
                if (commentTags is not null)
                {
                    AudioCommentConversion.RequireTags(commentTags, outputTags);
                    if (!missing.IsEmpty) throw new InvalidDataException("Converted audio did not retain the admitted audio comments.");
                }
                using (var reference = new FileStream(referencePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
                {
                    if (Convert.ToHexString(await SHA256.HashDataAsync(reference, token)) != captured.Sha256) throw new IOException("The audio reference changed.");
                    reference.Position = 0;
                    comparison = await RunFileAsync(output, DecodeArguments(plan.SampleRate),
                        (decoded, readToken) => AudioSampleValidation.CompareAsync(reference, decoded, plan.Channels, MaximumDecodedBytes, readToken), token);
                }
                if (comparison.Frames != captured.Frames) throw new InvalidDataException("Encoded audio changed its decoded sample count.");
                if (plan.RequiresExactSamples && comparison.MaximumError != 0) throw new InvalidDataException("A lossless conversion changed decoded samples.");
                if (comparison.RootMeanSquareError > .15 || (target == AudioFormat.Flac && comparison.MaximumError > 1.0 / 8388608))
                    throw new ImageFailureException(ImageFailure.ValidationFailed);
                if (optimize && (comparison.MaximumError != 0 || !missing.IsEmpty)) throw new InvalidDataException("FLAC optimization did not preserve samples and metadata.");
                useOriginal = optimize && output.Length >= input.Length;
            }
            source.Position = 0;
            if (source.Length != sourceLength || Convert.ToHexString(await SHA256.HashDataAsync(source, token)) != copied.Sha256)
                throw new IOException("The original audio changed during the operation.");
            var selected = useOriginal ? input : output!;
            selected.Position = 0;
            var outputHash = Convert.ToHexString(await SHA256.HashDataAsync(selected, token));
            selected.Position = 0;
            var result = new AudioFileEncodingResult(plan, useOriginal ? facts : outputFacts, comparison?.Frames,
                useOriginal || comparison?.MaximumError == 0 && plan.SampleRate == sourceAudio.SampleRate,
                useOriginal ? 0 : comparison?.MaximumError, useOriginal ? 0 : comparison?.RootMeanSquareError,
                useOriginal ? [] : missing, optimize, sourceLength, selected.Length, copied.Sha256, outputHash,
                plan.AlreadyTarget || optimize || wave is not null || commentTags is not null || videoInventory is not null);
            if (useOriginal) { output?.Dispose(); output = null; } else { input.Dispose(); input = null; }
            voiceInput?.Dispose(); voiceInput = null;
            foreach (var path in ownedPaths)
                if (path != selected.Name && File.Exists(path)) await DeleteOwnedFileAsync(path);
            var artifact = new AudioEncodingArtifact(directory, selected, result);
            if (useOriginal) input = null; else output = null;
            retained = true;
            return artifact;
        }
        finally
        {
            input?.Dispose(); output?.Dispose(); voiceInput?.Dispose();
            if (!retained)
            {
                foreach (var path in ownedPaths)
                    if (File.Exists(path)) await DeleteOwnedFileAsync(path);
                Directory.Delete(directory, false);
            }
            try { if (source.CanSeek) source.Position = position; }
            catch (ObjectDisposedException) { /* A caller closing its handle must not strand owned scratch. */ }
        }
    }

    internal static async Task DeleteOwnedFileAsync(string path)
    {
        // Native exit and reader completion precede cleanup. Windows can still
        // release a file handle slightly later. Retry sharing violations only,
        // revalidating the exact owned path each time; never sweep a directory.
        for (var attempt = 0; ; attempt++)
        {
            try { RequireOrdinaryPath(path); File.Delete(path); return; }
            catch (IOException error) when ((error.HResult & 0xffff) == 32 && attempt < 20)
            { await Task.Delay(100); }
        }
    }

    private string[] NativeInput => ["-hide_banner", "-v", "error", "-nostdin", "-n", "-xerror", "-err_detect", "explode",
        "-protocol_whitelist", "fd", "-format_whitelist", _probe.InputFormats, "-threads", "1", "-filter_threads", "1", "-i", "fd:"];

    private Task<byte[]> RunFileAsync(FileStream input, IEnumerable<string> arguments, int maximumOutput, CancellationToken token)
    {
        input.Position = 0;
        return SeekableMediaProcess.RunAsync(_executable, _scratch, input, NativeInput.Concat(arguments), maximumOutput, token);
    }

    private Task<T> RunFileAsync<T>(FileStream input, IEnumerable<string> arguments,
        Func<Stream, CancellationToken, Task<T>> consume, CancellationToken token)
    {
        input.Position = 0;
        return SeekableMediaProcess.RunAsync(_executable, _scratch, input, NativeInput.Concat(arguments), consume, token);
    }

    private static string[] DecodeArguments(int rate, int? sourceIndex = null, bool downmix = false, long? trimFrames = null)
    {
        var arguments = new List<string> { "-map", sourceIndex is { } index ? $"0:{index}" : "0:a:0",
            "-vn", "-sn", "-dn", "-threads", "1", "-ar", rate.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (downmix) arguments.AddRange(["-ac", "2"]);
        if (trimFrames is { } count) arguments.AddRange(["-af", $"atrim=end_sample={count},asetpts=PTS-STARTPTS"]);
        arguments.AddRange(["-c:a", "pcm_f64le", "-f", "f64le", "pipe:1"]);
        return arguments.ToArray();
    }

    private static async Task<(long Bytes, string Sha256)> CopyBoundedAsync(Stream source, Stream target, long maximum, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long bytes = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            if (count > maximum - bytes) throw new InvalidDataException("Audio data exceeds the file budget.");
            await target.WriteAsync(buffer.AsMemory(0, count), token);
            hash.AppendData(buffer, 0, count); bytes += count;
        }
        return (bytes, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static async Task<FlacMetadataHeader> ReadFlacHeaderAsync(FileStream source, CancellationToken token)
    {
        source.Position = 0;
        var marker = new byte[4];
        await source.ReadExactlyAsync(marker, token);
        if (!marker.AsSpan().SequenceEqual("fLaC"u8)) throw new InvalidDataException("FLAC optimization requires native FLAC.");
        using var header = new MemoryStream();
        header.Write(marker);
        for (var index = 0; index < FlacMetadata.MaximumBlocks; index++)
        {
            await source.ReadExactlyAsync(marker, token);
            var length = (marker[1] << 16) | (marker[2] << 8) | marker[3];
            if (header.Length + 4 + length > FlacMetadata.MaximumHeaderBytes || length > source.Length - source.Position)
                throw new InvalidDataException("FLAC metadata exceeds its byte budget or is truncated.");
            header.Write(marker);
            var data = new byte[length];
            await source.ReadExactlyAsync(data, token); header.Write(data);
            if ((marker[0] & 128) != 0) return FlacMetadata.Parse(header.ToArray());
        }
        throw new InvalidDataException("FLAC metadata exceeds its block budget.");
    }

    private static async Task<AsfMetadataInventory?> ReadAsfAsync(FileStream source, CancellationToken token)
    {
        if (source.Length < 16) return null;
        var position = source.Position;
        try
        {
            source.Position = 0; var marker = new byte[16]; await source.ReadExactlyAsync(marker, token);
            if (new Guid(marker) != new Guid("75b22630-668e-11cf-a6d9-00aa0062ce6c")) return null;
            var inventory = await AsfMetadata.ReadAsync(source, token);
            inventory.RequireConversionSupport(); return inventory;
        }
        finally { source.Position = position; }
    }

    private void RequireWmaProvider(AsfMetadataInventory? inventory, long inputBytes)
    {
        if (inventory?.Codec is "wmapro" or "wmalossless" && !_probe.HasRepairedWma)
            throw new NotSupportedException("WMA Professional and Lossless require the verified expanded decoder. Originals were kept.");
        if (inventory?.Codec != "wmavoice") return;
        if (_voice is null) throw new NotSupportedException("WMA Voice conversion requires the verified Windows Voice decoder. Originals were kept.");
        WindowsVoiceDecoder.RequireSupport(inventory, inputBytes);
    }

    // ffprobe omits raw precision for some PCM16 WMA Lossless streams. The
    // complete ASF inventory owns it; a conflicting native report is a refusal.
    internal static AudioProbeFacts PreserveAsfPrecision(AudioProbeFacts facts, AsfMetadataInventory? inventory)
    {
        if (inventory?.Codec != "wmalossless") return facts;
        var audio = facts.Streams.Where(stream => stream.Kind == "audio").ToArray();
        if (facts.Container != "asf" || audio.Length != 1 || audio[0].Codec != inventory.Codec ||
            audio[0].SampleRate != inventory.SampleRate || audio[0].Channels != inventory.Channels ||
            inventory.SampleBits is not (16 or 24) || audio[0].SampleBits is { } bits && bits != inventory.SampleBits)
            throw new InvalidDataException("WMA Lossless precision or stream facts disagree with the complete inventory.");
        return facts with { Streams = facts.Streams.Select(stream => stream.Kind == "audio" ? stream with { SampleBits = inventory.SampleBits } : stream).ToImmutableArray() };
    }

    private static List<string> EncodingArguments(AudioConversionPlan plan, AudioProbeFacts facts,
        Dictionary<string, string> originalTags, string outputPath, bool preserveRawMetadata, long decodedFrames,
        VideoAudioSelection? video = null, long? trimFrames = null, bool downmix = false)
    {
        var target = plan.Target;
        var inventoriedTags = video is not null || facts.Container is "mp3" or "aiff" or "asf";
        var explicitMetadata = inventoriedTags || target == AudioFormat.Wave;
        var arguments = new List<string> { "-map", video is { } selected ? $"0:{selected.StreamIndex}" : "0:a:0", "-vn", "-sn", "-dn", "-map_metadata",
            explicitMetadata ? "-1" : facts.Container == "ogg" ? "0:s:a:0" : "0", "-map_metadata:s:a:0", explicitMetadata ? "-1" : "0:s:a:0", "-threads", "1",
            "-ar", plan.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (video is not null) arguments.AddRange(["-map_chapters", "-1"]);
        if (downmix) arguments.AddRange(["-ac", "2"]);
        if (trimFrames is { } count) arguments.AddRange(["-af", $"atrim=end_sample={count},asetpts=PTS-STARTPTS"]);
        // Containers disagree about global versus stream tag scope. Preserve
        // non-conflicting descriptive values at global scope as well; compare
        // the actual output instead of assuming the muxer accepted each key.
        // Recompression restores original blocks, including duplicate/multiline
        // values. Sending them as command-line tags is unnecessary and lossy.
        if (!preserveRawMetadata)
            foreach (var tag in originalTags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
            {
                if (target == AudioFormat.Wave && !WaveTextFrames.UsesInfo(tag.Key, tag.Value)) continue;
                arguments.AddRange(["-metadata", tag.Key + "=" + tag.Value]);
                if (inventoriedTags && target is AudioFormat.Vorbis or AudioFormat.Opus)
                    arguments.AddRange(["-metadata:s:a:0", tag.Key + "=" + tag.Value]);
            }
        // Never set -ac: implicit downmixing is not part of a format choice.
        arguments.AddRange(target switch
        {
            AudioFormat.Wave => ["-c:a", plan.OutputCodec, "-f", "wav"],
            AudioFormat.Flac => ["-c:a", "flac", "-compression_level", "8", "-sample_fmt", plan.SampleBits <= 16 ? "s16" : "s32",
                "-bits_per_raw_sample", plan.SampleBits!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "flac"],
            AudioFormat.Mp3 => ["-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3"],
            AudioFormat.M4a => ["-c:a", "aac", "-profile:a", "aac_low", "-b:a", "192k", "-movflags", "+faststart", "-f", "ipod"],
            // Pinned default packing gives incorrect decoded lengths when a
            // short stream's audio shares one final page. Retain strict sample
            // count/error validation instead of adding or discarding samples.
            AudioFormat.Vorbis => ["-c:a", "libvorbis", "-q:a", "5", "-page_duration", "1", "-f", "ogg"],
            AudioFormat.Opus => ["-c:a", "libopus", "-application", "audio", "-b:a", "160k", "-vbr", "on", "-f", "opus"],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        });
        // The pinned encoder's implicit mapping permutes channels in 5.0 and
        // 6.1 output. Family 1 explicitly preserves the supported surround map.
        if (target == AudioFormat.Opus && plan.Channels > 2)
            arguments.AddRange(["-mapping_family", "1"]);
        // This pinned encoder otherwise silently reduces PCM32 to PCM24.
        // Enable its explicit 32-bit path only for a 32-bit FLAC plan, then
        // require the reported precision and exact decoded sample comparison.
        if (target == AudioFormat.Flac && plan.SampleBits == 32) arguments.AddRange(["-strict", "experimental"]);
        // FLAC permits a final block shorter than sixteen samples, but the
        // encoder's nominal block size must still meet that minimum. Otherwise
        // its inferred size can reject an entire short recording at startup.
        if (target == AudioFormat.Flac && decodedFrames < 16) arguments.AddRange(["-frame_size", "16"]);
        arguments.AddRange(["-fs", MaximumFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), outputPath]);
        return arguments;
    }

    private static Dictionary<string, string> DescriptiveTags(AudioProbeFacts facts)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in facts.Tags.Concat(facts.Streams.SelectMany(stream => stream.Tags)))
        {
            if (AudioCommentConversion.IsTechnicalTag(tag.Key)) continue;
            if (tag.Key.Equals("language", StringComparison.OrdinalIgnoreCase) && result.TryGetValue(tag.Key, out var language))
            {
                if (tag.Value == "und" && language != "und") continue;
                if (language == "und" && tag.Value != "und") { result[tag.Key] = tag.Value; continue; }
            }
            if (result.TryGetValue(tag.Key, out var previous) && previous != tag.Value)
                throw new NotSupportedException("Conflicting audio tags need an explicit preservation policy.");
            result[tag.Key] = tag.Value;
        }
        return result;
    }

    internal static void RequireOrdinaryPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("An absolute ordinary path is required.");
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked audio engine or scratch paths are not allowed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _voice?.Dispose(); _encoderLease.Dispose(); _probe.Dispose();
    }
}
