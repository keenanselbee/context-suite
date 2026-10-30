using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class ImageOptimizationPresetContracts
{
    public static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "image-optimization-presets");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        foreach (var format in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
        {
            using var image = new MagickImage(PngFixture.Create(257, 193, 8, 2, (x, y) =>
            {
                var level = 50 + x / 8 + y / 8 + (x * 13 + y * 7) % 3;
                return [(ushort)(level * 257), (ushort)(level * 257), (ushort)(level * 257)];
            }));
            image.ColorType = ColorType.TrueColor;
            image.Settings.ColorType = ColorType.TrueColor;
            image.Quality = 100;
            image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1");
            image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", false);
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
            image.Settings.SetDefine(MagickFormat.WebP, "method", "0");
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.ImageDescription, "Presets keep original metadata bytes");
            image.SetProfile(exif);
            image.SetProfile(ColorProfiles.SRGB);
            image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description/></rdf:RDF></x:xmpmeta>")));
            using var encoded = new MemoryStream();
            image.Write(encoded, format == ImageFormat.Jpeg ? MagickFormat.Jpeg : MagickFormat.WebP);
            var original = encoded.ToArray();
            foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
            {
                var result = Exercise(format + "-" + preset, original, format, preset);
                check(result.OptimizationMethod == preset + " (lossy)" && result.OptimizationAttempts == 1,
                    format + " " + preset + ": one fixed lossy primary recipe succeeds: " + result.OptimizationReason);
            }
            var automatic = Exercise(format + "-Auto", original, format, ImageOptimizationPreset.Auto, requireSmaller: false);
            check(automatic.OptimizationMethod != "Smallest (lossy)" && automatic.OptimizationAttempts is >= 1 and <= 2,
                format + ": Auto never selects Smallest and stays within two attempts");
            if (format == ImageFormat.Jpeg)
                check(automatic.OptimizationMethod == "Lossless" && automatic.OptimizationAttempts == 1,
                    "JPEG Auto does not add another lossy generation");
            var unknown = original.ToArray();
            if (format == ImageFormat.Jpeg) unknown[3] = 227;
            else
            {
                unknown = original.Concat(new byte[] { 84, 69, 83, 84, 0, 0, 0, 0 }).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), (uint)unknown.Length - 8);
            }
            foreach (var preset in Enum.GetValues<ImageOptimizationPreset>())
                Refuse(() => ImageAdapter.OptimizeImageBytes(unknown, preset, default), format + " " + preset + ": unknown metadata refused");
        }

        using (var coloredGrain = new MagickImage(PngFixture.Create(257, 193, 8, 2, (x, y) =>
        {
            var level = 50 + x / 8 + y / 8 + (x * 13 + y * 7) % 5;
            return [(ushort)((level + 30) * 257), (ushort)(level * 257), (ushort)((level + 10) * 257)];
        })))
        {
            coloredGrain.ColorType = ColorType.TrueColor;
            coloredGrain.Settings.ColorType = ColorType.TrueColor;
            coloredGrain.Quality = 100;
            coloredGrain.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1");
            coloredGrain.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", false);
            using var encoded = new MemoryStream();
            coloredGrain.Write(encoded, MagickFormat.Jpeg);
            var bytes = encoded.ToArray();
            var result = ImageAdapter.OptimizeImageBytes(bytes, ImageOptimizationPreset.Balanced, default);
            using var before = new MagickImage(bytes);
            using var reopened = new MagickImage(result.Bytes);
            check(result.Attempts == 2 && result.Method == "Lossless" && result.Reason.Contains("quality limits rejected"),
                "Difficult colored grain rejects Balanced quality and uses exactly its Lossless fallback");
            check(before.GetPixels().ToArray()!.SequenceEqual(reopened.GetPixels().ToArray()!) && result.Bytes.Length <= bytes.Length,
                "Quality fallback starts from original decoded samples and retains the quality floor");
        }

        using (var alpha = new MagickImage(PngFixture.Create(73, 57, 8, 6, (x, y) =>
            [(ushort)(x * 503), (ushort)(y * 607), (ushort)50000, (ushort)(x % 3 == 0 ? 0 : x % 3 == 1 ? 32768 : 65535)])))
        {
            alpha.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            alpha.Settings.SetDefine(MagickFormat.WebP, "exact", true);
            alpha.Settings.SetDefine(MagickFormat.WebP, "method", "0");
            using var encoded = new MemoryStream();
            alpha.Write(encoded, MagickFormat.WebP);
            var bytes = encoded.ToArray();
            var balanced = ImageAdapter.OptimizeImageBytes(bytes, ImageOptimizationPreset.Balanced, default);
            var smallest = ImageAdapter.OptimizeImageBytes(bytes, ImageOptimizationPreset.Smallest, default);
            using var before = new MagickImage(bytes);
            foreach (var result in new[] { balanced, smallest })
            {
                using var reopened = new MagickImage(result.Bytes);
                check(before.GetPixels().ToArray()!.SequenceEqual(reopened.GetPixels().ToArray()!) && result.Attempts == 2,
                    "Transparent WebP preserves every alpha and hidden RGB sample within two attempts");
            }
            check(balanced.Method == "Lossless" && (balanced.Reason.Contains("fallback") || balanced.Bytes.SequenceEqual(bytes)),
                "Transparent Balanced uses its predetermined exact fallback");
            check(smallest.Bytes.SequenceEqual(bytes) && smallest.Reason.Contains("Selected Smallest"),
                "Transparent Smallest stops after inapplicable Balanced without recursive Lossless");
        }

        using (var lossy = new MagickImage(PngFixture.Create(131, 97, 8, 2, (x, y) =>
            [(ushort)((80 + x / 8) * 257), (ushort)((60 + y / 8) * 257), (ushort)25000])))
        {
            lossy.Quality = 90;
            using var encoded = new MemoryStream();
            lossy.Write(encoded, MagickFormat.WebP);
            var bytes = encoded.ToArray();
            var result = ImageAdapter.OptimizeImageBytes(bytes, ImageOptimizationPreset.Auto, default);
            using var before = new MagickImage(bytes);
            using var after = new MagickImage(result.Bytes);
            check(result.Method == "Lossless" && result.Attempts == 1 && before.GetPixels().ToArray()!.SequenceEqual(after.GetPixels().ToArray()!),
                "Lossy WebP Auto retains exact decoded samples without another lossy generation");
            check(result.Bytes.Length <= bytes.Length, "Lossy WebP exact VP8L candidate is retained only when smaller");
        }
        QualityGates(check);
        PhotographicQualityGates(check);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellationObserved = false;
        try { ImageAdapter.OptimizeImageBytes([], ImageOptimizationPreset.Balanced, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        check(cancellationObserved, "Preset byte path observes cancellation before decoding or fallback");

        ImageWorkResult Exercise(string name, byte[] original, ImageFormat format, ImageOptimizationPreset preset, bool requireSmaller = true)
        {
            var path = Path.Combine(root, name + (format == ImageFormat.Jpeg ? ".jpg" : ".webp"));
            File.WriteAllBytes(path, original);
            var facts = adapter.ProbeLosslessOptimization(new(Guid.NewGuid(), path), default);
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var policy = LosslessImageOptimizationPlan.PolicyFor(format, preset);
            var result = adapter.OptimizeLosslessImage(new(facts, temporary, policy), default);
            var output = File.ReadAllBytes(temporary);
            check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(output)) &&
                result.EngineIdentity!.EndsWith(policy, StringComparison.Ordinal), name + ": policy-bound independently checked output hash");
            check((requireSmaller ? output.Length < original.Length : output.Length <= original.Length) && File.ReadAllBytes(path).SequenceEqual(original),
                name + ": smaller-only output and unchanged source");
            using var before = new MagickImage(original);
            using var after = new MagickImage(output);
            check(before.Width == after.Width && before.Height == after.Height && before.Orientation == after.Orientation &&
                before.ProfileNames.All(profile => before.GetProfile(profile)!.ToByteArray().SequenceEqual(after.GetProfile(profile)!.ToByteArray())),
                name + ": independent decoder retains dimensions, orientation, and profiles");
            check(format == ImageFormat.Jpeg
                ? new JpegLosslessEnvelope(original).MetadataBytes().SequenceEqual(new JpegLosslessEnvelope(output).MetadataBytes())
                : new WebPLosslessEnvelope(original).MetadataBytes().SequenceEqual(new WebPLosslessEnvelope(output).MetadataBytes()),
                name + ": exact original raw metadata bytes retained");
            if (result.OptimizationMethod != "Lossless")
                check(ImageOptimizationQuality.Accept(before.GetPixels().ToByteArray(PixelMapping.RGBA)!, after.GetPixels().ToByteArray(PixelMapping.RGBA)!,
                    (int)before.Width, (int)before.Height, preset, default), name + ": decoded output passes the selected numeric quality floor");
            return result;
        }

        void Refuse(Action action, string label)
        {
            var refused = false;
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException) { refused = true; }
            check(refused, label);
        }
    }

    private static void QualityGates(Action<bool, string> check)
    {
        const int width = 128, height = 128;
        var source = new byte[width * height * 4];
        for (var index = 0; index < source.Length; index += 4)
        { source[index] = source[index + 1] = source[index + 2] = 100; source[index + 3] = 255; }
        foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
        {
            check(ImageOptimizationQuality.Accept(source, source.ToArray(), width, height, preset, default), preset + ": exact sample control passes quality gates");
            var changed = source.ToArray();
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            for (var channel = 0; channel < 3; channel++) changed[(y * width + x) * 4 + channel] += 12;
            check(!ImageOptimizationQuality.Accept(source, changed, width, height, preset, default), preset + ": small locally damaged region rejected despite low global error");
            changed = source.ToArray();
            changed[4 * (64 * width + 64)] += 12;
            check(!ImageOptimizationQuality.Accept(source, changed, width, height, preset, default), preset + ": new sharp grain on flat region rejected");
            var edge = source.ToArray();
            for (var x = 0; x < width; x++) edge[x * 4] = (byte)(x < 64 ? 70 + x % 4 * 10 : 200 + x % 4 * 10);
            changed = edge.ToArray();
            var erosion = (byte)(preset == ImageOptimizationPreset.Balanced ? 10 : 15);
            changed[63 * 4] += erosion;
            changed[64 * 4] -= erosion;
            check(!ImageOptimizationQuality.Accept(edge, changed, width, height, preset, default), preset + ": local contrast loss at a strong edge rejected");
            var gradient = source.ToArray();
            for (var x = 0; x < width; x++) gradient[x * 4] = (byte)(x + 50);
            changed = gradient.ToArray();
            changed[64 * 4] += 12;
            check(!ImageOptimizationQuality.Accept(gradient, changed, width, height, preset, default), preset + ": smooth gradient curvature damage rejected");
            changed = source.ToArray();
            changed[3] = 0;
            check(!ImageOptimizationQuality.Accept(source, changed, width, height, preset, default), preset + ": alpha damage independently rejected");
            var hidden = source.ToArray(); hidden[3] = 0;
            changed = hidden.ToArray(); changed[0]++;
            check(!ImageOptimizationQuality.Accept(hidden, changed, width, height, preset, default), preset + ": hidden RGB damage independently rejected");
        }
    }

    private static void PhotographicQualityGates(Action<bool, string> check)
    {
        const int width = 128, height = 128;
        var random = new Random(104729);
        var texture = new byte[width * height * 4];
        for (var offset = 0; offset < texture.Length; offset += 4)
        {
            for (var channel = 0; channel < 3; channel++) texture[offset + channel] = (byte)random.Next(40, 190);
            texture[offset + 3] = 255;
        }
        check(ImageOptimizationQuality.ClassifyTextureTiles(texture, width, height, default).All(value => value),
            "Photographic quality controls actually exercise the textured-image policy");
        foreach (var preset in new[] { ImageOptimizationPreset.Balanced, ImageOptimizationPreset.Smallest })
        {
            var changed = texture.ToArray();
            for (var offset = 0; offset < changed.Length; offset += 4)
            for (var channel = 0; channel < 3; channel++) changed[offset + channel] = (byte)(changed[offset + channel] + (offset / 4 % 2 == 0 ? 1 : -1));
            check(ImageOptimizationQuality.Accept(texture, changed, width, height, preset, default),
                preset + ": bounded one-level texture error control passes");
            changed = texture.ToArray();
            for (var y = 32; y < 40; y++)
            for (var x = 32; x < 40; x++)
            for (var channel = 0; channel < 3; channel++) changed[(y * width + x) * 4 + channel] += 12;
            check(!ImageOptimizationQuality.Accept(texture, changed, width, height, preset, default),
                preset + ": localized destruction rejected even with 100% texture classification");
            var mixed = texture.ToArray();
            for (var y = 24; y < 48; y++)
            for (var x = 24; x < 48; x++)
            for (var channel = 0; channel < 3; channel++) mixed[(y * width + x) * 4 + channel] = 220;
            changed = mixed.ToArray(); changed[(32 * width + 32) * 4] += 12;
            check(!ImageOptimizationQuality.Accept(mixed, changed, width, height, preset, default),
                preset + ": isolated grain on a flat patch inside a photo is rejected");
            for (var y = 28; y < 44; y++)
            for (var channel = 0; channel < 3; channel++) mixed[(y * width + 32) * 4 + channel] = 190;
            changed = mixed.ToArray();
            for (var y = 28; y < 44; y++)
            for (var channel = 0; channel < 3; channel++) changed[(y * width + 32) * 4 + channel] = 205;
            check(!ImageOptimizationQuality.Accept(mixed, changed, width, height, preset, default),
                preset + ": faint text contrast remains protected inside a photo");
            changed = texture.ToArray(); changed[3]--;
            check(!ImageOptimizationQuality.Accept(texture, changed, width, height, preset, default),
                preset + ": photographic policy never permits alpha changes");
        }
    }
}
