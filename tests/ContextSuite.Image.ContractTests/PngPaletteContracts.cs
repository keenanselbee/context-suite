using System.Buffers.Binary;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class PngPaletteContracts
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        const int width = 128, height = 64;
        var original = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            original[i] = (byte)(x / 4 + 8); original[i + 1] = (byte)(y / 4 + 8); original[i + 2] = 16;
            original[i + 3] = x < 2 ? (byte)0 : y < 2 ? (byte)128 : (byte)255;
            if (x < 2) { original[i] = 20; original[i + 1] = 30; }
        }
        check(PngPalette.CanUse(original, width, height, default), "Palette: exact-alpha fixture admitted");
        var first = await PngPalette.GenerateAsync(original, width, height, default);
        var second = await PngPalette.GenerateAsync(original, width, height, default);
        check(first.SequenceEqual(second), "Palette: fresh processes return identical pixels despite randomized hash seeds");
        check(Enumerable.Range(0, width * height).All(p => original[p * 4 + 3] == first[p * 4 + 3] &&
            (original[p * 4 + 3] != 0 || original.AsSpan(p * 4, 4).SequenceEqual(first.AsSpan(p * 4, 4)))),
            "Palette: exact alpha and hidden RGB");
        var encoded = new PngLosslessContainer(QuantizationFixtures.Encode(width, height, original)).EncodePalette(first, default);
        var compressed = new PngLosslessContainer(encoded).RestoreAndValidate(await PngOptimizer.EncodeAsync(encoded, default), default);
        using (var image = new MagickImage(compressed))
            check(image.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(first), "Palette: indexed PNG and trimmed transparency round-trip exact");

        var manyHidden = new byte[129 * 4];
        for (var i = 0; i < 128; i++) manyHidden[i * 4] = (byte)i;
        manyHidden[^1] = 255;
        check(!PngPalette.CanUse(manyHidden, 129, 1, default) && !PngPalette.CanUse(new byte[4], 1, 1, default) &&
            !PngPalette.CanUse([], 9000001, 1, default), "Palette: bounded dimensions and impossible alpha allocation reject in preflight");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await PngPalette.GenerateAsync(original, width, height, cancelled.Token); throw new Exception("Cancellation lost"); }
        catch (OperationCanceledException) { check(true, "Palette: pre-cancellation does not launch work"); }

        foreach (var input in new[] { Array.Empty<byte>(), new byte[8], Packet(1, 1, []), Packet(1, 1, [1, 2, 3, 255, 9]) })
        {
            try
            {
                await PngOptimizer.RunAsync(input, "ContextSuite.Palette.exe", PngPalette.ExecutableSha256, 4, [], default);
                throw new Exception("Invalid native protocol accepted");
            }
            catch (ImageFailureException error) { check(error.Failure == ImageFailure.EngineFailure, "Palette: malformed/truncated/excess native input rejected"); }
        }

        var ramp = new byte[256 * 64 * 4];
        for (var i = 0; i < ramp.Length; i += 4)
        { ramp[i] = ramp[i + 1] = ramp[i + 2] = (byte)(16 + i / 4 % 256 / 8); ramp[i + 3] = 255; }
        var plateau = ramp.ToArray(); var dither = ramp.ToArray();
        for (var i = 0; i < ramp.Length; i += 4)
        for (var c = 0; c < 3; c++)
        { plateau[i + c] = (byte)(ramp[i + c] / 8 * 8); dither[i + c] = (byte)(ramp[i + c] + (i / 4 % 2 == 0 ? -1 : 1)); }
        check(!PngGradientQuality.Measure(ramp, ramp, 256, 64, default).Rejected &&
            PngGradientQuality.Measure(ramp, plateau, 256, 64, default).Rejected &&
            !PngGradientQuality.Measure(ramp, dither, 256, 64, default).Rejected,
            "Production plateau screen: identity and mild dither pass, flattened gradient rejects");
        foreach (var fixture in QuantizationFixtures.Create(true))
        {
            var reduced = QuantizationCandidate.ReducePrecision(fixture.Pixels, 6);
            var oracle = GradientRejection.Measure(fixture.Pixels, reduced, fixture.Width, fixture.Height);
            var actual = PngGradientQuality.Measure(fixture.Pixels, reduced, fixture.Width, fixture.Height, default);
            check(actual.EligibleRuns == oracle.EligibleRuns && actual.FlattenedRuns == oracle.FlattenedRuns && actual.Rejected == oracle.Rejected,
                "Production gradient screen agrees with research oracle: " + fixture.Name);
        }
    }

    private static byte[] Packet(int width, int height, byte[] pixels)
    {
        var result = new byte[8 + pixels.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, width); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), height);
        pixels.CopyTo(result, 8); return result;
    }
}
