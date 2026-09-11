using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    private readonly SortedDictionary<int, FileResult> _pdfPages = [];
    private readonly HashSet<string> _pdfRecoveryRecords = new(StringComparer.OrdinalIgnoreCase);
    internal string? PdfSourceHash { get; private set; }
    internal bool PdfResumeBlocked { get; private set; }
    public int PdfPageCount { get; private set; }
    public int SavedPdfPages => _pdfPages.Values.Count(page => page.Publication?.IsCommitted == true);
    public bool HasIncompletePdfPages => PdfPageCount > 0 && SavedPdfPages < PdfPageCount;
    internal IEnumerable<int> CompletedPdfPages => _pdfPages.Where(page => page.Value.Publication?.IsCommitted == true).Select(page => page.Key);
    private string PdfPageDetails => PdfPageCount == 0 ? "" : "\nPage results:\n" + string.Join("\n", _pdfPages.Select(page =>
        $"Page {page.Key + 1}: {page.Value.Message}" +
        (page.Value.Publication?.OutputPath is { } output ? "\n  " + output : "") +
        (page.Value.Publication?.RecoveryRecordPath is { } recovery ? "\n  Recovery record: " + recovery : ""))) +
        (_pdfRecoveryRecords.Count == 0 ? "" : "\nEarlier recovery records retained:\n" + string.Join("\n", _pdfRecoveryRecords));

    internal void ResumePdfFrom(FileRow previous)
    {
        PdfSourceHash = previous.SavedPdfPages > 0 ? previous.PdfSourceHash : null;
        PdfPageCount = previous.PdfPageCount;
        foreach (var page in previous._pdfPages.Where(page => page.Value.Publication?.IsCommitted == true)) _pdfPages.Add(page.Key, page.Value);
        _pdfRecoveryRecords.UnionWith(previous._pdfRecoveryRecords);
        foreach (var page in previous._pdfPages.Values)
            if (page.Publication?.RecoveryRecordPath is { } recovery) _pdfRecoveryRecords.Add(recovery);
        if (PdfPageCount > 0) ApplyResult(new(Path, OperationState.Pending, "Waiting to retry unfinished PDF pages. Completed copies are kept."));
    }

    internal void BeginPdfPages(PdfRasterSource source)
    {
        if (PdfSourceHash is not null && PdfSourceHash != source.Sha256)
            throw new InvalidDataException("The PDF changed since the previous attempt. Start a new Convert command for this version; completed copies were kept.");
        PdfSourceHash = source.Sha256;
        PdfPageCount = source.Document.Pages.Length;
        foreach (var page in source.Document.Pages)
            if (!_pdfPages.TryGetValue(page.Index, out var previous) || previous.Publication?.IsCommitted != true)
                _pdfPages[page.Index] = new(Path, OperationState.Pending, "Waiting to convert.");
        ApplyResult(new(Path, OperationState.Pending, "Preparing PDF page copies"));
    }

    internal void BlockPdfResume(string message)
    {
        PdfResumeBlocked = true;
        ApplyResult(new(Path, OperationState.Failed, message));
    }

    internal void ApplyPdfPage(int index, FileResult result)
    {
        if (index < 0 || index >= PdfPageCount) throw new InvalidDataException("PDF page result is outside this document.");
        _pdfPages[index] = result;
        var results = _pdfPages.Values.ToArray();
        var active = results.Any(page => page.State is OperationState.Pending or OperationState.Running);
        var state = active ? OperationState.Running : results.Any(page => page.State == OperationState.Failed) ? OperationState.Failed :
            results.Any(page => page.State == OperationState.Unsupported) ? OperationState.Unsupported :
            results.Any(page => page.State == OperationState.Cancelled) ? OperationState.Cancelled : OperationState.Succeeded;
        var saved = SavedPdfPages;
        var unfinished = PdfPageCount - saved;
        var message = $"{saved} of {PdfPageCount} page copies saved. " + (active ? $"Processing page {index + 1}." :
            saved == PdfPageCount ? "Original PDF kept." : $"{unfinished} {(unfinished == 1 ? "page" : "pages")} not converted. Original PDF and completed copies kept.");
        if (!active && saved == 0 && state == OperationState.Failed) message += " " + result.Message;
        var published = results.FirstOrDefault(page => page.Publication?.HasWarning == true) ?? results.FirstOrDefault(page => page.Publication?.IsCommitted == true);
        ApplyResult(new(Path, state, message, published?.Publication, result.EngineIdentity ?? published?.EngineIdentity, saved > 0 && saved < PdfPageCount));
    }
}
