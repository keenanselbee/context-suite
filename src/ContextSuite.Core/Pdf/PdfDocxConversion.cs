using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Pdf;

public sealed record PdfDocxSource(Guid ItemId, string Path, string Sha256, long FileBytes,
    int PageCount, int TextCharacters, int ImageCount, bool NeedsOcr)
{
    public const int MaximumSourceBytes = 64 * 1024 * 1024;
    public const int MaximumPages = 256;
    public const int MaximumCharacters = 2_000_000;
    public const int MaximumImages = 256;
    public void Validate()
    {
        new PdfFileProbe(ItemId, Path).Validate();
        if (Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) ||
            FileBytes is <= 0 or > MaximumSourceBytes || PageCount is <= 0 or > MaximumPages ||
            TextCharacters is < 0 or > MaximumCharacters || ImageCount is < 0 or > MaximumImages)
            throw new InvalidDataException("Invalid PDF-to-Word source facts.");
    }
}

public sealed record PdfDocxPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<PdfDocxSource> Sources)
{
    public ConfirmedPdfDocx Confirm()
    {
        if (BatchId == Guid.Empty || Settings?.Operation != "convert" || Settings.Preferences is null ||
            Sources.IsDefaultOrEmpty || Sources.Length > 128 || Sources.Any(x => x is null) || Sources.Select(x => x.ItemId).Distinct().Count() != Sources.Length ||
            Sources.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sources.Length)
            throw new InvalidDataException("Invalid PDF-to-Word batch.");
        foreach (var source in Sources)
        {
            source.Validate();
            if (source.NeedsOcr || source.TextCharacters == 0) throw new InvalidDataException("This PDF needs OCR before editable Word conversion.");
        }
        if (Sources.Sum(x => x.FileBytes) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("PDF-to-Word batch exceeds its source budget.");
        Settings.SelectOutput(false, false, false, false);
        return new(this);
    }
}

public sealed class ConfirmedPdfDocx
{
    internal ConfirmedPdfDocx(PdfDocxPlan plan) { Plan = plan; }
    public PdfDocxPlan Plan { get; }
}

public sealed record PdfDocxWork(PdfDocxSource Source, Guid OutputId, string TemporaryPath, string Policy = PdfDocxWork.CurrentPolicy)
{
    public const string CurrentPolicy = "pdf-docx-best-effort-1";
    public const int MaximumOutputBytes = 64 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing PDF-to-Word source.");
        Source.Validate();
        if (Source.NeedsOcr || Source.TextCharacters == 0 || OutputId == Guid.Empty || OutputId == Source.ItemId ||
            Policy != CurrentPolicy || !PdfFileProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(TemporaryPath), System.IO.Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid PDF-to-Word output reservation.");
    }
}

public sealed record PdfDocxResult(OutputValidation Validation, string SourceSha256, long OutputBytes,
    int PageCount, int TextCharacters, int ImageCount, int TableCount, string Policy, string EngineIdentity,
    int FigureCount = 0, int FigureTextCharacters = 0);
