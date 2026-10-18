using System.Collections.Immutable;

namespace ContextSuite.Core.Images;

public sealed record IconFrameLayout(uint Size, uint Width, uint Height, uint Left, uint Top)
{
    public static ImmutableArray<IconFrameLayout> Create(uint width, uint height)
    {
        if (width is 0 or > 16384 || height is 0 or > 16384 || (ulong)width * height > 40_000_000)
            throw new InvalidDataException("Icon source dimensions exceed the image limits.");
        var frames = ImmutableArray.CreateBuilder<IconFrameLayout>();
        foreach (var size in new uint[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var scale = Math.Min(1, (double)size / Math.Max(width, height));
            var fittedWidth = Math.Max(1u, (uint)Math.Round(width * scale, MidpointRounding.AwayFromZero));
            var fittedHeight = Math.Max(1u, (uint)Math.Round(height * scale, MidpointRounding.AwayFromZero));
            frames.Add(new(size, fittedWidth, fittedHeight, (size - fittedWidth) / 2, (size - fittedHeight) / 2));
            // Include the next standard canvas for a small source without enlarging
            // its artwork. Larger, mostly empty canvases would misrepresent its size.
            if (size >= Math.Max(width, height)) break;
        }
        return frames.ToImmutable();
    }
}
