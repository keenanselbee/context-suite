using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class AvifContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var options = new ImageConversionOptions(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality, Metadata: ImageMetadataMode.Automatic);
        var measurements = new List<object>();
        var sequence = 0;
        var png = Path.Combine(root, "authored.png");
        File.WriteAllBytes(png, PngFixture.Create(64, 48, 8, 6, (x, y) =>
            [(ushort)(x * 4 * 257), (ushort)(y * 5 * 257), (ushort)(((x / 8 + y / 8) % 2 == 0 ? 40 : 220) * 257), (ushort)((x % 16) * 17 * 257)]));
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Bmp3, MagickFormat.Tga })
        {
            var path = Path.Combine(root, "input-" + format);
            using (var source = new MagickImage(png))
            {
                if (format is MagickFormat.Jpeg or MagickFormat.Bmp3) { source.BackgroundColor = MagickColors.White; source.Alpha(AlphaOption.Remove); }
                source.Quality = 95;
                source.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                source.Settings.SetDefine(MagickFormat.WebP, "exact", true);
                source.Write(path, format);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var output = Execute(facts, options);
            using var expected = new MagickImage(path, format);
            CheckPixels(output, expected.GetPixels().ToByteArray(PixelMapping.RGBA)!, "source " + format);
            var outputFacts = adapter.Probe(new(Guid.NewGuid(), output), default);
            check(outputFacts.Format == ImageFormat.Avif && outputFacts.Width == 64 && outputFacts.Height == 48 &&
                outputFacts.HasTransparency == facts.HasTransparency && outputFacts.BitDepth == 8, "AVIF is reusable by the existing input decoder: " + format);
            var preview = adapter.Preview(new(facts, options), default);
            check(preview.Width == 64 && preview.Height == 48 && preview.BgraPixels.Length == 64 * 48 * 4, "AVIF encoded preview: " + format);
            check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), "AVIF preserves original bytes: " + format);
        }
        foreach (var orientation in Enumerable.Range(1, 8))
        {
            var path = Path.Combine(root, $"orientation-{orientation}.png");
            using (var source = new MagickImage(png))
            {
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)orientation);
                exif.SetValue(ExifTag.Artist, "AVIF fixture author"); exif.SetValue(ExifTag.XResolution, new Rational(150));
                exif.SetValue(ExifTag.YResolution, new Rational(75)); exif.SetValue(ExifTag.ResolutionUnit, (ushort)2);
                source.SetProfile(exif); source.Orientation = (OrientationType)orientation;
                source.Density = new Density(150, 75, DensityUnit.PixelsPerInch);
                source.Write(path, MagickFormat.Png);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var output = Execute(facts, options);
            var decoded = HeifCodec.Decode(File.ReadAllBytes(output), default);
            var metadata = new ExifProfile("Exif\0\0"u8.ToArray().Concat(decoded.Exif).ToArray());
            check(decoded.Info.Width == (orientation >= 5 ? 48 : 64) && decoded.Info.Height == (orientation >= 5 ? 64 : 48) &&
                metadata.GetValue(ExifTag.Orientation)?.Value == 1 && metadata.GetValue(ExifTag.Artist)?.Value == "AVIF fixture author",
                $"AVIF orientation applied once with descriptive EXIF retained: {orientation} ({decoded.Info.Width}x{decoded.Info.Height}; orientation={metadata.GetValue(ExifTag.Orientation)?.Value}; artist={metadata.GetValue(ExifTag.Artist)?.Value}; bytes={decoded.Exif.Length})");
            using var expected = new MagickImage(path); expected.AutoOrient();
            CheckPixels(output, expected.GetPixels().ToByteArray(PixelMapping.RGBA)!, "orientation " + orientation);
            var outputFacts = adapter.Probe(new(Guid.NewGuid(), output), default);
            check(outputFacts.Resolution is { } density && density.X == (orientation >= 5 ? 75 : 150) && density.Y == (orientation >= 5 ? 150 : 75),
                "AVIF normalized physical density: " + orientation);
        }
        var profilePath = Path.Combine(root, "profile16.png");
        File.WriteAllBytes(profilePath, PngFixture.Create(64, 48, 16, 6,
            (x, y) => [(ushort)(x * 1000), (ushort)(y * 1300), 19000, (ushort)((x % 16) * 4369)]));
        var xmp = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description xmlns:exif='http://ns.adobe.com/exif/1.0/' xmlns:photoshop='http://ns.adobe.com/photoshop/1.0/' exif:ColorSpace='2' photoshop:ICCProfile='Adobe RGB (1998)'/></rdf:RDF></x:xmpmeta>";
        using (var source = new MagickImage(profilePath))
        { source.SetProfile(ColorProfiles.AdobeRGB1998); source.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(xmp))); source.Write(profilePath, MagickFormat.Png); }
        var profiled = adapter.Probe(new(Guid.NewGuid(), profilePath), default);
        var profiledOutput = Execute(profiled, options);
        using (var expected = new MagickImage(profilePath))
        {
            expected.TransformColorSpace(ColorProfiles.AdobeRGB1998, ColorProfiles.SRGB, ColorTransformMode.HighRes); expected.Depth = 8;
            CheckPixels(profiledOutput, expected.GetPixels().ToByteArray(PixelMapping.RGBA)!, "16-bit ICC before quantization");
        }
        var profileDecoded = HeifCodec.Decode(File.ReadAllBytes(profiledOutput), default);
        var metadataText = Encoding.UTF8.GetString(profileDecoded.Xmp);
        check(profileDecoded.Icc.SequenceEqual(ColorProfiles.SRGB.ToByteArray()) && metadataText.Contains("sRGB IEC61966-2.1", StringComparison.Ordinal) &&
            !metadataText.Contains("Adobe RGB", StringComparison.Ordinal), "AVIF normalizes ICC and stale XMP color declarations");
        var removed = HeifCodec.Decode(File.ReadAllBytes(Execute(profiled, options with { Metadata = ImageMetadataMode.RemoveDescriptive })), default);
        check(removed.Xmp.Length == 0 && removed.Exif.Length == 0 && removed.Icc.SequenceEqual(ColorProfiles.SRGB.ToByteArray()), "AVIF descriptive removal retains sRGB interpretation");
        var conflictingExif = new ExifProfile(); conflictingExif.SetValue(ExifTag.Orientation, (ushort)6);
        var conflictingPath = Path.Combine(root, "conflicting-exif.avif");
        File.WriteAllBytes(conflictingPath, AvifCodec.Encode(1, 1, [30, 90, 150, 255], ColorProfiles.SRGB.ToByteArray(),
            conflictingExif.ToByteArray(), [], default));
        var conflictRefused = false;
        try { adapter.Probe(new(Guid.NewGuid(), conflictingPath), default); }
        catch (NotSupportedException) { conflictRefused = true; }
        catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput) { conflictRefused = true; }
        check(conflictRefused, "Existing AVIF input refuses conflicting parsed EXIF orientation");
        foreach (var (width, height) in new (uint, uint)[] { (1, 1), (1, 257), (257, 1), (17, 31), (257, 259) })
        {
            var path = Path.Combine(root, $"geometry-{width}-{height}.png");
            File.WriteAllBytes(path, PngFixture.Create(width, height, 8, 6,
                (x, y) => [(ushort)((x % 256) * 257), (ushort)((y % 256) * 257), 30000, (ushort)(((x + y) % 3 == 0 ? 0 : 255) * 257)]));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var output = Execute(facts, options);
            using var expected = new MagickImage(path);
            CheckPixels(output, expected.GetPixels().ToByteArray(PixelMapping.RGBA)!, $"small/odd geometry {width}x{height}");
            var decoded = HeifCodec.Decode(File.ReadAllBytes(output), default);
            check(decoded.Info.Width == width && decoded.Info.Height == height, "AVIF retains odd and one-pixel dimensions");
        }
        var grayPath = Path.Combine(root, "gray-profile.png");
        File.WriteAllBytes(grayPath, PngFixture.Create(53, 19, 16, 0, (x, y) => [(ushort)(x * 1200 + y * 100)]));
        var grayProfile = new ColorProfile(PngFixture.GrayProfile(ColorProfiles.SRGB.ToByteArray()));
        using (var gray = new MagickImage(grayPath))
        {
            gray.SetProfile(grayProfile); gray.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            gray.Write(grayPath, MagickFormat.Png);
        }
        var grayFacts = adapter.Probe(new(Guid.NewGuid(), grayPath), default);
        check(grayFacts.HasGrayscaleProfile, "AVIF fixture contains grayscale ICC interpretation");
        var grayOutput = Execute(grayFacts, options);
        using (var expected = new MagickImage(grayPath))
        {
            expected.TransformColorSpace(grayProfile, ColorProfiles.SRGB, ColorTransformMode.HighRes);
            CheckPixels(grayOutput, expected.GetPixels().ToByteArray(PixelMapping.RGBA)!, "16-bit gray ICC to sRGB");
        }
        var resized = Execute(profiled, options with { MaximumDimension = 23 });
        var resizedFacts = adapter.Probe(new(Guid.NewGuid(), resized), default);
        check(resizedFacts.Width == 23 && resizedFacts.Height == 17, "AVIF resize preserves planned aspect ratio");
        var changed = adapter.Probe(new(Guid.NewGuid(), png), default);
        File.AppendAllText(png, "changed");
        Reject(() => Execute(changed, options), "changed source");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledRequest = new ImagePreviewRequest(profiled, options);
        try { adapter.Preview(cancelledRequest, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
        catch (OperationCanceledException) { check(true, "AVIF pre-cancelled request"); }
        File.WriteAllText(Path.Combine(root, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));

        string Execute(ImageSourceFacts facts, ImageConversionOptions selected)
        {
            var directory = Path.Combine(root, "output-" + ++sequence); Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, $".context-suite-{facts.ItemId:N}.tmp"); using (File.Create(output)) { }
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], selected, new("convert", new()));
            check(plan.Items[0].CanExecute && !plan.ReplaceOriginal, "AVIF plan is executable and copy-only");
            var stopwatch = Stopwatch.StartNew();
            var result = adapter.Convert(new(plan.Items[0], selected, output), default);
            check(result.OutputBytes == new FileInfo(output).Length && result.BitDepth == 8, "AVIF encoded output validated and bound to result");
            measurements.Add(new { Source = Path.GetFileName(facts.Path), facts.Width, facts.Height, Bytes = result.OutputBytes, Milliseconds = stopwatch.Elapsed.TotalMilliseconds });
            return output;
        }
        void CheckPixels(string path, byte[] expected, string label)
        {
            var decoded = HeifCodec.Decode(File.ReadAllBytes(path), default);
            double squares = 0; long samples = 0; var alphaExact = true;
            for (var at = 0; at < expected.Length; at += 4)
            {
                var output = decoded.Pixels.AsSpan(at * 2, 8);
                alphaExact &= BinaryPrimitives.ReadUInt16LittleEndian(output[6..]) == expected[at + 3] * 257;
                if (expected[at + 3] == 0) continue;
                for (var channel = 0; channel < 3; channel++)
                { var difference = (BinaryPrimitives.ReadUInt16LittleEndian(output[(channel * 2)..]) + 128) / 257 - expected[at + channel]; squares += difference * difference; samples++; }
            }
            var rmse = samples == 0 ? 0 : Math.Sqrt(squares / samples);
            check(alphaExact && rmse <= 6, "AVIF independent decode exact alpha and RGB RMSE <=6: " + label + " (" + rmse.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }
        void Reject(Action action, string label)
        {
            var rejected = false;
            try { action(); } catch (Exception error) when (error is InvalidDataException or ImageFailureException) { rejected = true; }
            check(rejected, "AVIF rejects " + label);
        }
    }
}
