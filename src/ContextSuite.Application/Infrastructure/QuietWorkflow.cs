using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

// Window-independent policy: no success notification for problems, warnings or cancellation.
internal sealed class QuietWorkflow
{
    private readonly Dictionary<Guid, (DateTimeOffset Started, bool Sound)> _batches = new();
    private bool _progressDismissed;
    public bool NeedsAttention { get; private set; }
    public bool WarningSoundRequested { get; private set; }
    public void DismissAttention() { NeedsAttention = false; _progressDismissed = true; }
    public void Begin(Guid id, bool sound, DateTimeOffset now)
    {
        if (_batches.TryAdd(id, (now, sound))) _progressDismissed = false;
    }
    public bool ShowProgress(DateTimeOffset now) => !_progressDismissed && _batches.Values.Any(batch => now - batch.Started >= TimeSpan.FromSeconds(2));

    public bool Complete(Guid id, IReadOnlyList<FileResult> results)
    {
        WarningSoundRequested = false;
        if (!_batches.Remove(id, out var batch)) return false;
        WarningSoundRequested = batch.Sound && results.Any(result => result.Publication?.MetadataWarning == true);
        var problem = results.Any(result => result.State is OperationState.Failed or OperationState.Unsupported || result.PartialOutput || result.Publication?.HasWarning == true);
        NeedsAttention |= problem;
        return batch.Sound && results.Count > 0 && !problem && results.All(result => result.State is OperationState.Succeeded or OperationState.Unchanged);
    }
}
