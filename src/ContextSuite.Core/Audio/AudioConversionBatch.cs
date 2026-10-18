using System.Collections.Immutable;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Audio;

public sealed record AudioConversionItem(AudioFileSource Source, AudioConversionPlan? Encoding, string? BlockReason)
{
    public bool CanExecute => BlockReason is null && Encoding is { AlreadyTarget: false };
}

public sealed record AudioConversionBatch(Guid BatchId, AudioFormat Target, BatchSettings Settings, bool ReplaceOriginal,
    ImmutableArray<AudioConversionItem> Items, WemEncodingOptions? Wem = null)
{
    public bool HasExecutableItems => !Items.IsDefaultOrEmpty && Items.Any(item => item.CanExecute);
    public AudioConversionConsent RequiredConsent => Items.Where(item => item.CanExecute)
        .Aggregate(AudioConversionConsent.None, (consent, item) => consent | item.Encoding!.RequiredConsent);
    public ImmutableArray<string> Notices => Items.Where(item => item.CanExecute).SelectMany(item => item.Encoding!.Notices).Distinct().ToImmutableArray();

    public static AudioConversionBatch Create(Guid batchId, IEnumerable<AudioFileSource> sources, AudioFormat target,
        BatchSettings settings, bool replaceOriginal = false, WemEncodingOptions? wem = null)
    {
        if (batchId == Guid.Empty || !Enum.IsDefined(target) || settings?.Operation != "convert" || settings.Preferences is null)
            throw new InvalidDataException("Invalid audio conversion batch, target or settings.");
        if (target == AudioFormat.Wem) (wem ?? throw new InvalidDataException("Missing WEM encoding options.")).Validate();
        else if (wem is not null) throw new InvalidDataException("WEM options cannot accompany another target.");
        var items = sources.Take(4097).Select(source =>
        {
            if (source is null) throw new InvalidDataException("Missing audio source.");
            source.Validate();
            AudioConversionPlan? encoding = null; var reason = source.ConversionBlockReason;
            try
            {
                if (VideoAudioSelection.IsVideoPath(source.Path) && source.Video is null)
                    throw new NotSupportedException("Select an audio track before extracting it from video.");
                encoding = AudioConversionPlan.Create(source.Facts, target, wem, source.Video);
            }
            catch (NotSupportedException error) { reason ??= target == AudioFormat.Wem ? error.Message : "This audio cannot use the selected format without changing an unsupported feature."; }
            return new AudioConversionItem(source, encoding, reason);
        }).ToImmutableArray();
        if (items.IsEmpty || items.Length > 4096 || items.Select(item => item.Source.ItemId).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid audio conversion selection.");
        return new(batchId, target, settings, replaceOriginal && items.All(item => item.Source.Video is null), items, wem);
    }

    public ConfirmedAudioConversion Confirm(AudioConversionConsent accepted, bool replacementConfirmed, bool replacementAvailable)
    {
        if (Items.IsDefaultOrEmpty || Items.Any(item => item?.Source is null)) throw new InvalidDataException("Missing audio conversion plan.");
        var expected = Create(BatchId, Items.Select(item => item.Source), Target, Settings, ReplaceOriginal, Wem);
        if (!HasExecutableItems || ReplaceOriginal != expected.ReplaceOriginal || !expected.Items.Zip(Items).All(pair => pair.First.BlockReason == pair.Second.BlockReason &&
            Equivalent(pair.First.Encoding, pair.Second.Encoding))) throw new InvalidDataException("Audio conversion plan was changed after planning.");
        foreach (var item in Items.Where(item => item.CanExecute)) item.Encoding!.RequireConsent(accepted);
        Settings.SelectOutput(ReplaceOriginal, replacementConfirmed, false, replacementAvailable);
        return new(this, accepted);
    }

    private static bool Equivalent(AudioConversionPlan? expected, AudioConversionPlan? actual)
    {
        if (expected is null || actual is null) return expected is null && actual is null;
        return expected with { Notices = [] } == actual with { Notices = [] } && !actual.Notices.IsDefault && expected.Notices.SequenceEqual(actual.Notices);
    }
}

public sealed class ConfirmedAudioConversion
{
    internal ConfirmedAudioConversion(AudioConversionBatch plan, AudioConversionConsent accepted) { Plan = plan; AcceptedConsent = accepted; }
    public AudioConversionBatch Plan { get; }
    public AudioConversionConsent AcceptedConsent { get; }
}

public sealed record AudioConversionWork(AudioFileSource Source, string TemporaryPath, AudioFormat Target,
    AudioConversionConsent AcceptedConsent, string Policy = AudioConversionPlan.CurrentPolicy, WemEncodingOptions? Wem = null)
{
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing audio conversion source.");
        Source.Validate();
        if (!Enum.IsDefined(Target) || Policy != AudioConversionPlan.CurrentPolicy ||
            string.IsNullOrWhiteSpace(TemporaryPath) || !Path.IsPathFullyQualified(TemporaryPath) || TemporaryPath.Length > 32700 ||
            TemporaryPath.IndexOfAny(['\0', '\r', '\n']) >= 0 || Source.ConversionBlockReason is not null ||
            Path.GetFileName(TemporaryPath) != $".context-suite-{Source.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(Source.Path), Path.GetFullPath(TemporaryPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid audio conversion work or output reservation.");
        if (VideoAudioSelection.IsVideoPath(Source.Path) && Source.Video is null)
            throw new InvalidDataException("Video extraction requires an explicit audio track.");
        var plan = AudioConversionPlan.Create(Source.Facts, Target, Wem, Source.Video);
        if (plan.AlreadyTarget) throw new InvalidDataException("Same-format audio must remain unchanged without encoding.");
        plan.RequireConsent(AcceptedConsent);
    }
}
