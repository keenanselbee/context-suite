using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;

internal static class AnalysisViewContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        var html = Path.Combine(scratch, "index.html");
        var unknown = Path.Combine(scratch, "mystery.unregistered");
        var missing = Path.Combine(scratch, "missing.bin");
        await File.WriteAllTextAsync(html, "<html><body>Analysis fixture</body></html>");
        await File.WriteAllBytesAsync(unknown, [0, 255, 1]);
        await using var worker = new WorkerClient(Path.Combine(scratch, "must-not-start.exe"));
        await using var view = new MainViewModel(worker);
        view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [html]));
        await view.WaitForIdleAsync();
        check(view.AnalysisRows.Count == 1 && view.SelectedAnalysisRow == view.AnalysisRows[0] &&
            !view.HasMultipleAnalysisFiles && !view.DisplayRows.Any() && !view.HasResults && !view.IsOperationBusy,
            "analysis window: single selection remains separate from operation results");
        var first = view.SelectedAnalysisRow!;
        check(first.AnalysisPresentation is not null && first.AnalysisFileSize != "Unavailable" && !first.HasOutput,
            "analysis window: single result presents actual facts without output actions");
        view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [unknown, missing]));
        await view.WaitForIdleAsync();
        check(view.HasMultipleAnalysisFiles && view.AnalysisRows.Count == 3 && view.SelectedAnalysisRow == view.AnalysisRows[1] &&
            view.AnalysisStatus.Contains("2 of 3") && view.AnalysisStatus.Contains("1 could not be read") &&
            view.AnalysisStatus.Contains("1 unknown type"),
            "analysis window: later batch retains earlier facts and distinguishes unknown from unreadable");
        check(view.AnalysisRows[2].HasAnalysisNotice && view.AnalysisRows[2].AnalysisNotice.Contains("moved or deleted") &&
            view.AnalysisReport.Contains("index.html") && view.AnalysisReport.Contains("missing.bin"),
            "analysis window: unreadable row and combined copy report retain actionable details");
        view.SelectedAnalysisRow = first;
        view.Admit(new(Guid.NewGuid(), "convert", "png", [html]));
        // Cancel transform work while another Analyze request is queued. Neither analysis nor its selection may be lost.
        view.Admit(new(Guid.NewGuid(), "analyze", "open-details", [html]));
        view.SelectedAnalysisRow = first;
        view.CancelCommand.Execute(null);
        await view.WaitForIdleAsync();
        check(view.AnalysisRows.Count == 4 && view.AnalysisRows[^1].HasAnalysis && view.SelectedAnalysisRow == first &&
            view.DisplayRows.Single().Result.State == OperationState.Cancelled && !view.IsOperationBusy,
            "independent windows: cancelling conversion preserves queued analysis and inspector selection");
        var quiet = new QuietWorkflow();
        var id = Guid.NewGuid(); var start = DateTimeOffset.UtcNow;
        quiet.Begin(id, false, start);
        quiet.DismissAttention();
        check(!quiet.ShowProgress(start.AddSeconds(3)) && !quiet.NeedsAttention,
            "independent windows: dismissed progress does not reopen on the next timer tick");
        quiet.Complete(id, [new(html, OperationState.Failed, "Failure")]);
        check(quiet.NeedsAttention, "independent windows: later conversion failure still requests attention");
    }
}
