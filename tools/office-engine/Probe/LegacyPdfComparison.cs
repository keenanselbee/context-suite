using System.Text.Json;
using System.Text.RegularExpressions;

internal static class LegacyPdfComparison
{
    public static Comparison Compare(string baselineFolder, JsonElement baselineRender, string[] baselineText,
        string legacyFolder, JsonElement legacyRender, string[] legacyText)
    {
        var baselinePages = baselineRender.GetProperty("pages").EnumerateArray().ToArray();
        var legacyPages = legacyRender.GetProperty("pages").EnumerateArray().ToArray();
        if (baselinePages.Length != legacyPages.Length || baselinePages.Length is < 1 or > 16)
            throw new InvalidDataException("Legacy comparison needs matching bounded page counts.");
        var pages = new List<PageComparison>();
        for (var index = 0; index < baselinePages.Length; index++)
        {
            var before = baselinePages[index]; var after = legacyPages[index];
            var width = before.GetProperty("width").GetInt32(); var height = before.GetProperty("height").GetInt32();
            var oldWidth = after.GetProperty("width").GetInt32(); var oldHeight = after.GetProperty("height").GetInt32();
            var stride = before.GetProperty("stride").GetInt32(); var oldStride = after.GetProperty("stride").GetInt32();
            var pixels = Read(baselineFolder, index, width, height, stride);
            var oldPixels = Read(legacyFolder, index, oldWidth, oldHeight, oldStride);
            long? different = null, totalDifference = null; int? maximumDifference = null;
            if (width == oldWidth && height == oldHeight)
            {
                different = 0; totalDifference = 0; maximumDifference = 0;
                for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    var changed = false;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var delta = Math.Abs(pixels[row * stride + column * 4 + channel] - oldPixels[row * oldStride + column * 4 + channel]);
                        changed |= delta != 0; totalDifference += delta; maximumDifference = Math.Max(maximumDifference.Value, delta);
                    }
                    if (changed) different++;
                }
            }
            pages.Add(new(index + 1, width, height, oldWidth, oldHeight,
                before.GetProperty("widthPoints").GetDouble(), before.GetProperty("heightPoints").GetDouble(),
                after.GetProperty("widthPoints").GetDouble(), after.GetProperty("heightPoints").GetDouble(),
                different == 0, different, totalDifference, maximumDifference));
        }
        // Whitespace normalization is only a comparison observation, not the authored text oracle.
        var textEqual = baselineText.Select(Normalize).SequenceEqual(legacyText.Select(Normalize), StringComparer.Ordinal);
        return new(textEqual, pages.ToArray());
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim();
    private static byte[] Read(string folder, int index, int width, int height, int stride)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096 || stride < width * 4 || stride > width * 4 + 16 || (long)stride * height > 32 * 1024 * 1024)
            throw new InvalidDataException("Comparison render dimensions exceed limits.");
        var file = Path.Combine(folder, $"page-{index + 1}.bgra");
        using var input = File.OpenRead(file);
        if (input.Length != (long)stride * height) throw new InvalidDataException("Comparison render length mismatch.");
        var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); return bytes;
    }
    internal sealed record Comparison(bool TextEqual, PageComparison[] Pages);
    internal sealed record PageComparison(int Page, int ModernWidth, int ModernHeight, int LegacyWidth, int LegacyHeight,
        double ModernWidthPoints, double ModernHeightPoints, double LegacyWidthPoints, double LegacyHeightPoints,
        bool ExactPixels, long? DifferentPixels, long? AbsoluteChannelDifference, int? MaximumChannelDifference);
}
