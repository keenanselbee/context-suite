using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Operations;

public sealed record OutputIntent(Guid ItemId, string SourcePath, string TargetExtension,
    BatchSettings Settings, bool ReplaceOriginal = false, bool ReplacementConfirmed = false,
    bool QuickAction = false, DdsRepresentation? Dds = null, int? PageNumber = null);

// Produced by the adapter's semantic validator for the matching reserved output.
// The publisher independently checks the digest; an exit code is never sufficient.
public sealed record OutputValidation(Guid ItemId, string Sha256, bool MatchesPlan);

public enum PublicationOutcome { CopyCreated, SourceReplaced, OriginalRetained, BackupRetained, RecoveryRequired, Unchanged, Cancelled, Failed }

public sealed record PublicationResult(string SourcePath, PublicationOutcome Outcome, string Message,
    string? OutputPath = null, string? RetainedOriginalPath = null, string? RecoveryRecordPath = null,
    long SourceBytes = 0, long OutputBytes = 0, bool CleanupWarning = false, bool MetadataWarning = false)
{
    public bool IsCommitted => Outcome is PublicationOutcome.CopyCreated or PublicationOutcome.SourceReplaced or
        PublicationOutcome.OriginalRetained or PublicationOutcome.BackupRetained or PublicationOutcome.RecoveryRequired;
    public bool HasWarning => MetadataWarning || CleanupWarning || Outcome is PublicationOutcome.OriginalRetained or PublicationOutcome.BackupRetained or PublicationOutcome.RecoveryRequired;

    public FileResult ToFileResult()
    {
        var state = Outcome switch
        {
            PublicationOutcome.Cancelled => OperationState.Cancelled,
            PublicationOutcome.Failed => OperationState.Failed,
            PublicationOutcome.Unchanged => OperationState.Unchanged,
            _ => OperationState.Succeeded
        };
        return new FileResult(SourcePath, state, Message, this);
    }
}

public sealed record PublicationSummary(int Completed, int Warnings, int Unchanged, int Cancelled, int Failed, long BytesSaved)
{
    public static PublicationSummary From(IEnumerable<PublicationResult> results)
    {
        var values = results.ToArray();
        return new(values.Count(v => v.IsCommitted), values.Count(v => v.HasWarning),
            values.Count(v => v.Outcome == PublicationOutcome.Unchanged),
            values.Count(v => v.Outcome == PublicationOutcome.Cancelled),
            values.Count(v => v.Outcome == PublicationOutcome.Failed),
            values.Where(v => v.IsCommitted).Sum(v => v.SourceBytes - v.OutputBytes));
    }
}
