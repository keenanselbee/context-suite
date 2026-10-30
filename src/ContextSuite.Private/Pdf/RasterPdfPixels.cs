using ContextSuite.Core.Pdf;

namespace ContextSuite.Private.Pdf;

public sealed record RasterPdfPixels(RasterPdfPage Page, byte[] Color, byte[]? Alpha, byte[] IccProfile);
