using System.Security.Cryptography;
using System.Runtime.InteropServices;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class AdapterContracts
{
    public static void Run(string root, IReadOnlyDictionary<MagickFormat, string> inputs, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(root, "adapter-scratch"));
        string? lastOutput = null;
        foreach (var source in inputs.Values)
        {
            var facts = adapter.Probe(new(Guid.NewGuid(), source), CancellationToken.None);
            check(facts.Width == 3 && facts.Height == 2 && facts.UnsupportedReason is null, "adapter probe: " + facts.Format);
            foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga }.Where(f => f != facts.Format))
            {
                var result = Execute(facts, new(target, Metadata: target is ImageFormat.Bmp or ImageFormat.Tga ? ImageMetadataMode.RemoveDescriptive : ImageMetadataMode.Preserve));
                check(result.Width == 3 && result.Height == 2 && result.OutputBytes > 0 && result.Validation.MatchesPlan,
                    "adapter validated conversion: " + facts.Format + " to " + target);
            }
        }
        var png = inputs[MagickFormat.Png];
        var transparent = Path.Combine(root, "alpha.png");
        using (var image = new MagickImage(png))
        {
            image.HasAlpha = true;
            image.GetPixels().SetPixel(0, 0, new ushort[] { 65535, 0, 0, 0 });
            image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            image.Write(transparent, MagickFormat.Png);
        }
        var alpha = adapter.Probe(new(Guid.NewGuid(), transparent), CancellationToken.None);
        check(alpha.HasTransparency, "adapter detects actual transparent pixels");
        Reject(() => Execute(alpha, new(ImageFormat.Jpeg)), "missing matte");
        var matte = Execute(alpha, new(ImageFormat.Jpeg, MatteRgb: 0xffffff, Metadata: ImageMetadataMode.RemoveDescriptive));
        check(matte.Validation.MatchesPlan, "adapter composites explicit matte to JPEG");
        var webp = Execute(alpha, new(ImageFormat.WebP, WebPLossless: true, Metadata: ImageMetadataMode.RemoveDescriptive));
        check(webp.Validation.MatchesPlan, "adapter preserves alpha in lossless WebP");
        using (var originalAlpha = new MagickImage(transparent))
        using (var outputAlpha = new MagickImage(lastOutput!))
            check(originalAlpha.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(outputAlpha.GetPixels().ToByteArray(PixelMapping.RGBA)!),
                "adapter lossless WebP retains exact RGBA including transparent pixel color");
        var preview = adapter.Preview(new(alpha, new(ImageFormat.Jpeg, MatteRgb: 0xffffff, Metadata: ImageMetadataMode.RemoveDescriptive)), CancellationToken.None);
        check(preview.Width <= 512 && preview.Height <= 512 && preview.BgraPixels.Length == preview.Width * preview.Height * 4,
            "adapter returns bounded decoded display pixels, not a full-resolution converted file");

        foreach (var orientation in Enumerable.Range(1, 8))
        {
            var oriented = Path.Combine(root, "oriented-" + orientation + ".jpg");
            using (var image = new MagickImage(png))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.Orientation, (ushort)orientation);
                exif.SetValue(ExifTag.Artist, "Context Suite test fixture");
                image.SetProfile(exif);
                image.Orientation = (OrientationType)orientation;
                image.Write(oriented, MagickFormat.Jpeg);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), oriented), CancellationToken.None);
            var output = Execute(facts, new(ImageFormat.Png));
            check(output.Width == (orientation >= 5 ? 2 : 3) && output.Height == (orientation >= 5 ? 3 : 2),
                "adapter applies EXIF orientation and preserves normalized profile: " + orientation);
            Execute(facts, new(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic));
            using var before = new MagickImage(oriented);
            using var after = new MagickImage(lastOutput!);
            check(after.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value == "Context Suite test fixture" &&
                after.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value == 1,
                "automatic metadata: retains author and normalizes orientation " + orientation);
            var originalPixels = before.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            var actualPixels = after.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            var matches = true;
            for (var y = 0; y < after.Height; y++)
            for (var x = 0; x < after.Width; x++)
            {
                var (sx, sy) = orientation switch
                {
                    1 => (x, y), 2 => (2 - x, y), 3 => (2 - x, 1 - y), 4 => (x, 1 - y),
                    5 => (y, x), 6 => (y, 1 - x), 7 => (2 - y, 1 - x), 8 => (2 - y, x),
                    _ => throw new InvalidOperationException()
                };
                matches &= actualPixels.AsSpan((y * (int)after.Width + x) * 4, 4).SequenceEqual(originalPixels.AsSpan((sy * 3 + sx) * 4, 4));
            }
            check(matches, "automatic metadata: orientation has exact independently mapped output pixels: " + orientation);
        }
        var profiled = Path.Combine(root, "profiled.jpg");
        using (var image = new MagickImage(png))
        {
            image.SetProfile(ColorProfiles.SRGB);
            image.Write(profiled, MagickFormat.Jpeg);
        }
        var profileFacts = adapter.Probe(new(Guid.NewGuid(), profiled), CancellationToken.None);
        check(Execute(profileFacts, new(ImageFormat.WebP)).Validation.MatchesPlan, "adapter preserves exact ICC profile");
        check(Execute(profileFacts, new(ImageFormat.WebP, Metadata: ImageMetadataMode.Automatic)).Validation.MatchesPlan,
            "automatic metadata: retains exact ICC profile in WebP");
        var extrasPath = Path.Combine(root, "automatic-extras.png");
        File.WriteAllBytes(extrasPath, PngFixture.Create(4, 3, 8, 2, (x, y) => [(byte)(x * 40), (byte)(y * 60), 90],
            ("fdEC", new byte[] { 1, 2, 3 }), ("tEXt", "Comment\0Fixture extra"u8.ToArray())));
        var extras = adapter.Probe(new(Guid.NewGuid(), extrasPath), CancellationToken.None);
        check(extras.HasOtherMetadata && extras.UnsupportedReason is null, "automatic metadata: private ancillary PNG chunk is not a decode failure");
        foreach (var target in new[] { ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga, ImageFormat.Jpeg })
        {
            var converted = Execute(extras, new(target, WebPLossless: target == ImageFormat.WebP, Metadata: ImageMetadataMode.Automatic));
            check(converted.Validation.MatchesPlan, "automatic metadata: validated extra-metadata conversion to " + target);
            using var outputImage = new MagickImage(lastOutput!, new MagickReadSettings { Format = target switch
            { ImageFormat.Tga => MagickFormat.Tga, ImageFormat.Bmp => MagickFormat.Bmp, ImageFormat.WebP => MagickFormat.WebP, _ => MagickFormat.Jpeg } });
            if (target != ImageFormat.Jpeg)
            {
                using var inputImage = new MagickImage(extrasPath);
                check(inputImage.GetPixels().ToByteArray(PixelMapping.RGB)!.SequenceEqual(outputImage.GetPixels().ToByteArray(PixelMapping.RGB)!),
                    "automatic metadata: exact lossless pixels after extras removal to " + target);
            }
            check(outputImage.GetAttribute("Comment") is null, "automatic metadata: unsupported text omitted from " + target);
        }
        var criticalPath = Path.Combine(root, "unknown-critical.png");
        File.WriteAllBytes(criticalPath, PngFixture.Create(4, 3, 8, 2, (_, _) => [0, 0, 0], ("ABCD", new byte[] { 1 })));
        Reject(() => adapter.Probe(new(Guid.NewGuid(), criticalPath), CancellationToken.None), "unknown critical PNG chunk is not removable metadata");
        var originalPreview = adapter.Preview(new(profileFacts), CancellationToken.None);
        check(originalPreview.Width == 3 && originalPreview.Height == 2, "adapter color-managed source preview never upscales");
        var resized = Execute(profileFacts, new(ImageFormat.Png, MaximumDimension: 2));
        check(resized.Width == 2 && resized.Height == 1, "adapter resize matches confirmed rounded dimensions");
        var corrupt = Path.Combine(root, "corrupt.png");
        File.WriteAllBytes(corrupt, File.ReadAllBytes(png)[..20]);
        Reject(() => adapter.Probe(new(Guid.NewGuid(), corrupt), CancellationToken.None), "truncated input before decoder");
        var fake = Path.Combine(root, "not-image.png");
        File.WriteAllText(fake, "not an image despite its extension");
        Reject(() => adapter.Probe(new(Guid.NewGuid(), fake), CancellationToken.None), "extension spoof");
        var changed = Path.Combine(root, "changed.png");
        File.Copy(png, changed);
        var stale = adapter.Probe(new(Guid.NewGuid(), changed), CancellationToken.None);
        File.Copy(transparent, changed, true);
        Reject(() => Execute(stale, new(ImageFormat.WebP)), "source changed after planning");
        var factsPng = adapter.Probe(new(Guid.NewGuid(), png), CancellationToken.None);
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [factsPng], new(ImageFormat.WebP), new("convert", new())).Items[0];
        Reject(() => adapter.Convert(new(plan, new(ImageFormat.WebP), png), CancellationToken.None), "attempt to write source directly");
        var unrelated = Path.Combine(root, "unrelated-empty.txt");
        using (File.Create(unrelated)) { }
        var linked = Path.Combine(root, $".context-suite-{factsPng.ItemId:N}.tmp");
        if (!CreateHardLink(linked, unrelated, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Reject(() => adapter.Convert(new(plan, new(ImageFormat.WebP), linked), CancellationToken.None), "hard-linked temporary output");
        check(new FileInfo(unrelated).Length == 0, "adapter never writes through a hard-linked output into another file");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { adapter.Probe(new(Guid.NewGuid(), png), cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "adapter respects cancellation before decode"); }

        ImageWorkResult Execute(ImageSourceFacts facts, ImageConversionOptions options)
        {
            var hash = SHA256.HashData(File.ReadAllBytes(facts.Path));
            var batch = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
            var confirmed = batch.Confirm(true, false, false);
            var folder = Path.Combine(root, "output-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $".context-suite-{facts.ItemId:N}.tmp");
            lastOutput = path;
            using (File.Create(path)) { }
            var result = adapter.Convert(new(confirmed.Plan.Items[0], options, path), CancellationToken.None);
            if (!SHA256.HashData(File.ReadAllBytes(facts.Path)).SequenceEqual(hash)) throw new InvalidOperationException("Adapter changed a source");
            return result;
        }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException or MagickException) { check(true, "adapter rejects " + name); return; }
            throw new InvalidOperationException("FAILED to reject " + name);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);
}
