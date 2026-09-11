namespace ContextSuite.Core.Operations;

public enum OperationState { Pending, Running, Unsupported, Cancelled, Failed, Succeeded, Unchanged }
public enum FactCertainty { Explicit, Derived, Unknown, NotEncoded, Unavailable }
public enum OutputMode { SiblingCopy, RecoverableReplacement }
public enum AccessState { Trial, Paid, Expired, Unavailable }

public sealed record MediaFact(string Section, string Name, string Value, FactCertainty Certainty);
public sealed record OperationWarning(string Code, string Message);
public sealed record OutputPolicy(OutputMode Mode = OutputMode.SiblingCopy, bool SkipIfLarger = true);
public sealed record FileResult(string Path, OperationState State, string Message, PublicationResult? Publication = null, string? EngineIdentity = null,
    bool PartialOutput = false);
public sealed record OperationProgress(Guid RequestId, int Completed, int Total);
public sealed record MediaCapability(string Operation, string InputFormat, string? OutputFormat);
public sealed record OperationPlan(Guid RequestId, string? TargetFormat, string? PresetId,
    OutputPolicy Output, IReadOnlyList<OperationWarning> Warnings);
public sealed record AccessDecision(AccessState State, string Message)
{
    public bool CanStart => State is AccessState.Trial or AccessState.Paid;
}

public interface IAccessPolicy
{
    ValueTask<AccessDecision> EvaluateAsync(CancellationToken cancellationToken);
}

public interface IMediaCatalog
{
    IReadOnlyList<MediaCapability> Capabilities { get; }
}
