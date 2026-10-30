using System.Buffers.Binary;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class TgaOptimizationContracts
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        foreach (var stride in new[] { 3, 4 })
        foreach (var origin in new byte[] { 0, 16, 32, 48 })
        foreach (var footer in new[] { false, true })
        {
            var original = Fixture(stride, origin, footer);
            var candidate = TgaOptimization.Optimize(original, default);
            check(candidate.Length < original.Length && candidate[2] == 10,
                $"TGA {stride}/{origin}/{footer}: exact RLE produces a smaller raster");
            check(candidate.AsSpan(3, 20).SequenceEqual(original.AsSpan(3, 20)) &&
                (!footer || candidate.AsSpan(candidate.Length - 26).SequenceEqual(original.AsSpan(original.Length - 26))),
                "TGA: header, ID, origin, and footer bytes preserved");
            using var before = new MagickImage(original, new MagickReadSettings { Format = MagickFormat.Tga });
            using var after = new MagickImage(candidate, new MagickReadSettings { Format = MagickFormat.Tga });
            check(before.GetPixels().ToArray()!.SequenceEqual(after.GetPixels().ToArray()!),
                "TGA: native decoder independently verifies exact samples including hidden RGB");
            check(TgaOptimization.Optimize(candidate, default).SequenceEqual(candidate), "TGA: optimized RLE repeats without changes");
            var path = Path.Combine(root, $"original-{stride}-{origin}-{footer}.tga");
            File.WriteAllBytes(path, original);
            var facts = adapter.ProbeLosslessOptimization(new(Guid.NewGuid(), path), default);
            foreach (var preset in new[] { ImageOptimizationPreset.Auto, ImageOptimizationPreset.Lossless })
            {
                var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
                File.WriteAllBytes(temporary, []);
                var result = adapter.OptimizeLosslessImage(new(facts, temporary, LosslessImageOptimizationPlan.PolicyFor(ImageFormat.Tga, preset)), default);
                check(result.OptimizationMethod == "Lossless" && result.OptimizationAttempts == 1 &&
                    File.ReadAllBytes(temporary).SequenceEqual(candidate) && File.ReadAllBytes(path).SequenceEqual(original),
                    "TGA: reserved file path follows exact Auto/Lossless recipe");
                File.Delete(temporary);
            }
        }
        var protectedFooter = Fixture(4, 32, true);
        protectedFooter[^26] = 1;
        var refused = false;
        try { TgaOptimization.Optimize(protectedFooter, default); }
        catch (ImageFailureException) { refused = true; }
        check(refused, "TGA: extension/developer offsets remain unsupported");
        var malformed = Fixture(4, 32, false)[..^1];
        refused = false;
        try { TgaOptimization.Optimize(malformed, default); }
        catch (InvalidDataException) { refused = true; }
        check(refused, "TGA: incomplete samples refused");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        refused = false;
        try { TgaOptimization.Optimize(Fixture(3, 0, false), stop.Token); }
        catch (OperationCanceledException) { refused = true; }
        check(refused, "TGA: cancellation before encoding");
    }

    private static byte[] Fixture(int stride, byte origin, bool footer)
    {
        const int width = 259, height = 3;
        var bytes = new byte[23 + width * height * stride + (footer ? 26 : 0)];
        bytes[0] = 5; bytes[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), height);
        bytes[16] = (byte)(stride * 8); bytes[17] = (byte)(origin | (stride == 4 ? 8 : 0));
        "IDtag"u8.CopyTo(bytes.AsSpan(18));
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = 23 + pixel * stride;
            bytes[offset] = (byte)(pixel / 128 * 13);
            bytes[offset + 1] = 47; bytes[offset + 2] = 213;
            if (stride == 4) bytes[offset + 3] = (byte)(pixel / width * 127);
        }
        if (footer) Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0").CopyTo(bytes, bytes.Length - 18);
        return bytes;
    }
}
