using System.Collections.Immutable;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.Gif;

public sealed record GifProbe(Guid ItemId, string Path)
{
    public void Validate()
    {
        if (ItemId == Guid.Empty || !PdfFileProbe.ValidPath(Path)) throw new InvalidDataException("Invalid GIF source.");
    }
}

public sealed record GifSourceFacts(Guid ItemId, string Path, string Sha256, long FileBytes,
    uint Width, uint Height, int FrameCount, ImmutableArray<int> DelayCentiseconds, int? LoopCount)
{
    public const long MaximumFileBytes = 128 * 1024 * 1024;
    public const long MaximumDisplayedPixels = 512_000_000;
    public void Validate()
    {
        new GifProbe(ItemId, Path).Validate();
        if (Sha256 is not { Length: 64 } || !Sha256.All(char.IsAsciiHexDigit) || FileBytes is < 14 or > MaximumFileBytes ||
            Width is 0 or > 16384 || Height is 0 or > 16384 || (long)Width * Height > 40_000_000 ||
            FrameCount is < 1 or > 4096 || (long)Width * Height * FrameCount > MaximumDisplayedPixels ||
            DelayCentiseconds.IsDefault || DelayCentiseconds.Length != FrameCount || DelayCentiseconds.Any(delay => delay is < 0 or > 65535) ||
            LoopCount is < 0 or > 65535)
            throw new InvalidDataException("GIF facts exceed supported frame or pixel limits.");
    }
}

public sealed record GifConversionPlan(Guid BatchId, ImmutableArray<GifSourceFacts> Sources, BatchSettings Settings)
{
    public const long MaximumOutputBytes = 2L * 1024 * 1024 * 1024;
    public static GifConversionPlan Create(Guid id, IEnumerable<GifSourceFacts> sources, BatchSettings settings)
    {
        var selected = sources.Take(4097).ToImmutableArray();
        if (id == Guid.Empty || selected.IsDefaultOrEmpty || selected.Length > 4096 || selected.Any(source => source is null) ||
            settings?.Operation != "convert" || settings.Preferences is null)
            throw new InvalidDataException("Invalid GIF conversion selection.");
        foreach (var source in selected) source.Validate();
        if (selected.Sum(source => (long)source.FrameCount) > 4096 ||
            selected.Sum(source => (long)source.Width * source.Height * source.FrameCount) > GifSourceFacts.MaximumDisplayedPixels ||
            selected.Select(source => source.ItemId).Distinct().Count() != selected.Length ||
            selected.Select(source => System.IO.Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            throw new InvalidDataException("GIF batch exceeds its frame or pixel limit or repeats a source.");
        _ = settings.SelectOutput(false, false, false, false);
        return new(id, selected, settings);
    }
    public ConfirmedGifConversion Confirm()
    {
        _ = Create(BatchId, Sources, Settings);
        return new(this);
    }
}

public sealed class ConfirmedGifConversion
{
    internal ConfirmedGifConversion(GifConversionPlan plan) { Plan = plan; }
    public GifConversionPlan Plan { get; }
}

public sealed record GifWork(GifSourceFacts Source, int FrameIndex, Guid OutputId, string TemporaryPath)
{
    public const long MaximumOutputBytes = 128 * 1024 * 1024;
    public void Validate()
    {
        if (Source is null) throw new InvalidDataException("Missing GIF facts.");
        Source.Validate();
        if (FrameIndex < 0 || FrameIndex >= Source.FrameCount || OutputId == Guid.Empty || OutputId == Source.ItemId ||
            !PdfFileProbe.ValidPath(TemporaryPath) || System.IO.Path.GetFileName(TemporaryPath) != $".context-suite-{OutputId:N}.tmp" ||
            string.Equals(System.IO.Path.GetFullPath(TemporaryPath), System.IO.Path.GetFullPath(Source.Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid GIF frame output reservation.");
    }
}

public sealed record GifWorkResult(OutputValidation Validation, Guid SourceId, string SourceSha256, int FrameIndex,
    uint Width, uint Height, long OutputBytes, string EngineIdentity);
