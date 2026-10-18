using System.Collections.ObjectModel;
using System.ComponentModel;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private FileRow? _selectedAnalysisRow;
    public ObservableCollection<FileRow> AnalysisRows { get; } = [];
    public bool HasMultipleAnalysisFiles => AnalysisRows.Count > 1;
    public bool HasSelectedAnalysis => SelectedAnalysisRow is not null;
    public string AnalysisCopyLabel => HasMultipleAnalysisFiles ? "Copy _report" : "Copy _details";
    public FileRow? SelectedAnalysisRow
    {
        get => _selectedAnalysisRow;
        set
        {
            if (value is null || !AnalysisRows.Contains(value) || ReferenceEquals(value, _selectedAnalysisRow)) return;
            _selectedAnalysisRow = value;
            Changed(); Changed(nameof(HasSelectedAnalysis));
        }
    }

    public string AnalysisStatus
    {
        get
        {
            var complete = AnalysisRows.Count(row => row.HasAnalysis);
            var failed = AnalysisRows.Count(row => row.Result.State is OperationState.Failed or OperationState.Unsupported);
            var cancelled = AnalysisRows.Count(row => row.Result.State == OperationState.Cancelled);
            var unknown = AnalysisRows.Count(row => row.Analysis?.Identity.Confidence == IdentificationConfidence.Unknown);
            if (AnalysisRows.Count == 1 && complete == 1) return "";
            var status = complete + failed + cancelled == AnalysisRows.Count
                ? $"{complete} of {AnalysisRows.Count} files analyzed"
                : $"Analyzing files · {complete + failed + cancelled} of {AnalysisRows.Count} finished";
            if (failed > 0) status += $" · {failed} could not be read";
            if (cancelled > 0) status += $" · {cancelled} cancelled";
            if (unknown > 0) status += $" · {unknown} unknown type{(unknown == 1 ? "" : "s")}";
            return status;
        }
    }

    private void AdmitAnalysisPresentation(string operation, FileRow[] rows)
    {
        if (operation != "analyze") return;
        foreach (var row in rows)
        {
            AnalysisRows.Add(row);
            row.PropertyChanged += OnAnalysisRowChanged;
        }
        SelectedAnalysisRow = rows.FirstOrDefault();
        Changed(nameof(HasMultipleAnalysisFiles)); Changed(nameof(AnalysisCopyLabel));
        Changed(nameof(AnalysisStatus));
    }

    private void OnAnalysisRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileRow.Result)) Changed(nameof(AnalysisStatus));
    }

    public string AnalysisReport => string.Join(Environment.NewLine + Environment.NewLine,
        AnalysisRows.Select(row => row.Name + Environment.NewLine + row.ResultDetails));
}
