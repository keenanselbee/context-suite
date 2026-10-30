using System.Collections.Immutable;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

public sealed record AudioFileEncodingResult(AudioConversionPlan Plan, AudioProbeFacts Facts,
    long? DecodedFrames, bool ExactSamples, double? MaximumSampleError, double? RootMeanSquareError,
    ImmutableArray<string> MissingTags, bool IsOptimization, long SourceBytes, long OutputBytes,
    string SourceSha256, string OutputSha256, bool SourceMetadataVerified = false);

// Held read lease on one worker-owned candidate. It is not a publication receipt;
// callers still need metadata admission and application-owned publication.
public sealed class AudioEncodingArtifact : IDisposable
{
    private readonly string _directory;
    private bool _disposed;
    public FileStream Content { get; }
    public AudioFileEncodingResult Result { get; }

    internal AudioEncodingArtifact(string directory, FileStream content, AudioFileEncodingResult result)
    { _directory = directory; Content = content; Result = result; }

    public void Dispose()
    {
        if (_disposed) return;
        var path = Content.Name;
        Content.Dispose();
        AudioEncodingAdapter.RequireOrdinaryPath(path);
        File.Delete(path);
        Directory.Delete(_directory, false);
        _disposed = true;
    }
}
