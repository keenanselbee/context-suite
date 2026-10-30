using System.Globalization;
using ImageMagick;

// Research-only settings. Every candidate starts from original samples, never a previous output.
internal sealed record QuantizationCandidate(string Name, uint Colors = 0, DitherMethod Dither = DitherMethod.No,
    int DiffusionPercent = 100, int RgbBits = 0, bool PreserveAlpha = false, bool Kmeans = false,
    ColorSpace PaletteSpace = ColorSpace.sRGB, bool ProtectGradients = false)
{
    internal static IReadOnlyList<QuantizationCandidate> All()
    {
        var candidates = new List<QuantizationCandidate>();
        foreach (uint colors in new uint[] { 256, 128, 64 })
        foreach (var dither in new[] { DitherMethod.No, DitherMethod.FloydSteinberg, DitherMethod.Riemersma })
            candidates.Add(new($"{colors}-{dither}", colors, dither));
        candidates.Add(new("rgb7-exact-alpha", RgbBits: 7, PreserveAlpha: true));
        candidates.Add(new("rgb6-exact-alpha", RgbBits: 6, PreserveAlpha: true));
        foreach (int amount in new[] { 25, 50, 75 })
            candidates.Add(new($"256-FS{amount}", 256, DitherMethod.FloydSteinberg, amount));
        candidates.Add(new("rgb256-exact-alpha", 256, PreserveAlpha: true));
        candidates.Add(new("rgb128-exact-alpha", 128, PreserveAlpha: true));
        candidates.Add(new("rgb256-FS50-exact-alpha", 256, DitherMethod.FloydSteinberg, 50, PreserveAlpha: true));
        candidates.Add(new("kmeans128-8-exact-alpha", 128, PreserveAlpha: true, Kmeans: true));
        return candidates;
    }

    internal (byte[] Encoded, byte[] ExpectedPixels) Encode(QuantizationFixtures.Fixture fixture)
    {
        byte[] expected;
        if (RgbBits != 0)
        {
            expected = ReducePrecision(fixture.Pixels, RgbBits);
            return EncodeRgba(fixture, expected);
        }
        using var image = new MagickImage(fixture.Png);
        if (PreserveAlpha || fixture.Pixels.Where((_, i) => i % 4 == 3).All(a => a == 255)) image.Alpha(AlphaOption.Off);
        if (Kmeans)
        {
            // Fixed 8x4x4 sRGB cube, 128 seeds. Full-resolution training, eight iterations maximum.
            // No auto/random seed selection; repeat-run checks still verify native determinism.
            var seeds = new List<string>();
            for (int r = 0; r < 8; r++)
            for (int g = 0; g < 4; g++)
            for (int b = 0; b < 4; b++) seeds.Add($"#{(int)Math.Round(r * 255.0 / 7):X2}{g * 85:X2}{b * 85:X2}");
            image.Kmeans(new KmeansSettings { NumberColors = Colors, MaxIterations = 8, Tolerance = .01, SeedColors = string.Join(';', seeds) });
        }
        else
        {
            if (DiffusionPercent != 100)
                image.SetArtifact("dither:diffusion-amount", DiffusionPercent.ToString(CultureInfo.InvariantCulture) + "%");
            image.Quantize(new QuantizeSettings { Colors = Colors, ColorSpace = PaletteSpace,
                DitherMethod = Dither, MeasureErrors = true, TreeDepth = 0 });
        }
        // Quantize can leave samples in its working space. Transform before treating bytes as sRGB.
        image.ColorSpace = ColorSpace.sRGB;
        expected = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        if (ProtectGradients) GradientProtection.Apply(fixture.Pixels, expected, fixture.Width, fixture.Height);
        if (PreserveAlpha)
        {
            for (int i = 0; i < expected.Length; i += 4)
            {
                expected[i + 3] = fixture.Pixels[i + 3];
                // Exact hidden RGB is inexpensive in policy terms, but may cost compression.
                if (expected[i + 3] == 0) fixture.Pixels.AsSpan(i, 3).CopyTo(expected.AsSpan(i, 3));
            }
            return EncodeRgba(fixture, expected);
        }
        image.Depth = 8;
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time");
        return (image.ToByteArray(MagickFormat.Png), expected);
    }

    internal static byte[] ReducePrecision(byte[] original, int bits)
    {
        if (bits is not (6 or 7)) throw new ArgumentOutOfRangeException(nameof(bits));
        var result = original.ToArray(); int levels = (1 << bits) - 1;
        for (int i = 0; i < result.Length; i += 4)
        {
            if (original[i + 3] == 0) continue;
            for (int c = 0; c < 3; c++)
            {
                int index = (original[i + c] * levels + 127) / 255;
                result[i + c] = (byte)((index * 255 + levels / 2) / levels);
            }
        }
        return result;
    }

    private static (byte[] Encoded, byte[] ExpectedPixels) EncodeRgba(QuantizationFixtures.Fixture fixture, byte[] pixels)
    {
        // Reconstitute full RGBA instead of forcing alpha into a <=256-entry combined palette.
        using var image = new MagickImage(QuantizationFixtures.Encode(fixture.Width, fixture.Height, pixels));
        image.Depth = 8;
        bool opaque = pixels.Where((_, i) => i % 4 == 3).All(a => a == 255);
        // Prevent the writer from choosing a new palette after RGB/alpha reconstruction.
        image.Settings.SetDefine(MagickFormat.Png, "color-type", opaque ? "2" : "6");
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time");
        return (image.ToByteArray(MagickFormat.Png), pixels);
    }
}
