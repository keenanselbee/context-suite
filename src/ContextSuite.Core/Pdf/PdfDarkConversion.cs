using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Pdf;

// These are bounded admission facts, not a claim of universal PDF support.
public sealed record PdfDarkFacts(int PageCount, int ChangedPages, int AlreadyDarkPages,
    int RecoloredImages, int PreservedImages, string Policy = PdfDarkWork.CurrentPolicy,
    int RecoveredImages = 0, int RecoveredCoordinates = 0)
{
    public void Validate()
    {
        if (Policy != PdfDarkWork.CurrentPolicy || PageCount is < 1 or > 512 ||
            ChangedPages < 0 || AlreadyDarkPages < 0 || ChangedPages > PageCount ||
            AlreadyDarkPages != PageCount - ChangedPages || RecoloredImages is < 0 or > 32768 ||
            PreservedImages is < 0 or > 32768 || RecoveredImages is < 0 or > 32768 ||
            RecoveredCoordinates is < 0 or > 4096 ||
            ChangedPages == 0 && (RecoloredImages != 0 || RecoveredImages != 0 || RecoveredCoordinates != 0))
            throw new InvalidDataException("Invalid PDF dark-mode admission facts.");
    }
}

public sealed record PdfDarkSource(Guid ItemId, string Path, string Sha256, long FileBytes, PdfDarkFacts Facts)
{
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public void Validate()
    {
        new PdfFileProbe(ItemId, Path).Validate();
        if (!System.IO.Path.GetExtension(Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
            Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) ||
            FileBytes is <= 0 or > MaximumSourceBytes || Facts is null)
            throw new InvalidDataException("Invalid PDF dark-mode source.");
        Facts.Validate();
    }
}

public sealed record PdfDarkPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<PdfDarkSource> Sources)
{
    public ConfirmedPdfDark Confirm()
    {
        if (BatchId == Guid.Empty || Settings?.Operation != "convert" || Settings.Preferences is null ||
            Sources.IsDefaultOrEmpty || Sources.Length > 128 || Sources.Any(source => source is null))
            throw new InvalidDataException("Invalid PDF dark-mode batch.");
        foreach (var source in Sources) source.Validate();
        if (Sources.Select(source => source.ItemId).Distinct().Count() != Sources.Length ||
            Sources.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sources.Length)
            throw new InvalidDataException("Duplicate PDF dark-mode sources.");
        if (Sources.Sum(source => source.FileBytes) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("PDF dark-mode batch exceeds its source budget.");
        // Appearance conversion always creates a copy, including with saved overwrite consent.
        Settings.SelectOutput(false, false, false, false);
        return new(this);
    }
}

public sealed class ConfirmedPdfDark
{
    internal ConfirmedPdfDark(PdfDarkPlan plan) { Plan = plan; }
    public PdfDarkPlan Plan { get; }
}

public sealed record PdfDarkWork(PdfDarkSource Source, string TemporaryPath, string Policy = PdfDarkWork.CurrentPolicy)
{
    public const string CurrentPolicy = "pdf-dark-8";
    public const int MaximumOutputBytes = 128 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing PDF dark-mode source.");
        Source.Validate();
        if (Source.Facts.ChangedPages == 0 || Policy != CurrentPolicy || !PdfFileProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(TemporaryPath), System.IO.Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid PDF dark-mode output reservation.");
    }
}

public sealed record PdfDarkResult(OutputValidation Validation, string SourceSha256, long SourceBytes,
    long OutputBytes, PdfDarkFacts Facts, string EngineIdentity)
{
    public void Validate(PdfDarkWork work)
    {
        work.Validate();
        if (Validation is null || Validation.ItemId != work.Source.ItemId || !Validation.MatchesPlan ||
            Validation.Sha256 is not { Length: 64 } || !Validation.Sha256.All(char.IsAsciiHexDigit) ||
            SourceSha256 != work.Source.Sha256 || SourceBytes != work.Source.FileBytes ||
            OutputBytes is <= 0 or > PdfDarkWork.MaximumOutputBytes || Facts != work.Source.Facts ||
            string.IsNullOrWhiteSpace(EngineIdentity) || EngineIdentity.Length > 256)
            throw new InvalidDataException("PDF dark-mode result does not match its plan.");
    }
}
