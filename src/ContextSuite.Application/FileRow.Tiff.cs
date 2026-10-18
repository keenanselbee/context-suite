using ContextSuite.Core.Operations;
using ContextSuite.Core.Tiff;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    private readonly SortedDictionary<int, FileResult> _tiffPages = [];
    private readonly HashSet<string> _tiffRecoveryRecords = new(StringComparer.OrdinalIgnoreCase);
    internal string? TiffSourceHash { get; private set; }
    internal uint? TiffMatteRgb { get; set; }
    internal bool TiffResumeBlocked { get; private set; }
    public int TiffPageCount { get; private set; }
    public int SavedTiffPages => _tiffPages.Values.Count(page => page.Publication?.IsCommitted == true);
    public bool HasIncompleteTiffPages => TiffPageCount > 0 && SavedTiffPages < TiffPageCount;
    internal IEnumerable<(int PageIndex, long OutputBytes)> CompletedTiffOutputs => _tiffPages
        .Where(page => page.Value.Publication?.IsCommitted == true)
        .Select(page => (page.Key, page.Value.Publication!.OutputBytes));
    private string TiffPageDetails => TiffPageCount == 0 ? "" : "\nTIFF results:\n" + string.Join("\n", _tiffPages.Select(page =>
        (page.Key < 0 ? "PDF" : $"Page {page.Key + 1}") + ": " + page.Value.Message +
        (page.Value.Publication?.OutputPath is { } output ? "\n  " + output : "") +
        (page.Value.Publication?.RecoveryRecordPath is { } recovery ? "\n  Recovery record: " + recovery : ""))) +
        (_tiffRecoveryRecords.Count == 0 ? "" : "\nEarlier recovery records retained:\n" + string.Join("\n", _tiffRecoveryRecords));

    internal void ResumeTiffFrom(FileRow previous)
    {
        TiffSourceHash = previous.SavedTiffPages > 0 ? previous.TiffSourceHash : null;
        TiffMatteRgb = previous.TiffMatteRgb;
        TiffPageCount = previous.TiffPageCount;
        foreach (var page in previous._tiffPages.Where(page => page.Value.Publication?.IsCommitted == true)) _tiffPages.Add(page.Key, page.Value);
        _tiffRecoveryRecords.UnionWith(previous._tiffRecoveryRecords);
        foreach (var page in previous._tiffPages.Values)
            if (page.Publication?.RecoveryRecordPath is { } recovery) _tiffRecoveryRecords.Add(recovery);
        if (TiffPageCount > 0) ApplyResult(new(Path, OperationState.Pending, "Waiting to retry unfinished TIFF outputs. Completed copies are kept."));
    }

    internal void BeginTiff(TiffSourceFacts source, bool pdf)
    {
        if (TiffSourceHash is not null && TiffSourceHash != source.Sha256)
            throw new InvalidDataException("The TIFF changed since the previous attempt. Start a new Convert command; completed copies were kept.");
        TiffSourceHash = source.Sha256;
        TiffPageCount = pdf ? 1 : source.Pages.Length;
        foreach (var index in pdf ? new[] { -1 } : Enumerable.Range(0, source.Pages.Length))
            if (!_tiffPages.TryGetValue(index, out var previous) || previous.Publication?.IsCommitted != true)
                _tiffPages[index] = new(Path, OperationState.Pending, "Waiting to convert.");
        ApplyResult(new(Path, OperationState.Pending, "Preparing TIFF output copies"));
    }

    internal void BlockTiffResume(string message)
    {
        TiffResumeBlocked = true;
        ApplyResult(new(Path, OperationState.Failed, message));
    }

    internal void ApplyTiffPage(int index, FileResult result)
    {
        if (!_tiffPages.ContainsKey(index)) throw new InvalidDataException("TIFF result is outside this source.");
        _tiffPages[index] = result;
        var pages = _tiffPages.Values.ToArray();
        var active = pages.Any(page => page.State is OperationState.Pending or OperationState.Running);
        var state = active ? OperationState.Running : pages.Any(page => page.State == OperationState.Failed) ? OperationState.Failed :
            pages.Any(page => page.State == OperationState.Unsupported) ? OperationState.Unsupported :
            pages.Any(page => page.State == OperationState.Cancelled) ? OperationState.Cancelled : OperationState.Succeeded;
        var saved = SavedTiffPages;
        var message = $"{saved} of {TiffPageCount} TIFF {(TiffPageCount == 1 ? "output" : "page copies")} saved. " +
            (active ? "Processing." : saved == TiffPageCount ? "Original TIFF kept." : "Original and completed copies kept.");
        if (!active && saved < TiffPageCount) message += " " + result.Message;
        var published = pages.FirstOrDefault(page => page.Publication?.HasWarning == true) ?? pages.FirstOrDefault(page => page.Publication?.IsCommitted == true);
        ApplyResult(new(Path, state, message, published?.Publication, result.EngineIdentity ?? published?.EngineIdentity,
            saved > 0 && saved < TiffPageCount));
    }

    internal System.Collections.Generic.IReadOnlyList<FileResult> TiffResults => _tiffPages.Values.ToArray();
}
