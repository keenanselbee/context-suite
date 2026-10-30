// Research-only plateau veto, not a perceptual-quality guarantee or production policy.
internal sealed record GradientRejection(int EligibleRuns, int FlattenedRuns, double FlattenedPercent, bool Rejected)
{
    internal static GradientRejection Measure(byte[] source, byte[] output, int width, int height)
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
            int start = (y * width + x) * 4, step = direction == 0 ? 4 : width * 4;
            for (int c = 0; c < 3; c++)
            {
                double sourceLeft = 0, sourceRight = 0, outputLeft = 0, outputRight = 0;
                double previous = 0; bool increasing = false, decreasing = false, smooth = true;
                for (int k = 0; k < 32; k++)
                {
                    int i = start + k * step;
                    double value = QuantizationMetrics.Composite(source[i + c], source[i + 3], background);
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
                        outputLeft += QuantizationMetrics.Composite(output[i + c], output[i + 3], background);
                    }
                    if (k >= 28)
                    {
                        sourceRight += value;
                        outputRight += QuantizationMetrics.Composite(output[i + c], output[i + 3], background);
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
        // A provisional extra veto. Absence of eligible runs is explicitly not visual approval.
        return new(eligible, flattened, percent, eligible >= 32 && percent >= 5);
    }

    internal static void Verify()
    {
        const int width = 256, height = 64;
        var ramp = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            for (int c = 0; c < 3; c++) ramp[p * 4 + c] = (byte)(16 + p % width / 8);
            ramp[p * 4 + 3] = 255;
        }
        var plateau = ramp.ToArray(); var dither = ramp.ToArray();
        for (int p = 0; p < width * height; p++)
        for (int c = 0; c < 3; c++)
        {
            plateau[p * 4 + c] = (byte)(ramp[p * 4 + c] / 8 * 8);
            dither[p * 4 + c] = (byte)(ramp[p * 4 + c] + ((p % width + p / width) % 2 == 0 ? -1 : 1));
        }
        if (Measure(ramp, ramp, width, height).FlattenedRuns != 0 ||
            !Measure(ramp, plateau, width, height).Rejected || Measure(ramp, dither, width, height).Rejected)
            throw new InvalidDataException("Plateau identity/rejection/dither contract failed.");
        // Transpose to prove vertical coverage, and use constant partial alpha for composite coverage.
        var vertical = new byte[ramp.Length]; var verticalPlateau = new byte[ramp.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            ramp.AsSpan((y * width + x) * 4, 4).CopyTo(vertical.AsSpan((x * height + y) * 4, 4));
            plateau.AsSpan((y * width + x) * 4, 4).CopyTo(verticalPlateau.AsSpan((x * height + y) * 4, 4));
        }
        if (!Measure(vertical, verticalPlateau, height, width).Rejected) throw new InvalidDataException("Vertical plateau contract failed.");
        for (int p = 0; p < width * height; p++)
        for (int c = 0; c < 3; c++)
        {
            vertical[p * 4 + c] = (byte)(100 - vertical[p * 4 + c]);
            verticalPlateau[p * 4 + c] = (byte)(100 - verticalPlateau[p * 4 + c]);
        }
        if (!Measure(vertical, verticalPlateau, height, width).Rejected) throw new InvalidDataException("Descending plateau contract failed.");
        for (int p = 0; p < width * height; p++) ramp[p * 4 + 3] = plateau[p * 4 + 3] = 200;
        if (!Measure(ramp, plateau, width, height).Rejected) throw new InvalidDataException("Translucent plateau contract failed.");
        Array.Clear(ramp); Array.Clear(plateau);
        for (int p = 0; p < width * height; p++)
            for (int c = 0; c < 3; c++) plateau[p * 4 + c] = 255;
        if (Measure(ramp, plateau, width, height).EligibleRuns != 0) throw new InvalidDataException("Flat/hidden RGB contract failed.");
        for (int p = 0; p < width * height; p++) ramp[p * 4 + 3] = plateau[p * 4 + 3] = 255;
        if (Measure(ramp, plateau, width, height).EligibleRuns != 0 || Measure(new byte[4], new byte[4], 1, 1).EligibleRuns != 0)
            throw new InvalidDataException("Opaque flat/tiny contract failed.");
        Console.WriteLine("Plateau rejection contracts passed: identity, horizontal/vertical/descending, translucent, mild dither, opaque flat, hidden RGB, tiny.");
    }
}
