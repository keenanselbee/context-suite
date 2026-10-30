internal sealed record QuantizationMetrics(double CompositeRmse, double WorstTileRmse, double MinimumMeanSsim,
    double AlphaMeanError, int AlphaMaximumError, int ChangedPixels, int HiddenRgbChangedPixels)
{
    // Display-referred sRGB byte values, black AND white backgrounds, non-overlapping 8x8 tiles.
    // These are screening measurements, not a perceptual-quality guarantee or pngquant quality score.
    internal static QuantizationMetrics Measure(byte[] source, byte[] output, int width, int height)
    {
        if (source.Length != checked(width * height * 4) || source.Length != output.Length) throw new ArgumentException("RGBA dimensions");
        double alphaSum = 0; int alphaMax = 0, changed = 0, hidden = 0;
        for (var i = 0; i < source.Length; i += 4)
        {
            int alphaError = Math.Abs(source[i + 3] - output[i + 3]); alphaSum += alphaError; alphaMax = Math.Max(alphaMax, alphaError);
            if (!source.AsSpan(i, 4).SequenceEqual(output.AsSpan(i, 4))) changed++;
            if (source[i + 3] == 0 && !source.AsSpan(i, 3).SequenceEqual(output.AsSpan(i, 3))) hidden++;
        }
        double maximumRmse = 0, worstTile = 0, minimumSsim = 1;
        foreach (int background in new[] { 0, 255 })
        {
            double squares = 0, ssim = 0; int tiles = 0;
            for (var ty = 0; ty < height; ty += 8)
            for (var tx = 0; tx < width; tx += 8)
            {
                double tileSquares = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0; int count = 0;
                for (var y = ty; y < Math.Min(height, ty + 8); y++)
                for (var x = tx; x < Math.Min(width, tx + 8); x++)
                {
                    int i = (y * width + x) * 4; double lx = 0, ly = 0;
                    for (int c = 0; c < 3; c++)
                    {
                        double a = Composite(source[i + c], source[i + 3], background);
                        double b = Composite(output[i + c], output[i + 3], background);
                        tileSquares += (a - b) * (a - b);
                        double weight = c == 0 ? .2126 : c == 1 ? .7152 : .0722;
                        lx += weight * a; ly += weight * b;
                    }
                    sx += lx; sy += ly; sxx += lx * lx; syy += ly * ly; sxy += lx * ly; count++;
                }
                double mx = sx / count, my = sy / count;
                double vx = Math.Max(0, sxx / count - mx * mx), vy = Math.Max(0, syy / count - my * my), covariance = sxy / count - mx * my;
                const double c1 = 6.5025, c2 = 58.5225;
                ssim += ((2 * mx * my + c1) * (2 * covariance + c2)) / ((mx * mx + my * my + c1) * (vx + vy + c2));
                squares += tileSquares; worstTile = Math.Max(worstTile, Math.Sqrt(tileSquares / (3 * count))); tiles++;
            }
            maximumRmse = Math.Max(maximumRmse, Math.Sqrt(squares / (width * height * 3)));
            minimumSsim = Math.Min(minimumSsim, ssim / tiles);
        }
        return new(maximumRmse, worstTile, minimumSsim, alphaSum / (width * height), alphaMax, changed, hidden);
    }

    internal static double Composite(byte color, byte alpha, int background) => color * (alpha / 255.0) + background * (1 - alpha / 255.0);

    internal static byte[] CompositePreview(byte[] pixels, int background)
    {
        var result = new byte[pixels.Length];
        for (int i = 0; i < result.Length; i += 4)
        {
            for (int c = 0; c < 3; c++) result[i + c] = (byte)Math.Round(Composite(pixels[i + c], pixels[i + 3], background));
            result[i + 3] = 255;
        }
        return result;
    }
}
