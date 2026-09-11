using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Audio;

public sealed record AudioFileProbe(Guid ItemId, string Path);
public sealed record AudioFileSource(Guid ItemId, string Path, string Sha256, long FileBytes,
    AudioProbeFacts Facts, string? OptimizationBlockReason = null, string? ConversionBlockReason = null)
{
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public void Validate()
    {
        if (ItemId == Guid.Empty || string.IsNullOrWhiteSpace(Path) || !System.IO.Path.IsPathFullyQualified(Path) ||
            Path.Length > 32700 || Path.IndexOfAny(['\0', '\r', '\n']) >= 0 || Sha256 is null || Sha256.Length != 64 ||
            !Sha256.All(char.IsAsciiHexDigit) || FileBytes is <= 0 or > MaximumFileBytes || Facts is null ||
            OptimizationBlockReason?.Length > 1024 || ConversionBlockReason?.Length > 1024)
            throw new InvalidDataException("Audio source facts are incomplete or exceed their bounds.");
        Facts.Validate();
    }
}

public sealed record FlacOptimizationItem(AudioFileSource Source, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

public sealed record FlacOptimizationPlan(Guid BatchId, BatchSettings Settings, bool ReplaceOriginal,
    ImmutableArray<FlacOptimizationItem> Items)
{
    public const string Policy = "flac-lossless-1";
    public bool HasExecutableItems => !Items.IsDefaultOrEmpty && Items.Any(item => item.CanExecute);

    public static FlacOptimizationPlan Create(Guid batchId, IEnumerable<AudioFileSource> sources, BatchSettings settings, bool replaceOriginal = false)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null)
            throw new InvalidDataException("Invalid FLAC optimization batch or settings.");
        var items = sources.Take(4097).Select(source =>
        {
            source.Validate();
            var reason = source.OptimizationBlockReason;
            try { AudioConversionPlan.CreateFlacOptimization(source.Facts); }
            catch (NotSupportedException) { reason ??= "This file does not have a supported FLAC optimization plan."; }
            return new FlacOptimizationItem(source, reason);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(item => item.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid FLAC optimization selection.");
        return new(batchId, settings, replaceOriginal, items);
    }

    public ConfirmedFlacOptimization Confirm(bool replacementConfirmed, bool replacementAvailable)
    {
        if (Items.IsDefaultOrEmpty || Items.Any(item => item?.Source is null))
            throw new InvalidDataException("No valid FLAC optimization plan is available.");
        var expected = Create(BatchId, Items.Select(item => item.Source), Settings, ReplaceOriginal);
        if (!HasExecutableItems || !expected.Items.SequenceEqual(Items)) throw new InvalidDataException("No valid FLAC optimization plan is available.");
        Settings.SelectOutput(ReplaceOriginal, replacementConfirmed, false, replacementAvailable);
        return new(this);
    }
}

public sealed class ConfirmedFlacOptimization
{
    internal ConfirmedFlacOptimization(FlacOptimizationPlan plan) { Plan = plan; }
    public FlacOptimizationPlan Plan { get; }
}

public sealed record FlacOptimizationWork(AudioFileSource Source, string TemporaryPath, string Policy = FlacOptimizationPlan.Policy);
public sealed record AudioWorkResult(OutputValidation Validation, long SourceBytes, long OutputBytes, long DecodedFrames,
    string SourceSha256, string Policy, string EngineIdentity);
