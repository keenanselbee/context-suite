using System.Collections.Immutable;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Pdf;

public sealed record PdfFileProbe(Guid ItemId, string Path)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !ValidPath(Path)) throw new InvalidDataException("Invalid PDF source identity or path.");
    }

    internal static bool ValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path) &&
        path.Length <= 32700 && path.IndexOfAny(['\0', '\r', '\n']) < 0;
}

// Probe facts help planning; complete preservation admission is repeated during execution.
public sealed record PdfFileSource(Guid ItemId, string Path, string Sha256, long FileBytes, PdfProbeFacts Facts)
{
    public const int MaximumFileBytes = 16 * 1024 * 1024;
    public void Validate()
    {
        new PdfFileProbe(ItemId, Path).Validate();
        if (Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) || FileBytes is <= 0 or > MaximumFileBytes || Facts is null)
            throw new InvalidDataException("Invalid bounded PDF source facts.");
        Facts.Validate();
    }
}

public sealed record PdfOptimizationItem(PdfFileSource Source, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

public sealed record PdfOptimizationPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<PdfOptimizationItem> Items)
{
    public const string Policy = "pdf-structural-1";
    public bool HasExecutableItems => !Items.IsDefaultOrEmpty && Items.Any(item => item.CanExecute);

    public static PdfOptimizationPlan Create(Guid batchId, IEnumerable<PdfFileSource> sources, BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null)
            throw new InvalidDataException("Invalid PDF optimization settings or batch.");
        var items = sources.Take(4097).Select(source =>
        {
            source.Validate();
            return new PdfOptimizationItem(source, source.Facts.IsEncrypted ? "Encrypted PDFs cannot be optimized." :
                source.Facts.ReportedSignatureFieldCount > 0 ? "PDFs with signature fields cannot be optimized." : null);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(item => item.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid PDF optimization selection.");
        return new(batchId, settings, items);
    }

    public ConfirmedPdfOptimization Confirm()
    {
        if (Items.IsDefaultOrEmpty || Items.Any(item => item?.Source is null)) throw new InvalidDataException("No valid PDF optimization plan.");
        var expected = Create(BatchId, Items.Select(item => item.Source), Settings);
        if (!HasExecutableItems || !expected.Items.SequenceEqual(Items)) throw new InvalidDataException("No valid PDF optimization plan.");
        // Document transformations currently always publish copies, including when
        // the saved Optimize preference requests replacement for supported media.
        Settings.SelectOutput(false, false, false, false);
        return new(this);
    }
}

public sealed class ConfirmedPdfOptimization
{
    internal ConfirmedPdfOptimization(PdfOptimizationPlan plan) { Plan = plan; }
    public PdfOptimizationPlan Plan { get; }
}

public sealed record PdfOptimizationWork(PdfFileSource Source, string TemporaryPath, string Policy = PdfOptimizationPlan.Policy)
{
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing PDF source.");
        Source.Validate();
        if (Policy != PdfOptimizationPlan.Policy || !PdfFileProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(TemporaryPath), System.IO.Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid PDF optimization work or output reservation.");
    }
}

public sealed record PdfWorkResult(OutputValidation Validation, long SourceBytes, long OutputBytes, string SourceSha256,
    string Policy, string EngineIdentity);
