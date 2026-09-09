using System.Collections.Immutable;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Images;

public enum PngOptimizationPreset { Lossless, Balanced, Smallest, Auto }

// Fixed recipes; no alpha changes or resizing. The researched fdEC exception
// requires a copy and an explicit completion warning, even with replacement on.
public sealed record PngOptimizationItem(ImageSourceFacts Source, string? BlockReason)
{
    public bool CanExecute => BlockReason is null;
}

public sealed record PngOptimizationPlan(Guid BatchId, BatchSettings Settings, bool ReplaceOriginal,
    ImmutableArray<PngOptimizationItem> Items, PngOptimizationPreset Preset = PngOptimizationPreset.Lossless)
{
    public const string Policy = "png-lossless-preserve-v1";
    public const string BalancedPolicy = "png-balanced-fixed-v3";
    public const string SmallestPolicy = "png-smallest-palette-v3";
    public const string AutoPolicy = "png-auto-fixed-v2";
    public string SelectedPolicy => Preset switch
    {
        PngOptimizationPreset.Lossless => Policy,
        PngOptimizationPreset.Balanced => BalancedPolicy,
        PngOptimizationPreset.Smallest => SmallestPolicy,
        PngOptimizationPreset.Auto => AutoPolicy,
        _ => throw new InvalidDataException("Unknown PNG preset.")
    };
    public bool HasExecutableItems => Items.Any(item => item.CanExecute);

    public ConfirmedPngOptimization Confirm(bool replacementConfirmed, bool replacementAvailable)
    {
        var expected = Create(BatchId, Items.Select(item => item.Source), Settings, ReplaceOriginal, Preset);
        if (!expected.Items.SequenceEqual(Items) || !HasExecutableItems)
            throw new InvalidDataException("No valid PNG optimization plan is available.");
        Settings.SelectOutput(ReplaceOriginal, replacementConfirmed, false, replacementAvailable);
        return new(this);
    }

    public static PngOptimizationPlan Create(Guid batchId, IEnumerable<ImageSourceFacts> sources,
        BatchSettings settings, bool replaceOriginal = false, PngOptimizationPreset preset = PngOptimizationPreset.Lossless)
    {
        if (batchId == Guid.Empty || settings?.Operation != "optimize" || settings.Preferences is null || !Enum.IsDefined(preset))
            throw new InvalidDataException("Invalid optimization batch or settings.");
        var items = sources.Take(4097).Select(source =>
        {
            source.Validate();
            return new PngOptimizationItem(source, source.Format != ImageFormat.Png ? "Optimization currently supports PNG only." :
                source.UnsupportedReason);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(i => i.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid optimization selection.");
        return new(batchId, settings, replaceOriginal, items, preset);
    }
}

public sealed class ConfirmedPngOptimization
{
    internal ConfirmedPngOptimization(PngOptimizationPlan plan) { Plan = plan; }
    public PngOptimizationPlan Plan { get; }
}

public sealed record PngOptimizationWork(ImageSourceFacts Source, string TemporaryPath, string Policy = PngOptimizationPlan.Policy);
