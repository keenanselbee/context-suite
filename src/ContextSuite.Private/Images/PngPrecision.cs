using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// Display-referred sRGB sample policy. No palette, dither, alpha reduction or color transform.
internal static class PngPrecision
{
    public static byte[] Reduce(byte[] source, int bits, CancellationToken cancellationToken)
    {
        if (bits is not (6 or 7) || source.Length % 4 != 0) throw new InvalidDataException("Invalid precision policy.");
        var output = source.ToArray();
        var levels = (1 << bits) - 1;
        for (var i = 0; i < source.Length; i += 4)
        {
            if (i % 65536 == 0) cancellationToken.ThrowIfCancellationRequested();
            if (source[i + 3] == 0) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var index = (source[i + channel] * levels + 127) / 255;
                output[i + channel] = (byte)((index * 255 + levels / 2) / levels);
            }
        }
        return output;
    }

    public static bool MeetsQuality(byte[] source, byte[] output, int width, int height, int bits, CancellationToken cancellationToken, bool palette = false)
    {
        if (bits is not (6 or 7) || width <= 0 || height <= 0 || source.Length != checked(width * height * 4) || output.Length != source.Length)
            throw new InvalidDataException("Invalid PNG quality buffers.");
        var maximumError = palette ? 48 : bits == 7 ? 1 : 2;
        for (var i = 0; i < source.Length; i += 4)
        {
            if (i % 65536 == 0) cancellationToken.ThrowIfCancellationRequested();
            if (source[i + 3] != output[i + 3]) return false;
            for (var c = 0; c < 3; c++)
                if (Math.Abs(source[i + c] - output[i + c]) > (source[i + 3] == 0 ? 0 : maximumError)) return false;
        }
        // Same fixed research screens, evaluated at full resolution against black and white.
        foreach (var background in new[] { 0, 255 })
        {
            double squares = 0, ssim = 0;
            var tiles = 0;
            for (var ty = 0; ty < height; ty += 8)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var tx = 0; tx < width; tx += 8)
                {
                    double tileSquares = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
                    var count = 0;
                    for (var y = ty; y < Math.Min(height, ty + 8); y++)
                    for (var x = tx; x < Math.Min(width, tx + 8); x++)
                    {
                        var i = (y * width + x) * 4;
                        double lx = 0, ly = 0, alpha = source[i + 3] / 255.0;
                        for (var c = 0; c < 3; c++)
                        {
                            double a = source[i + c] * alpha + background * (1 - alpha);
                            double b = output[i + c] * alpha + background * (1 - alpha);
                            tileSquares += (a - b) * (a - b);
                            var weight = c == 0 ? .2126 : c == 1 ? .7152 : .0722;
                            lx += weight * a; ly += weight * b;
                        }
                        sx += lx; sy += ly; sxx += lx * lx; syy += ly * ly; sxy += lx * ly; count++;
                    }
                    if (Math.Sqrt(tileSquares / (3 * count)) > (bits == 7 ? 6 : 12)) return false;
                    double mx = sx / count, my = sy / count;
                    double vx = Math.Max(0, sxx / count - mx * mx), vy = Math.Max(0, syy / count - my * my), covariance = sxy / count - mx * my;
                    ssim += ((2 * mx * my + 6.5025) * (2 * covariance + 58.5225)) /
                        ((mx * mx + my * my + 6.5025) * (vx + vy + 58.5225));
                    squares += tileSquares;
                    tiles++;
                }
            }
            if (Math.Sqrt(squares / (width * height * 3)) > (bits == 7 ? 2 : 4) || ssim / tiles < (bits == 7 ? .98 : .95)) return false;
        }
        return true;
    }
}
