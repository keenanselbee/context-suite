using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Pdf;

public sealed record PdfPageConversionPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<PdfRasterSource> Sources)
{
    public const long MaximumBatchPixels = 512_000_000;
    public const int MaximumBatchPages = 4096;

    public static PdfPageConversionPlan Create(Guid batchId, IEnumerable<PdfRasterSource> sources, BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "convert" || settings.Preferences is null)
            throw new InvalidDataException("Invalid PDF page conversion settings.");
        var selected = sources.Take(MaximumBatchPages + 1).ToImmutableArray();
        if (selected.IsEmpty || selected.Length > MaximumBatchPages || selected.Any(source => source is null))
            throw new InvalidDataException("Invalid PDF page conversion selection.");
        long pixels = 0, pages = 0;
        foreach (var source in selected)
        {
            source.Validate();
            pages += source.Document.Pages.Length;
            pixels += source.Document.Pages.Sum(page => (long)page.Width * page.Height);
        }
        if (pages > MaximumBatchPages || pixels > MaximumBatchPixels || selected.Select(source => source.ItemId).Distinct().Count() != selected.Length)
            throw new InvalidDataException("PDF selection exceeds the page conversion budget or repeats an item identity.");
        settings.SelectOutput(false, false, false, false);
        return new(batchId, settings, selected);
    }

    public ConfirmedPdfPageConversion Confirm()
    {
        if (Sources.IsDefaultOrEmpty) throw new InvalidDataException("No PDF pages selected.");
        _ = Create(BatchId, Sources, Settings);
        return new(this);
    }
}

public sealed class ConfirmedPdfPageConversion
{
    internal ConfirmedPdfPageConversion(PdfPageConversionPlan plan) { Plan = plan; }
    public PdfPageConversionPlan Plan { get; }
}

public sealed record PdfPageWork(PdfRasterSource Source, int PageIndex, Guid OutputId, string TemporaryPath,
    string Policy = PdfRasterProtocol.Policy)
{
    public const int MaximumOutputBytes = 64 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing PDF rendering source.");
        Source.Validate();
        if (PageIndex < 0 || PageIndex >= Source.Document.Pages.Length || OutputId == Guid.Empty || OutputId == Source.ItemId ||
            Policy != PdfRasterProtocol.Policy || !PdfFileProbe.ValidPath(TemporaryPath) ||
            Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            string.Equals(Path.GetFullPath(TemporaryPath), Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid PDF page output reservation or policy.");
    }
}

public sealed record PdfPageResult(OutputValidation Validation, PdfRasterPage Page, string SourceSha256,
    long OutputBytes, string PixelSha256, string Policy, string EngineIdentity);
