// Research-only, source-defined mask. No image-specific coordinates or production thresholds.
internal static class GradientProtection
{
    internal static bool[] Mask(byte[] source, int width, int height)
    {
        if (source.Length != checked(width * height * 4)) throw new ArgumentException("RGBA dimensions");
        var mask = new bool[width * height];
        for (int y = 2; y < height - 2; y++)
        for (int x = 2; x < width - 2; x++)
        {
            bool smooth = true;
            for (int c = 0; c < 3 && smooth; c++)
            {
                int low = 255, high = 0;
                for (int dy = -2; dy <= 2 && smooth; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int i = ((y + dy) * width + x + dx) * 4;
                    if (source[i + 3] != 255) { smooth = false; break; }
                    low = Math.Min(low, source[i + c]); high = Math.Max(high, source[i + c]);
                }
                smooth &= high <= 64 && high - low <= 6;
            }
            mask[y * width + x] = smooth;
        }
        return mask;
    }

    internal static void Apply(byte[] source, byte[] output, int width, int height)
    {
        if (source.Length != output.Length) throw new ArgumentException("RGBA dimensions");
        var mask = Mask(source, width, height);
        for (int p = 0; p < mask.Length; p++)
            if (mask[p])
                for (int c = 0; c < 3; c++)
                    output[p * 4 + c] = (byte)Math.Clamp((int)output[p * 4 + c], Math.Max(0, source[p * 4 + c] - 1), Math.Min(255, source[p * 4 + c] + 1));
    }

    internal sealed record Diagnostic(int SmoothDarkPixels, int ComparedEdges, int NewStrongEdges, double NewStrongEdgePercent);

    // Count newly introduced RGB steps >=3 where the original step was <=1, within the source mask.
    // This also detects dither noise: it is a diagnostic, NOT a calibrated banding acceptance score.
    internal static Diagnostic Measure(byte[] source, byte[] output, int width, int height)
    {
        if (source.Length != output.Length) throw new ArgumentException("RGBA dimensions");
        var mask = Mask(source, width, height); int compared = 0, introduced = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = y * width + x;
            if (!mask[p]) continue;
            for (int direction = 0; direction < 2; direction++)
            {
                int q = direction == 0 ? (x + 1 < width ? p + 1 : -1) : (y + 1 < height ? p + width : -1);
                if (q < 0 || !mask[q]) continue;
                int originalStep = 0, outputStep = 0;
                for (int c = 0; c < 3; c++)
                {
                    originalStep = Math.Max(originalStep, Math.Abs(source[p * 4 + c] - source[q * 4 + c]));
                    outputStep = Math.Max(outputStep, Math.Abs(output[p * 4 + c] - output[q * 4 + c]));
                }
                if (originalStep > 1) continue;
                compared++; if (outputStep >= 3) introduced++;
            }
        }
        return new(mask.Count(v => v), compared, introduced, compared == 0 ? 0 : 100.0 * introduced / compared);
    }

    internal static void Verify()
    {
        const int width = 64, height = 16;
        var ramp = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            for (int c = 0; c < 3; c++) ramp[p * 4 + c] = (byte)(p % width);
            ramp[p * 4 + 3] = 255;
        }
        var reduced = QuantizationCandidate.ReducePrecision(ramp, 6);
        if (Measure(ramp, ramp, width, height).NewStrongEdges != 0 || Measure(ramp, reduced, width, height).NewStrongEdges == 0)
            throw new InvalidDataException("Gradient diagnostic ramp contract failed.");
        var mask = Mask(ramp, width, height); var before = reduced.ToArray();
        Apply(ramp, reduced, width, height);
        for (int p = 0; p < mask.Length; p++)
        {
            if (reduced[p * 4 + 3] != ramp[p * 4 + 3]) throw new InvalidDataException("Protection changed alpha.");
            for (int c = 0; c < 3; c++)
                if (mask[p] ? Math.Abs(reduced[p * 4 + c] - ramp[p * 4 + c]) > 1 : reduced[p * 4 + c] != before[p * 4 + c])
                    throw new InvalidDataException("Protection bound or mask contract failed.");
        }
        ramp[8 * width * 4 + 32 * 4 + 3] = 0;
        if (Mask(ramp, width, height)[8 * width + 32]) throw new InvalidDataException("Transparent sample admitted.");
        Console.WriteLine("Gradient diagnostic and protection contracts passed.");
    }
}
