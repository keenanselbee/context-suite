using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Core.Office;

public sealed record OfficePdfWork(OfficeExportWork Export, OfficeExportCandidate Candidate, string TemporaryPath,
    string Policy = OfficePdfPolicy.Policy)
{
    public void Validate()
    {
        if (Export is null || Candidate?.Completion is null || Policy != OfficePdfPolicy.Policy)
            throw new InvalidDataException("Missing Office export completion or PDF validation policy.");
        Export.Validate();
        Candidate.Validate(Export, Candidate.Completion.RequestId);
        if (!PdfFileProbe.ValidPath(TemporaryPath) || Path.GetFileName(TemporaryPath) != $".context-suite-{Export.ItemId:N}.tmp")
            throw new InvalidDataException("Office PDF requires its existing output reservation.");
        var output = Path.GetFullPath(TemporaryPath);
        if (output.StartsWith(Export.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Office publication reservation must remain outside the engine context.");
    }
}

public sealed record OfficePdfResult(OutputValidation Validation, long SourceBytes, string SourceSha256,
    long OutputBytes, int PageCount, string Policy, string EngineIdentity)
{
    public void Validate(OfficePdfWork work)
    {
        work.Validate();
        if (Validation is null || Validation.ItemId != work.Export.ItemId || !Validation.MatchesPlan ||
            !string.Equals(Validation.Sha256, work.Candidate.Completion.OutputSha256, StringComparison.OrdinalIgnoreCase) ||
            SourceBytes != work.Export.SourceBytes || !string.Equals(SourceSha256, work.Export.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            OutputBytes != work.Candidate.Completion.OutputBytes || PageCount is <= 0 or > OfficePdfPolicy.MaximumPages || Policy != work.Policy ||
            string.IsNullOrWhiteSpace(EngineIdentity) || EngineIdentity.Length > 256 || EngineIdentity.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new InvalidDataException("Office PDF validation response does not match its completed export.");
    }
}
