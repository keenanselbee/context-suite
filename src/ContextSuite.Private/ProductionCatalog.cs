using ContextSuite.Core.Operations;

namespace ContextSuite.Private;

// Format-pair capabilities are conditional on bounded content probing and a valid plan.
// The adapter contracts exercise every advertised pair; variants are not inferred from extensions.
public sealed class ProductionCatalog : IMediaCatalog
{
    public IReadOnlyList<MediaCapability> Capabilities { get; } = Array.AsReadOnly(
        (from source in new[] { "png", "jpeg", "webp", "bmp", "tga" }
         from target in new[] { "png", "jpeg", "webp", "bmp", "tga" }
         where source != target
         select new MediaCapability("convert", source, target))
        .Concat(new[] { "png", "jpeg", "webp", "bmp", "tga" }.Select(source => new MediaCapability("convert", source, "dds")))
        .Concat(new[] { "png", "jpeg", "webp", "bmp", "tga" }.Select(source => new MediaCapability("convert", source, "ico")))
        .Concat(new[] { "png", "jpeg", "webp", "bmp", "tga" }.Select(source => new MediaCapability("convert", source, "avif")))
        .Concat([new MediaCapability("convert", "dds", "dds"), new MediaCapability("convert", "dds", "png"),
            new MediaCapability("optimize", "png", "png")]).ToArray());
}
