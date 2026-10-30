using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

// Version-one gates operate on the original decoded 8-bit samples, without a
// display transform. Local, edge, and smooth-gradient limits supplement global
// error; no aggregate score can excuse a damaged small region.
internal static class ImageOptimizationQuality
{
    private const int TileSize = 8;
    private const double MinimumPhotographicTileFraction = 0.60;

    internal static bool Accept(byte[] original, byte[] candidate, int width, int height,
        ImageOptimizationPreset preset, CancellationToken token)
    {
        if (width < 1 || height < 1 || original.Length != checked(width * height * 4) || candidate.Length != original.Length)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        var texture = ClassifyTextureTiles(original, width, height, token);
        if (texture.Count(value => value) >= texture.Length * MinimumPhotographicTileFraction)
            return AcceptPhotographic(original, candidate, width, height, preset, texture, token);
        var smallest = preset == ImageOptimizationPreset.Smallest;
        var maximumError = smallest ? 32 : 18;
        var maximumTileMean = smallest ? 5.0 : 2.5;
        var maximumRmse = smallest ? 5.0 : 2.5;
        var maximumBias = smallest ? 1.2 : 0.6;
        var maximumEdgeError = smallest ? 28 : 16;
        var maximumFlatJump = smallest ? 7 : 4;
        var maximumCurvature = smallest ? 10 : 6;
        double squared = 0;
        long[] bias = new long[3];
        for (var tileY = 0; tileY < height; tileY += 8)
        {
            token.ThrowIfCancellationRequested();
            for (var tileX = 0; tileX < width; tileX += 8)
            {
                var errorSum = 0;
                var samples = 0;
                for (var y = tileY; y < Math.Min(tileY + 8, height); y++)
                for (var x = tileX; x < Math.Min(tileX + 8, width); x++)
                {
                    var offset = (y * width + x) * 4;
                    if (original[offset + 3] != candidate[offset + 3]) return false;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var index = offset + channel;
                        var error = candidate[index] - original[index];
                        if (Math.Abs(error) > maximumError || original[offset + 3] == 0 && error != 0) return false;
                        errorSum += Math.Abs(error);
                        samples++;
                        squared += error * error;
                        bias[channel] += error;
                        if (x > 0 && !Pair(index, index - 4)) return false;
                        if (y > 0 && !Pair(index, index - width * 4)) return false;
                        if (x > 1 && !Gradient(index, index - 4, index - 8)) return false;
                        if (y > 1 && !Gradient(index, index - width * 4, index - width * 8)) return false;
                    }
                }
                if (errorSum > samples * maximumTileMean) return false;
            }
        }
        var pixels = (long)width * height;
        return squared <= pixels * 3 * maximumRmse * maximumRmse && bias.All(value => Math.Abs(value) <= pixels * maximumBias);

        bool Pair(int a, int b)
        {
            var before = original[a] - original[b];
            var after = candidate[a] - candidate[b];
            if (Math.Abs(before) >= 24 && (Math.Sign(before) != Math.Sign(after) || Math.Abs(after - before) > maximumEdgeError)) return false;
            return Math.Abs(before) > 1 || Math.Abs(after) <= maximumFlatJump;
        }

        bool Gradient(int a, int b, int c)
        {
            var first = original[a] - original[b];
            var second = original[b] - original[c];
            if (Math.Abs(first) > 8 || Math.Abs(second) > 8 || Math.Abs(first - second) > 2) return true;
            var afterFirst = candidate[a] - candidate[b];
            var afterSecond = candidate[b] - candidate[c];
            return Math.Abs(afterFirst - afterSecond) <= maximumCurvature &&
                !(first >= 2 && second >= 2 && Math.Min(afterFirst, afterSecond) < -2) &&
                !(first <= -2 && second <= -2 && Math.Max(afterFirst, afterSecond) > 2);
        }
    }

    internal static bool[] ClassifyTextureTiles(byte[] rgba, int width, int height, CancellationToken token)
    {
        var columns = (width + TileSize - 1) / TileSize;
        var texture = new bool[columns * ((height + TileSize - 1) / TileSize)];
        var colors = new HashSet<int>();
        for (var tileY = 0; tileY < height; tileY += TileSize)
        {
            token.ThrowIfCancellationRequested();
            for (var tileX = 0; tileX < width; tileX += TileSize)
            {
                colors.Clear();
                var flatPairs = 0;
                var pairs = 0;
                var minimum = 255;
                var maximum = 0;
                for (var y = tileY; y < Math.Min(tileY + TileSize, height); y++)
                for (var x = tileX; x < Math.Min(tileX + TileSize, width); x++)
                {
                    var offset = (y * width + x) * 4;
                    colors.Add(rgba[offset] << 16 | rgba[offset + 1] << 8 | rgba[offset + 2]);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        minimum = Math.Min(minimum, rgba[offset + channel]);
                        maximum = Math.Max(maximum, rgba[offset + channel]);
                    }
                    if (x > tileX) { pairs++; if (FlatPair(offset, offset - 4)) flatPairs++; }
                    if (y > tileY) { pairs++; if (FlatPair(offset, offset - width * 4)) flatPairs++; }
                }
                // Low-color artwork, flat regions, and smooth gradients retain
                // the stricter policy even inside a predominantly textured image.
                texture[tileY / TileSize * columns + tileX / TileSize] =
                    colors.Count >= 24 && maximum - minimum >= 24 && flatPairs <= pairs * 0.50;
            }
        }
        return texture;

        bool FlatPair(int a, int b)
        {
            for (var channel = 0; channel < 3; channel++)
                if (Math.Abs(rgba[a + channel] - rgba[b + channel]) > 1) return false;
            return true;
        }
    }

    // Photographic limits supplement the original artwork contract.
    // Sparse codec errors may occur in texture; small damaged regions still have
    // their own tile, outlier-count, edge, and genuine flat/gradient safeguards.
    private static bool AcceptPhotographic(byte[] original, byte[] candidate, int width, int height,
        ImageOptimizationPreset preset, bool[] texture, CancellationToken token)
    {
        var smallest = preset == ImageOptimizationPreset.Smallest;
        var columns = (width + TileSize - 1) / TileSize;
        var ordinaryError = smallest ? 32 : 18;
        var textureMaximumError = smallest ? 64 : 48;
        var textureMean = smallest ? 8.0 : 6.0;
        var protectedMean = smallest ? 5.0 : 2.5;
        var maximumTileOutliers = smallest ? 8 : 16;
        var maximumOutlierFraction = smallest ? 0.0001 : 0.0003;
        var edgeError = smallest ? 28 : 16;
        var maximumTextureEdgeError = smallest ? 64 : 48;
        var maximumEdgeFailureFraction = smallest ? 0.002 : 0.005;
        var maximumFlatJump = smallest ? 10 : 8;
        var maximumCurvature = smallest ? 10 : 8;
        var minimumProtectedContrast = smallest ? 0.65 : 0.80;
        long outliers = 0, edgeCount = 0, edgeFailures = 0;
        long[] bias = new long[3];
        double squared = 0;
        for (var tileY = 0; tileY < height; tileY += TileSize)
        {
            token.ThrowIfCancellationRequested();
            for (var tileX = 0; tileX < width; tileX += TileSize)
            {
                var textured = texture[tileY / TileSize * columns + tileX / TileSize];
                var tileError = 0;
                var samples = 0;
                var tileOutliers = 0;
                var tileEdges = 0;
                var tileEdgeFailures = 0;
                var coherentEdges = 0;
                long coherentBefore = 0, coherentAfter = 0;
                for (var y = tileY; y < Math.Min(tileY + TileSize, height); y++)
                for (var x = tileX; x < Math.Min(tileX + TileSize, width); x++)
                {
                    var offset = (y * width + x) * 4;
                    if (original[offset + 3] != candidate[offset + 3]) return false;
                    bool? flat = null;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var index = offset + channel;
                        var error = candidate[index] - original[index];
                        var absolute = Math.Abs(error);
                        if (absolute > (textured ? textureMaximumError : ordinaryError) || original[offset + 3] == 0 && error != 0) return false;
                        if (absolute > ordinaryError) { outliers++; tileOutliers++; }
                        tileError += absolute; samples++; squared += error * error; bias[channel] += error;
                        if (x > 0 && !Pair(index, index - 4, x - 1, y)) return false;
                        if (y > 0 && !Pair(index, index - width * 4, x, y - 1)) return false;
                        if (x > 3 && !Gradient(index, 4)) return false;
                        if (y > 3 && !Gradient(index, width * 4)) return false;
                    }

                    bool Pair(int a, int b, int neighborX, int neighborY)
                    {
                        var before = original[a] - original[b];
                        var after = candidate[a] - candidate[b];
                        var protectedEdge = !textured || !texture[neighborY / TileSize * columns + neighborX / TileSize];
                        if (protectedEdge && Math.Abs(before) >= 24 &&
                            (Math.Sign(before) != Math.Sign(after) || Math.Abs(after - before) > edgeError)) return false;
                        if (protectedEdge && Math.Abs(before) >= 8 && IsCoherentEdge(original, width, height, x, y, neighborX, neighborY))
                        {
                            coherentEdges++;
                            coherentBefore += Math.Abs(before);
                            coherentAfter += Math.Sign(before) == Math.Sign(after) ? Math.Abs(after) : 0;
                        }
                        if (!protectedEdge && Math.Abs(before) >= 24)
                        {
                            edgeCount++; tileEdges++;
                            if (Math.Abs(after - before) > maximumTextureEdgeError ||
                                Math.Abs(before) >= 48 && Math.Sign(before) != Math.Sign(after)) return false;
                            if (Math.Sign(before) != Math.Sign(after) || Math.Abs(after - before) > edgeError)
                            { edgeFailures++; tileEdgeFailures++; }
                        }
                        if (Math.Abs(before) <= 1 && Math.Abs(after) > maximumFlatJump)
                        {
                            flat ??= IsFlatNeighborhood(original, width, height, x, y);
                            if (flat.Value) return false;
                        }
                        return true;
                    }

                    bool Gradient(int index, int step)
                    {
                        var previous = original[index] - original[index - step];
                        var total = previous;
                        var increasing = previous >= 0;
                        var decreasing = previous <= 0;
                        if (Math.Abs(previous) > 8) return true;
                        for (var n = 1; n < 4; n++)
                        {
                            var delta = original[index - step * n] - original[index - step * (n + 1)];
                            if (Math.Abs(delta) > 8 || Math.Abs(delta - previous) > 2) return true;
                            increasing &= delta >= 0; decreasing &= delta <= 0;
                            total += delta; previous = delta;
                        }
                        if ((!increasing && !decreasing) || Math.Abs(total) < 4) return true;
                        var first = candidate[index] - candidate[index - step];
                        var second = candidate[index - step] - candidate[index - step * 2];
                        var originalFirst = original[index] - original[index - step];
                        var originalSecond = original[index - step] - original[index - step * 2];
                        if (Math.Abs(first - second) <= maximumCurvature &&
                            !(originalFirst >= 2 && originalSecond >= 2 && Math.Min(first, second) < -2) &&
                            !(originalFirst <= -2 && originalSecond <= -2 && Math.Max(first, second) > 2)) return true;
                        var center = (index - step * 2) / 4;
                        return !IsPlanarNeighborhood(original, width, height, center % width, center / width);
                    }
                }
                if (tileError > samples * (textured ? textureMean : protectedMean) || tileOutliers > maximumTileOutliers ||
                    tileEdgeFailures > Math.Max(2, tileEdges * 0.20) ||
                    coherentEdges >= 12 && coherentAfter < coherentBefore * minimumProtectedContrast) return false;
            }
        }
        var pixels = (long)width * height;
        var rmse = smallest ? 5.0 : 2.5;
        var maximumBias = smallest ? 1.2 : 0.6;
        return squared <= pixels * 3 * rmse * rmse && bias.All(value => Math.Abs(value) <= pixels * maximumBias) &&
            outliers <= pixels * 3 * maximumOutlierFraction && edgeFailures <= edgeCount * maximumEdgeFailureFraction;
    }

    private static bool IsFlatNeighborhood(byte[] rgba, int width, int height, int x, int y)
    {
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) return false;
        for (var channel = 0; channel < 3; channel++)
        {
            var minimum = 255; var maximum = 0;
            for (var row = y - 2; row <= y + 2; row++)
            for (var column = x - 2; column <= x + 2; column++)
            {
                var sample = rgba[(row * width + column) * 4 + channel];
                minimum = Math.Min(minimum, sample); maximum = Math.Max(maximum, sample);
                if (maximum - minimum > 4) return false;
            }
        }
        return true;
    }

    private static bool IsCoherentEdge(byte[] rgba, int width, int height, int x, int y, int neighborX, int neighborY)
    {
        var horizontal = x != neighborX;
        var normalX = horizontal ? 1 : 0;
        var normalY = horizontal ? 0 : 1;
        if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1 || neighborX < 1 || neighborY < 1) return false;
        var center = (y * width + x) * 4;
        var neighbor = (neighborY * width + neighborX) * 4;
        var perpendicular = horizontal ? width * 4 : 4;
        var normal = (normalY * width + normalX) * 4;
        var centerPlateau = true;
        var neighborPlateau = true;
        for (var channel = 0; channel < 3; channel++)
        {
            for (var direction = -1; direction <= 1; direction += 2)
                if (Math.Abs(rgba[center + channel + perpendicular * direction] - rgba[center + channel]) > 2 ||
                    Math.Abs(rgba[neighbor + channel + perpendicular * direction] - rgba[neighbor + channel]) > 2) return false;
            centerPlateau &= Math.Abs(rgba[center + channel + normal] - rgba[center + channel]) <= 2;
            neighborPlateau &= Math.Abs(rgba[neighbor + channel - normal] - rgba[neighbor + channel]) <= 2;
        }
        return centerPlateau || neighborPlateau;
    }

    private static bool IsPlanarNeighborhood(byte[] rgba, int width, int height, int x, int y)
    {
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) return false;
        for (var channel = 0; channel < 3; channel++)
        {
            var center = (y * width + x) * 4 + channel;
            var horizontal = (rgba[center + 8] - rgba[center - 8]) / 4.0;
            var vertical = (rgba[center + width * 8] - rgba[center - width * 8]) / 4.0;
            for (var row = -2; row <= 2; row++)
            for (var column = -2; column <= 2; column++)
                if (Math.Abs(rgba[((y + row) * width + x + column) * 4 + channel] -
                    (rgba[center] + horizontal * column + vertical * row)) > 2) return false;
        }
        return true;
    }

    internal static bool CanChooseBalanced(byte[] rgba, int width, int height, CancellationToken token)
    {
        if (width < 64 || height < 64) return false;
        var stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / 65536)));
        var count = 0;
        var textured = 0;
        var hard = 0;
        for (var y = 1; y < height; y += stride)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 1; x < width; x += stride)
            {
                var offset = (y * width + x) * 4;
                if (rgba[offset + 3] != 255) return false;
                var delta = 0;
                for (var channel = 0; channel < 3; channel++)
                    delta = Math.Max(delta, Math.Max(Math.Abs(rgba[offset + channel] - rgba[offset + channel - 4]),
                        Math.Abs(rgba[offset + channel] - rgba[offset + channel - width * 4])));
                if (delta is >= 3 and <= 24) textured++;
                if (delta >= 48) hard++;
                count++;
            }
        }
        // Avoid flat/gradient artwork and strong text edges. This is routing,
        // never acceptance: the complete candidate still passes every gate.
        return textured >= count * 0.50 && hard <= count * 0.01;
    }
}
