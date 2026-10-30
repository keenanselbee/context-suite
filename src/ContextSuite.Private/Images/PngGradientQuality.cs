namespace ContextSuite.Private.Images;

// Fixed plateau veto; this is not a universal perceptual-quality guarantee.
internal sealed record PngGradientQuality(int EligibleRuns, int FlattenedRuns, double FlattenedPercent, bool Rejected)
{
    internal static PngGradientQuality Measure(byte[] source, byte[] output, int width, int height, CancellationToken cancellationToken)
    {
        if (width < 1 || height < 1 || source.Length != checked(width * height * 4) || source.Length != output.Length)
            throw new ArgumentException("RGBA dimensions");
        int eligible = 0, flattened = 0;
        // Native-resolution 32-pixel horizontal/vertical runs on an 8-pixel grid.
        // Black/white composites also cover visible translucent gradients without exposing hidden RGB.
        foreach (int background in new[] { 0, 255 })
        for (int y = 0; y < height; y += 8)
        for (int x = 0; x < width; x += 8)
        for (int direction = 0; direction < 2; direction++)
        {
            if (direction == 0 ? x + 31 >= width : y + 31 >= height) continue;
            cancellationToken.ThrowIfCancellationRequested();
            int start = (y * width + x) * 4, step = direction == 0 ? 4 : width * 4;
            for (int c = 0; c < 3; c++)
            {
                double sourceLeft = 0, sourceRight = 0, outputLeft = 0, outputRight = 0;
                double previous = 0; bool increasing = false, decreasing = false, smooth = true;
                for (int k = 0; k < 32; k++)
                {
                    int i = start + k * step;
                    double value = Composite(source[i + c], source[i + 3], background);
                    if (k != 0)
                    {
                        double delta = value - previous;
                        if (Math.Abs(delta) > 1.01) { smooth = false; break; }
                        increasing |= delta > .01; decreasing |= delta < -.01;
                        if (increasing && decreasing) { smooth = false; break; }
                    }
                    previous = value;
                    if (k < 4)
                    {
                        sourceLeft += value;
                        outputLeft += Composite(output[i + c], output[i + 3], background);
                    }
                    if (k >= 28)
                    {
                        sourceRight += value;
                        outputRight += Composite(output[i + c], output[i + 3], background);
                    }
                }
                double sourceProgress = (sourceRight - sourceLeft) / 4;
                if (!smooth || Math.Abs(sourceProgress) < 2 || Math.Abs(sourceProgress) > 24) continue;
                eligible++;
                double outputProgress = (outputRight - outputLeft) / 4 * Math.Sign(sourceProgress);
                if (outputProgress <= Math.Abs(sourceProgress) * .25) flattened++;
            }
        }
        double percent = eligible == 0 ? 0 : 100.0 * flattened / eligible;
        // Fixed recipe veto. Absence of eligible runs is explicitly not visual approval.
        return new(eligible, flattened, percent, eligible >= 32 && percent >= 5);
    }

    private static double Composite(byte value, byte alpha, int background) => value * (alpha / 255.0) + background * (1 - alpha / 255.0);
}
