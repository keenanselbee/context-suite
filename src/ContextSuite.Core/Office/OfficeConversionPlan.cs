using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Office;

// Facts from application preflight. Execution must recheck the source identity;
// a catalog extension alone never establishes conversion eligibility.
public sealed record OfficeConversionSource(Guid ItemId, string Path, string Format, string Calculation,
    long FileBytes, string Sha256)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path) || Format is not ("docx" or "xlsx" or "pptx") ||
            (Format == "xlsx" ? Calculation is not ("cached" or "recalculate") : Calculation != "none") ||
            FileBytes is <= 0 or > OfficeHostProtocol.MaximumSourceBytes || Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid Office source or explicit export policy.");
    }
}

// One coordinated batch with one PDF copy per document. Unlike image-to-PDF,
// Office documents are not merged and do not require a page-order planner.
public sealed record OfficeConversionPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<OfficeConversionSource> Sources)
{
    public const int MaximumFiles = 4096;
    public const int MaximumPathCharacters = 1_000_000;
    public OutputPolicy Output => Settings.SelectOutput(false, false, true, false);

    public static OfficeConversionPlan Create(Guid batchId, IEnumerable<OfficeConversionSource> sources, BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "convert" || settings.Preferences is null ||
            settings.Preferences.OutputDirectory is { } folder && !PdfFileProbe.ValidPath(folder))
            throw new InvalidDataException("Invalid Office conversion settings.");
        ArgumentNullException.ThrowIfNull(sources);
        var selected = sources.Take(MaximumFiles + 1).ToImmutableArray();
        if (selected.IsEmpty || selected.Length > MaximumFiles || selected.Any(source => source is null))
            throw new InvalidDataException("Select a bounded set of Office documents.");
        foreach (var source in selected) source.Validate();
        if (selected.Sum(source => (long)source.Path.Length) > MaximumPathCharacters ||
            selected.Select(source => source.ItemId).Distinct().Count() != selected.Length ||
            selected.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            throw new InvalidDataException("Office selection has duplicate files or exceeds its path budget.");
        settings.SelectOutput(false, false, true, false);
        return new(batchId, settings, selected);
    }

    public ConfirmedOfficeConversion Confirm()
    {
        if (Sources.IsDefault) throw new InvalidDataException("Missing Office selection.");
        _ = Create(BatchId, Sources, Settings);
        return new(this);
    }
}

public sealed class ConfirmedOfficeConversion
{
    internal ConfirmedOfficeConversion(OfficeConversionPlan plan) { Plan = plan; }
    public OfficeConversionPlan Plan { get; }
}
