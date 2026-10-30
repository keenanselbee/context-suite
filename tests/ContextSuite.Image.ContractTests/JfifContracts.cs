using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class JfifContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var png = PngFixture.Create(9, 5, 8, 2, (x, y) => [(ushort)(x * 7000), (ushort)(y * 13000), 20000]);
        var jpegPath = Path.Combine(root, "photo.jpg");
        var jfifPath = Path.Combine(root, "photo.JfIf");
        using (var image = new MagickImage(png))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.Artist, "Context Suite contract");
            image.SetProfile(exif);
            image.Orientation = OrientationType.RightTop;
            image.Quality = 90;
            image.Write(jpegPath, MagickFormat.Jpeg);
        }
        File.Copy(jpegPath, jfifPath);
        var id = Guid.NewGuid();
        var jpegFacts = adapter.Probe(new(id, jpegPath), default);
        var facts = adapter.Probe(new(id, jfifPath), default);
        check(facts.Format == ImageFormat.Jpeg && facts.Orientation == 6 && facts.Width == 9 && facts.Height == 5 &&
            facts.Sha256 == jpegFacts.Sha256 && facts.ProfileNames.SequenceEqual(jpegFacts.ProfileNames),
            "JFIF uses content-validated JPEG facts, metadata and orientation");
        var original = SHA256.HashData(File.ReadAllBytes(jfifPath));
        foreach (var (target, encoded) in new[] { (ImageFormat.Png, MagickFormat.Png), (ImageFormat.WebP, MagickFormat.WebP),
            (ImageFormat.Bmp, MagickFormat.Bmp), (ImageFormat.Tga, MagickFormat.Tga) })
        {
            var options = new ImageConversionOptions(target, WebPLossless: target == ImageFormat.WebP,
                Metadata: ImageMetadataMode.Automatic);
            var path = Execute(facts, options);
            using var output = new MagickImage(path, encoded);
            using var expected = new MagickImage(jpegPath, MagickFormat.Jpeg);
            expected.AutoOrient();
            check(output.Width == 5 && output.Height == 9 &&
                output.GetPixels().ToByteArray(PixelMapping.RGB)!.SequenceEqual(expected.GetPixels().ToByteArray(PixelMapping.RGB)!),
                "JFIF retains decoded JPEG pixels and applies orientation exactly once: " + target);
            check(SHA256.HashData(File.ReadAllBytes(jfifPath)).SequenceEqual(original), "JFIF original retained: " + target);
        }
        foreach (var mode in new[] { ImageMetadataMode.Preserve, ImageMetadataMode.RemoveDescriptive })
        {
            using var output = new MagickImage(Execute(facts, new(ImageFormat.Png, Metadata: mode)), MagickFormat.Png);
            check(mode == ImageMetadataMode.Preserve
                ? output.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value == "Context Suite contract"
                : !output.ProfileNames.Any(name => name is "exif" or "xmp"), "JFIF reuses JPEG metadata policy: " + mode);
        }
        var disguised = Path.Combine(root, "disguised.jfif");
        File.WriteAllBytes(disguised, png);
        Reject(() => adapter.Probe(new(Guid.NewGuid(), disguised), default), "non-JPEG content under JFIF extension");
        File.WriteAllBytes(disguised, File.ReadAllBytes(jpegPath)[..24]);
        Reject(() => adapter.Probe(new(Guid.NewGuid(), disguised), default), "truncated JPEG under JFIF extension");
        File.AppendAllText(jfifPath, "changed");
        Reject(() => Execute(facts, new(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic)), "source changed after planning");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { adapter.Probe(new(Guid.NewGuid(), jpegPath), cancelled.Token); }
        catch (OperationCanceledException) { check(true, "JPEG alias probe respects cancellation"); return; }
        throw new InvalidOperationException("JPEG alias probe ignored cancellation.");

        string Execute(ImageSourceFacts source, ImageConversionOptions options)
        {
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new(ReplaceOriginals: true)), true);
            check(plan.Items[0].CanExecute && !plan.ReplaceOriginal, "JFIF mandatory copy despite overwrite setting");
            var directory = Path.Combine(root, "out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $".context-suite-{source.ItemId:N}.tmp");
            File.WriteAllBytes(path, []);
            var result = adapter.Convert(new(plan.Items[0], options, path), default);
            check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                "JFIF result binds validated output bytes");
            return path;
        }

        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception error) when (error is ImageFailureException or InvalidDataException or MagickException)
            { check(true, "JFIF rejects " + reason); return; }
            throw new InvalidOperationException("JFIF accepted " + reason);
        }
    }
}
