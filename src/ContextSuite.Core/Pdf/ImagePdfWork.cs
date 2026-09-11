using System.Collections.Immutable;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.Pdf;

public sealed record ImagePdfWork(ImagePdfPlan Plan, Guid OutputId, string TemporaryPath, bool PageOrderReviewed,
    string Policy = ImagePdfPlan.Policy)
{
    public void Validate()
    {
        if (Plan is null) throw new InvalidDataException("Missing combined PDF plan.");
        _ = Plan.Confirm(PageOrderReviewed);
        if (OutputId == Guid.Empty || Plan.Pages.Any(page => page.Source.ItemId == OutputId) || Policy != ImagePdfPlan.Policy ||
            !PdfFileProbe.ValidPath(TemporaryPath) || Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            Plan.Pages.Any(page => string.Equals(Path.GetFullPath(page.Source.Path), Path.GetFullPath(TemporaryPath), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid combined PDF reservation or policy.");
    }
}

public sealed record ImagePdfResult(OutputValidation Validation, Guid BatchId, int PageCount, ImmutableArray<string> SourceHashes,
    long OutputBytes, string Policy, string EngineIdentity);
