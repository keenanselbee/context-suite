using ContextSuite.Core.Operations;

namespace ContextSuite.Core.Images;

public sealed record ImageProbe(Guid ItemId, string SourcePath);
public sealed record ImageWork(ImageItemPlan Plan, ImageConversionOptions Options, string TemporaryPath);
public sealed record ImageWorkResult(OutputValidation Validation, uint Width, uint Height, uint BitDepth, long OutputBytes, string? EngineIdentity = null,
    string? OptimizationMethod = null, string? OptimizationReason = null);

// Raw, bounded display pixels: the UI must not invoke another decoder on untrusted source/preview files.
public sealed record ImagePreview(uint Width, uint Height, byte[] BgraPixels);
public sealed record ImagePreviewRequest(ImageSourceFacts Source, ImageConversionOptions? Options = null);
