using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Gif;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    private readonly SortedDictionary<int, FileResult> _gifFrames = [];
    private readonly Dictionary<int, string> _gifOutputHashes = [];
    private readonly HashSet<string> _gifRecoveryRecords = new(StringComparer.OrdinalIgnoreCase);
    internal GifSourceFacts? GifSource { get; private set; }
    internal GifFolderIdentity? GifFolder { get; set; }
    internal bool GifResumeBlocked { get; private set; }
    internal int SavedGifFrames => _gifFrames.Values.Count(frame => frame.Publication?.IsCommitted == true);
    internal bool HasIncompleteGifFrames => GifSource is { } source && SavedGifFrames < source.FrameCount;
    internal IReadOnlyDictionary<int, FileResult> GifFrames => _gifFrames;
    private string GifFrameDetails => GifSource is null ? "" : "\nGIF frame results (order retained; playback timing is not):\n" +
        string.Join("\n", _gifFrames.Select(frame => $"Frame {frame.Key + 1}: {frame.Value.Message}" +
            (frame.Value.Publication?.OutputPath is { } path ? "\n  " + path : "") +
            (frame.Value.Publication?.RecoveryRecordPath is { } record ? "\n  Recovery record: " + record : ""))) +
        (_gifRecoveryRecords.Count == 0 ? "" : "\nEarlier recovery records retained:\n" + string.Join("\n", _gifRecoveryRecords));

    internal void ResumeGifFrom(FileRow previous)
    {
        GifSource = previous.GifSource;
        GifFolder = previous.GifFolder;
        foreach (var frame in previous._gifFrames) _gifFrames.Add(frame.Key, frame.Value);
        foreach (var hash in previous._gifOutputHashes) _gifOutputHashes.Add(hash.Key, hash.Value);
        _gifRecoveryRecords.UnionWith(previous._gifRecoveryRecords);
    }

    internal bool BeginGif(GifSourceFacts source)
    {
        if (GifSource is { } previous && (previous.Sha256 != source.Sha256 || previous.FileBytes != source.FileBytes ||
            previous.Width != source.Width || previous.Height != source.Height || previous.FrameCount != source.FrameCount ||
            previous.LoopCount != source.LoopCount || !previous.DelayCentiseconds.SequenceEqual(source.DelayCentiseconds)))
        {
            BlockGifResume("The GIF changed. Start a new Convert command for this version; completed frames were kept.");
            return false;
        }
        GifSource = source;
        for (var index = 0; index < source.FrameCount; index++)
            if (!_gifFrames.TryGetValue(index, out var previousFrame) || previousFrame.Publication?.IsCommitted != true)
            {
                if (previousFrame?.Publication?.RecoveryRecordPath is { } recovery) _gifRecoveryRecords.Add(recovery);
                _gifFrames[index] = new(Path, OperationState.Pending, "Waiting to extract frame.");
                _gifOutputHashes.Remove(index);
            }
        ApplyResult(new(Path, OperationState.Pending, "Extracting PNG frames in order. Playback timing is not retained."));
        return true;
    }

    internal void BlockGifResume(string message)
    {
        GifResumeBlocked = true;
        ApplyResult(new(Path, OperationState.Failed, message));
    }

    internal async Task VerifyCommittedGifFramesAsync(CancellationToken token)
    {
        foreach (var (index, result) in _gifFrames.ToArray())
        {
            if (result.Publication?.IsCommitted != true) continue;
            var publication = result.Publication;
            var matches = false;
            if (publication.OutputPath is { } path && _gifOutputHashes.TryGetValue(index, out var hash))
            {
                try
                {
                    await using var output = PublicationFiles.OpenRead(path);
                    var actual = await PublicationFiles.FingerprintAsync(output, token);
                    matches = actual.Length == publication.OutputBytes &&
                        string.Equals(actual.Sha256, hash, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    System.ComponentModel.Win32Exception) { }
            }
            if (matches) continue;
            var message = $"Previously saved GIF frame {index + 1} is missing or changed. Completed copies were kept; start a new Convert command.";
            _gifFrames[index] = result with { State = OperationState.Failed, Message = message,
                Publication = publication with { Outcome = PublicationOutcome.Failed, Message = message } };
            _gifOutputHashes.Remove(index);
            throw new InvalidDataException(message);
        }
    }

    internal void ApplyGifFrame(int index, FileResult result, string? outputSha256 = null)
    {
        if (!_gifFrames.ContainsKey(index)) throw new InvalidDataException("GIF result is outside this source.");
        _gifFrames[index] = result;
        if (result.Publication?.IsCommitted == true && outputSha256 is not null) _gifOutputHashes[index] = outputSha256;
        else if (result.Publication?.IsCommitted != true) _gifOutputHashes.Remove(index);
        var frames = _gifFrames.Values.ToArray();
        var active = frames.Any(frame => frame.State is OperationState.Pending or OperationState.Running);
        var state = active ? OperationState.Running : frames.Any(frame => frame.State == OperationState.Failed) ? OperationState.Failed :
            frames.Any(frame => frame.State == OperationState.Unsupported) ? OperationState.Unsupported :
            frames.Any(frame => frame.State == OperationState.Cancelled) ? OperationState.Cancelled : OperationState.Succeeded;
        var saved = SavedGifFrames;
        var message = $"{saved} of {GifSource!.FrameCount} PNG frames saved. Original kept. " +
            "Frame order is retained; playback timing is not.";
        if (!active && HasIncompleteGifFrames) message += " " + result.Message;
        var publication = frames.FirstOrDefault(frame => frame.Publication?.HasWarning == true)?.Publication ??
            frames.FirstOrDefault(frame => frame.Publication?.IsCommitted == true)?.Publication;
        ApplyResult(new(Path, state, message, publication, result.EngineIdentity, saved > 0 && HasIncompleteGifFrames));
    }
}
