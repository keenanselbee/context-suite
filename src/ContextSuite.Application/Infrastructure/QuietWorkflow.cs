using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

// Window-independent policy: no success notification for problems, warnings or cancellation.
internal sealed class QuietWorkflow
{
    private readonly Dictionary<Guid, (DateTimeOffset Started, bool Sound)> _batches = new();
    public bool NeedsAttention { get; private set; }
    public void Begin(Guid id, bool sound, DateTimeOffset now) => _batches.TryAdd(id, (now, sound));
    public bool ShowProgress(DateTimeOffset now) => _batches.Values.Any(batch => now - batch.Started >= TimeSpan.FromSeconds(2));

    public bool Complete(Guid id, IReadOnlyList<FileResult> results)
    {
        if (!_batches.Remove(id, out var batch)) return false;
        var problem = results.Any(result => result.State is OperationState.Failed or OperationState.Unsupported || result.Publication?.HasWarning == true);
        NeedsAttention |= problem;
        return batch.Sound && results.Count > 0 && !problem && results.All(result => result.State is OperationState.Succeeded or OperationState.Unchanged);
    }
}
