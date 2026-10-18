using System.ComponentModel;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    public AnalysisSnapshot? AnalysisPresentation { get; private set; }
    public string AnalysisType => AnalysisPresentation?.TypeName ?? (Result.State switch
    {
        OperationState.Failed or OperationState.Unsupported => "File information unavailable",
        OperationState.Cancelled => "Analysis cancelled",
        OperationState.Running => "Reading file information…",
        _ => "Waiting to analyze"
    });
    public string AnalysisKeyInformation => AnalysisPresentation?.KeyInformation ?? Status;
    public string AnalysisFileSize => AnalysisPresentation?.FileSize ?? "Unavailable";
    public string AnalysisIdentification => AnalysisPresentation?.Identification ?? "Not identified";
    public string AnalysisNotice => AnalysisPresentation?.Warnings ??
        (Result.State is OperationState.Failed or OperationState.Unsupported or OperationState.Cancelled ? Status : "");
    public bool HasAnalysisNotice => AnalysisNotice.Length > 0;
    public string AnalysisLocation => System.IO.Path.GetDirectoryName(Path) ?? Path;

    private void NotifyAnalysisPresentation()
    {
        foreach (var property in new[] { nameof(AnalysisPresentation), nameof(AnalysisType), nameof(AnalysisKeyInformation),
            nameof(AnalysisFileSize), nameof(AnalysisIdentification), nameof(AnalysisNotice), nameof(HasAnalysisNotice) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
