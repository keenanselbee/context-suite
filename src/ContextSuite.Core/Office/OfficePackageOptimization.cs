using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Office;

public sealed record OfficePackageProbe(Guid ItemId, string Path)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !ValidPath(Path)) throw new InvalidDataException("Invalid document package identity or path.");
    }

    internal static bool ValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path) &&
        path.Length <= 32700 && path.IndexOfAny(['\0', '\r', '\n']) < 0;
}

public sealed record OfficePackageSource(Guid ItemId, string Path, string Sha256, long FileBytes, string Format)
{
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    public void Validate()
    {
        new OfficePackageProbe(ItemId, Path).Validate();
        if (Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) || FileBytes is <= 0 or > MaximumFileBytes ||
            Format is not ("docx" or "xlsx" or "pptx") || !System.IO.Path.GetExtension(Path).Equals("." + Format, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid document package source facts.");
    }
}

public sealed record OfficePackageOptimizationPlan(Guid BatchId, BatchSettings Settings, ImmutableArray<OfficePackageSource> Sources)
{
    public const string Policy = "ooxml-package-lossless-v1";
    public static OfficePackageOptimizationPlan Create(Guid batchId, IEnumerable<OfficePackageSource> sources, BatchSettings settings)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null)
            throw new InvalidDataException("Invalid document optimization batch or settings.");
        var items = sources.Take(4097).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Any(source => source is null) ||
            items.Select(source => source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid document optimization selection.");
        foreach (var source in items) source.Validate();
        return new(batchId, settings, items);
    }

    public ConfirmedOfficePackageOptimization Confirm()
    {
        if (Sources.IsDefaultOrEmpty) throw new InvalidDataException("No document optimization plan.");
        _ = Create(BatchId, Sources, Settings);
        Settings.SelectOutput(false, false, false, false);
        return new(this);
    }
}

public sealed class ConfirmedOfficePackageOptimization
{
    internal ConfirmedOfficePackageOptimization(OfficePackageOptimizationPlan plan) { Plan = plan; }
    public OfficePackageOptimizationPlan Plan { get; }
}

public sealed record OfficePackageWork(OfficePackageSource Source, string TemporaryPath, string Policy = OfficePackageOptimizationPlan.Policy)
{
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing document optimization source.");
        Source.Validate();
        if (Policy != OfficePackageOptimizationPlan.Policy || !OfficePackageProbe.ValidPath(TemporaryPath) ||
            System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(TemporaryPath), System.IO.Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid document optimization work or reservation.");
    }
}

public sealed record OfficePackageResult(OutputValidation Validation, long SourceBytes, long OutputBytes, string SourceSha256,
    string Policy, string EngineIdentity);
