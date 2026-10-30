using System.Security.Cryptography;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// The worker may fill only the application's empty reservation. Final naming,
// replacement, recycling and recovery stay in the application publisher.
public sealed class AudioFileAdapter(string binaryDirectory, string scratchDirectory, string? windowsVoiceDirectory = null) : IDisposable
{
    private readonly AudioEncodingAdapter _encoder = new(binaryDirectory, scratchDirectory, windowsVoiceDirectory);
    private bool _disposed;

    public async Task<AudioFileSource> ProbeFlacAsync(AudioFileProbe request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing audio item identity.");
        using var source = MediaFiles.OpenRead(request.Path);
        return await _encoder.InspectFlacAsync(request.ItemId, request.Path, source, token);
    }

    public async Task<AudioWorkResult> OptimizeFlacAsync(FlacOptimizationWork work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Source.Validate();
        if (work.Policy != FlacOptimizationPlan.Policy || work.Source.OptimizationBlockReason is not null ||
            Path.GetFileName(work.TemporaryPath) != $".context-suite-{work.Source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(work.Source.Path), Path.GetFullPath(work.TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid FLAC work or reserved output.");
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var actual = await _encoder.InspectFlacAsync(work.Source.ItemId, work.Source.Path, source, token);
        if (actual != work.Source with { Facts = actual.Facts } || !SameFacts(work.Source.Facts, actual.Facts))
            throw new InvalidDataException("Audio changed after planning.");
        using var output = new ReservedMediaOutput(work.TemporaryPath, AudioFileSource.MaximumFileBytes);
        using var artifact = await _encoder.OptimizeFlacFileAsync(source, token);
        var result = artifact.Result;
        if (!result.IsOptimization || !result.SourceMetadataVerified || !result.ExactSamples || !result.MissingTags.IsEmpty || result.DecodedFrames is not > 0 ||
            result.SourceSha256 != work.Source.Sha256 || result.SourceBytes != work.Source.FileBytes || result.Plan.Policy != work.Policy)
            throw new InvalidDataException("FLAC validation did not approve the planned source and preservation policy.");
        await CopyValidatedOutputAsync(artifact, output, token);
        return new(new(work.Source.ItemId, result.OutputSha256, true), result.SourceBytes, result.OutputBytes, result.DecodedFrames.Value,
            result.SourceSha256, work.Policy, "FFmpeg n9.0.1-27-g9b0578816c; flac-lossless-1");
    }

    public async Task<AudioFileSource> ProbeConversionAsync(AudioFileProbe request, AudioFormat target, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing audio item identity.");
        using var source = MediaFiles.OpenRead(request.Path);
        return await _encoder.InspectConversionAsync(request.ItemId, request.Path, source, target, token);
    }

    public async Task<AudioFileSource> ProbeVideoAsync(AudioFileProbe request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing video item identity.");
        using var source = MediaFiles.OpenRead(request.Path);
        return await _encoder.InspectVideoAsync(request.ItemId, request.Path, source, token);
    }

    public async Task<AudioFileSource> ProbeConversionAsync(AudioFileProbe request, AudioFormat target,
        CancellationToken token, VideoAudioSelection video)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.ItemId == Guid.Empty) throw new InvalidDataException("Missing video item identity.");
        using var source = MediaFiles.OpenRead(request.Path);
        return await _encoder.InspectConversionAsync(request.ItemId, request.Path, source, target, token, video);
    }

    public async Task<AudioWorkResult> ConvertAsync(AudioConversionWork work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        using var source = MediaFiles.OpenRead(work.Source.Path);
        var actual = await _encoder.InspectConversionAsync(work.Source.ItemId, work.Source.Path, source, work.Target, token, work.Source.Video);
        if (actual != work.Source with { Facts = actual.Facts } || !SameFacts(work.Source.Facts, actual.Facts))
            throw new InvalidDataException("Audio changed after planning.");
        var expected = AudioConversionPlan.Create(actual.Facts, work.Target, video: work.Source.Video);
        using var output = new ReservedMediaOutput(work.TemporaryPath, AudioFileSource.MaximumFileBytes);
        using var artifact = await _encoder.ConvertFileAsync(source, work.Target, work.AcceptedConsent, token, work.Source.Video);
        var result = artifact.Result;
        if (result.IsOptimization || result.Plan.AlreadyTarget || !result.SourceMetadataVerified || !result.MissingTags.IsEmpty ||
            result.DecodedFrames is not > 0 || result.SourceSha256 != work.Source.Sha256 || result.SourceBytes != work.Source.FileBytes ||
            (expected.RequiresExactSamples && !result.ExactSamples) || result.Plan with { Notices = [] } != expected with { Notices = [] })
            throw new InvalidDataException("Audio validation did not approve the planned source, encoding and metadata policy.");
        await CopyValidatedOutputAsync(artifact, output, token);
        var voice = actual.Facts.Streams.Any(stream => stream.Kind == "audio" && stream.Codec == "wmavoice");
        return new(new(work.Source.ItemId, result.OutputSha256, true), result.SourceBytes, result.OutputBytes, result.DecodedFrames.Value,
            result.SourceSha256, work.Policy, (voice ? "Windows Voice; " : "") + "FFmpeg n9.0.1-27-g9b0578816c; " + work.Policy);
    }

    private static async Task CopyValidatedOutputAsync(AudioEncodingArtifact artifact, ReservedMediaOutput output, CancellationToken token)
    {
        var result = artifact.Result; var buffer = new byte[65536];
        int count;
        while ((count = await artifact.Content.ReadAsync(buffer, token)) != 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
        output.Flush(true);
        output.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(output, token));
        if (output.Length != result.OutputBytes || digest != result.OutputSha256) throw new IOException("Reserved audio output changed while copying.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _encoder.Dispose();
    }

    private static bool SameFacts(AudioProbeFacts expected, AudioProbeFacts actual)
    {
        // ImmutableDictionary enumeration order/comparers can change across IPC.
        // Compare typed values, not their serialized property order.
        if (expected.Container != actual.Container || expected.ReportedDurationSeconds != actual.ReportedDurationSeconds ||
            expected.StartSeconds != actual.StartSeconds || expected.Streams.Length != actual.Streams.Length ||
            !TagsAgree(expected.Tags, actual.Tags)) return false;
        var expectedChapters = expected.Chapters.IsDefault ? [] : expected.Chapters;
        var actualChapters = actual.Chapters.IsDefault ? [] : actual.Chapters;
        if (expectedChapters.Length != actualChapters.Length || !expectedChapters.Zip(actualChapters).All(pair =>
            pair.First with { Tags = ImmutableDictionary<string, string>.Empty } == pair.Second with { Tags = ImmutableDictionary<string, string>.Empty } &&
            TagsAgree(pair.First.Tags, pair.Second.Tags))) return false;
        return expected.Streams.Zip(actual.Streams).All(pair =>
            pair.First with { Tags = ImmutableDictionary<string, string>.Empty } == pair.Second with { Tags = ImmutableDictionary<string, string>.Empty } &&
            TagsAgree(pair.First.Tags, pair.Second.Tags));
        static bool TagsAgree(ImmutableDictionary<string, string> before, ImmutableDictionary<string, string> after) =>
            before.Count == after.Count && before.All(tag => after.TryGetValue(tag.Key, out var value) && value == tag.Value);
    }
}
