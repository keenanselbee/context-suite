using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class ImageMatrixContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(root, "matrix-scratch"));
        var matrix = Path.Combine(root, "matrix");
        Directory.CreateDirectory(matrix);
        foreach (byte depth in new byte[] { 8, 16 })
        foreach (byte color in new byte[] { 0, 2, 4, 6 })
        {
            ushort[] Pixel(int x, int y)
            {
                var red = (ushort)(1000 + x * 1500);
                var green = (ushort)(2000 + y * 2500);
                var alpha = (ushort)(x < 8 ? 0 : x < 16 ? 32768 : 65535);
                return color switch { 0 => [red], 2 => [red, green, 48000], 4 => [red, alpha], _ => [red, green, 48000, alpha] };
            }
            var path = Save($"{color}-{depth}.png", PngFixture.Create(32, 16, depth, color, Pixel));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            check(facts.BitDepth == depth && facts.HasTransparency == (color is 4 or 6) && facts.UnsupportedReason is null,
                $"matrix: independent color-type {color}, {depth}-bit PNG admitted accurately");
            foreach (var options in new[] { new ImageConversionOptions(ImageFormat.Jpeg, MatteRgb: 0xffffff),
                new(ImageFormat.WebP), new(ImageFormat.WebP, WebPLossless: true) })
            {
                var output = Execute(facts, options);
                using var reopened = new MagickImage(output);
                check(reopened.Width == 32 && reopened.Height == 16 && (options.Target != ImageFormat.Jpeg || reopened.IsOpaque),
                    $"matrix: {color}/{depth} -> {options.Target} {(options.WebPLossless ? "lossless" : "lossy")}");
                if (depth == 8 && options.WebPLossless)
                {
                    using var before = new MagickImage(path);
                    check(before.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(reopened.GetPixels().ToByteArray(PixelMapping.RGBA)!),
                        $"matrix: exact 8-bit RGBA retained for lossless color-type {color}");
                    var webp = adapter.Probe(new(Guid.NewGuid(), output), CancellationToken.None);
                    var png = Execute(webp, new(ImageFormat.Png));
                    using var roundTrip = new MagickImage(png);
                    check(before.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(roundTrip.GetPixels().ToByteArray(PixelMapping.RGBA)!),
                        $"matrix: lossless WebP -> PNG exact pixels, color-type {color}");
                }
                if (depth == 16)
                    check(ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new())).Items[0].Warnings.Any(w => w.Code == "precision-reduction"),
                        "matrix: real 16-bit reduction is disclosed");
            }
        }

        var uniform = Save("matte.png", PngFixture.Create(32, 32, 8, 6, (_, _) => [65535, 0, 0, 32896]));
        var matteFacts = adapter.Probe(new(Guid.NewGuid(), uniform), CancellationToken.None);
        foreach (var (background, expected) in new[] { (0xffffffu, new byte[] { 255, 127, 127 }), (0u, new byte[] { 128, 0, 0 }) })
        {
            var options = new ImageConversionOptions(ImageFormat.Jpeg, Quality: 100, MatteRgb: background);
            using var image = new MagickImage(Execute(matteFacts, options));
            var rgba = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            check(Enumerable.Range(0, 3).All(c => Math.Abs(rgba[c] - expected[c]) <= 3), "matrix: JPEG half-alpha matte matches independent compositing expectation");
            var preview = adapter.Preview(new(matteFacts, options), CancellationToken.None);
            check(Math.Abs(preview.BgraPixels[2] - rgba[0]) <= 1 && Math.Abs(preview.BgraPixels[1] - rgba[1]) <= 1 && Math.Abs(preview.BgraPixels[0] - rgba[2]) <= 1,
                "matrix: encoded matte preview agrees with actual full-size uniform result");
        }

        var valid = PngFixture.Create(4, 3, 8, 2, (_, _) => [45000, 20000, 10000]);
        foreach (var profile in new[] { ColorProfiles.SRGB, ColorProfiles.AdobeRGB1998 })
        {
            var path = Save("icc-" + Guid.NewGuid().ToString("N") + ".png", valid);
            using (var image = new MagickImage(path))
            {
                image.SetProfile(profile);
                image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
                image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
                image.Write(path);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
            foreach (var mode in Enum.GetValues<ImageMetadataMode>())
            {
                using var output = new MagickImage(Execute(facts, new(target, Metadata: mode)));
                check(output.GetColorProfile()!.ToByteArray().SequenceEqual(profile.ToByteArray()), $"matrix: exact ICC survives {target}/{mode}");
            }
        }

        var grayProfile = PngFixture.GrayProfile(ColorProfiles.SRGB.ToByteArray());
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(grayProfile);
            var iccp = "gray\0\0"u8.ToArray().Concat(compressed.ToArray()).ToArray();
            var path = Save("gray-icc.png", PngFixture.Create(32, 16, 8, 0, (x, _) => [(ushort)(x * 2000)], ("iCCP", iccp)));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            var before = adapter.Preview(new(facts), CancellationToken.None);
            foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
            {
                var outputPath = Execute(facts, new(target, Quality: 100, WebPLossless: target == ImageFormat.WebP));
                var outputFacts = adapter.Probe(new(Guid.NewGuid(), outputPath), CancellationToken.None);
                var after = adapter.Preview(new(outputFacts), CancellationToken.None);
                check(before.BgraPixels.Zip(after.BgraPixels).All(pair => Math.Abs(pair.First - pair.Second) <= 3),
                    "matrix: grayscale ICC preserves displayed tone in " + target);
                using var output = new MagickImage(outputPath);
                check(output.GetColorProfile()!.ToByteArray().SequenceEqual(grayProfile), "matrix: grayscale ICC retained in " + target);
            }
            var alphaPath = Save("gray-alpha-icc.png", PngFixture.Create(32, 16, 8, 4, (_, _) => [32896,32896], ("iCCP", iccp)));
            var alphaFacts = adapter.Probe(new(Guid.NewGuid(), alphaPath), CancellationToken.None);
            var matteOptions = new ImageConversionOptions(ImageFormat.Jpeg, Quality: 100, MatteRgb: 0x00ff00);
            check(ImageConversionPlanner.Create(Guid.NewGuid(), [alphaFacts], matteOptions, new("convert", new())).Items[0]
                .Warnings.Any(w => w.Code == "color-profile-conversion"), "matrix: grayscale ICC matte conversion is disclosed");
            using (var matte = new MagickImage(Execute(alphaFacts, matteOptions)))
            {
                var pixel = matte.GetPixels().ToByteArray(PixelMapping.RGB)!;
                check(Math.Abs(pixel[0] - 64) <= 3 && Math.Abs(pixel[1] - 191) <= 3 && Math.Abs(pixel[2] - 64) <= 3 &&
                    matte.GetColorProfile()!.ToByteArray().SequenceEqual(ColorProfiles.SRGB.ToByteArray()),
                    "matrix: colored matte retains green while transforming grayscale ICC to sRGB");
            }
            Reject("colored-png-gray-profile.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [10000,20000,30000], ("iCCP", iccp)));
        }

        var metadataPath = Save("metadata.png", valid);
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Artist, "Context Suite fixture artist");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(49), new Rational(15), new Rational(0) });
            exif.SetValue(ExifTag.Orientation, (ushort)1);
            var xmp = Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0\0\0\0\0<x:xmpmeta xmlns:x='adobe:ns:meta/' xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:RDF><rdf:Description xmlns:tiff='http://ns.adobe.com/tiff/1.0/' xmlns:exif='http://ns.adobe.com/exif/1.0/' tiff:Orientation='1' tiff:ImageWidth='4' exif:GPSLatitude='49,15N'/></rdf:RDF></x:xmpmeta>");
            File.WriteAllBytes(metadataPath, PngFixture.Create(4, 3, 8, 2, (_, _) => [45000, 20000, 10000],
                ("eXIf", exif.ToByteArray()[6..]), ("iTXt", xmp)));
        }
        var metadata = adapter.Probe(new(Guid.NewGuid(), metadataPath), CancellationToken.None);
        foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
        {
            using var preserved = new MagickImage(Execute(metadata, new(target, MaximumDimension: 2)));
            check(preserved.GetExifProfile()?.GetValue(ExifTag.Artist)?.Value == "Context Suite fixture artist" &&
                preserved.GetExifProfile()?.GetValue(ExifTag.GPSLatitude)?.Value.Length == 3 &&
                Encoding.UTF8.GetString(preserved.GetXmpProfile()!.ToByteArray()).Contains("49,15N", StringComparison.Ordinal), "matrix: descriptive metadata and location survive explicit preservation");
            check(preserved.GetExifProfile()?.GetValue(ExifTag.PixelXDimension)?.Value == new Number(2) &&
                Encoding.UTF8.GetString(preserved.GetXmpProfile()!.ToByteArray()).Contains("ImageWidth=\"2\"", StringComparison.Ordinal), "matrix: metadata dimensions normalize after resize");
            using var stripped = new MagickImage(Execute(metadata, new(target, Metadata: ImageMetadataMode.RemoveDescriptive)));
            check(stripped.GetExifProfile() is null && stripped.GetXmpProfile() is null, "matrix: explicit removal eliminates EXIF/XMP location and descriptive profiles");
        }

        Reject("bad-crc.png", valid.Select((value, index) => index == valid.Length - 1 ? (byte)(value ^ 1) : value).ToArray());
        Reject("animation.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0, 0, 0], ("acTL", new byte[] { 0,0,0,2, 0,0,0,0 })));
        Reject("hdr.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0, 0, 0], ("cICP", new byte[] { 9,16,0,1 })));
        foreach (var chunk in new[] { ("mDCv", new byte[24]), ("cLLi", new byte[8]) })
            Reject(chunk.Item1 + ".png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0], chunk));
        foreach (var marker in new[] { "hdrgm", "gainmap", "hdr-gain-map", "urn:iso:std:iso:ts:21496" })
        {
            var path = Save("hdr-xmp-" + Guid.NewGuid().ToString("N") + ".png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
                ("iTXt", Xmp("<rdf:Description xmlns:hdr='" + marker + "' hdr:Version='1'/>"))));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.WebP, Metadata: ImageMetadataMode.RemoveDescriptive),
                new("convert", new())).HasExecutableItems, "matrix: metadata removal cannot bypass HDR admission: " + marker);
        }
        var cmykPath = Path.Combine(matrix, "cmyk.jpg");
        using (var cmyk = new MagickImage(valid))
        {
            cmyk.TransformColorSpace(ColorProfiles.SRGB, ColorProfiles.USWebCoatedSWOP);
            cmyk.Write(cmykPath, MagickFormat.Jpeg);
        }
        Reject("unsupported-cmyk.jpg", File.ReadAllBytes(cmykPath));
        var huge = valid.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(16), 20000);
        PngFixture.Chunk("IHDR", huge.AsSpan(16, 13).ToArray()).CopyTo(huge, 8);
        Reject("huge-header.png", huge);
        var badXmp = PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("iTXt", Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0\0\0\0\0<!DOCTYPE x [<!ENTITY bad SYSTEM 'file:///no-read'>]><x>&bad;</x>")));
        Reject("unsafe-xmp.png", badXmp);

        foreach (var (x, y, unit) in new[] { (11811u, 5906u, (byte)1), (2u, 1u, (byte)0) })
        {
            var physical = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(physical, x);
            BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(4), y);
            physical[8] = unit;
            var path = Save($"resolution-{unit}.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [10000, 20000, 30000], ("pHYs", physical)));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            check(facts.Resolution == new ImageResolution(x, y, unit == 1 ? (ushort)3 : (ushort)1, unit == 1 ? 100u : 1u),
                "matrix: independent PNG physical/aspect hint represented exactly");
            foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
            {
                var outputPath = Execute(facts, new(target));
                var output = adapter.Probe(new(Guid.NewGuid(), outputPath), CancellationToken.None);
                check(output.Resolution == facts.Resolution, $"matrix: exact physical/aspect resolution retained in {target} EXIF");
                var roundTrip = adapter.Probe(new(Guid.NewGuid(), Execute(output, new(ImageFormat.Png))), CancellationToken.None);
                check(roundTrip.Resolution == facts.Resolution, "matrix: resolution survives round trip to PNG");
                var stripped = adapter.Probe(new(Guid.NewGuid(), Execute(facts, new(target, Metadata: ImageMetadataMode.RemoveDescriptive))), CancellationToken.None);
                check(stripped.Resolution is null, "matrix: explicit metadata removal clears physical/aspect hints");
            }
        }

        var jpegFacts = adapter.Probe(new(Guid.NewGuid(), Save("jpeg-source.png", valid)), CancellationToken.None);
        var jpegBytes = File.ReadAllBytes(Execute(jpegFacts, new(ImageFormat.Jpeg)));
        foreach (var payload in new[] { "MPF\0payload", "http://ns.adobe.com/xap/1.0/\0<x>hdrgm</x>", "Exif\0\0late" })
        {
            var data = Encoding.ASCII.GetBytes(payload);
            var segment = new byte[data.Length + 4];
            segment[0] = 255; segment[1] = 225;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(data.Length + 2));
            data.CopyTo(segment, 4);
            Reject("late-app-" + Guid.NewGuid().ToString("N") + ".jpg", jpegBytes[..^2].Concat(segment).Concat(jpegBytes[^2..]).ToArray());
        }
        var progressivePath = Save("progressive.jpg", jpegBytes);
        using (var progressive = new MagickImage(progressivePath))
        {
            progressive.Settings.Interlace = Interlace.Plane;
            progressive.Write(progressivePath, MagickFormat.Jpeg);
        }
        var progressiveFacts = adapter.Probe(new(Guid.NewGuid(), progressivePath), CancellationToken.None);
        using (var converted = new MagickImage(Execute(progressiveFacts, new(ImageFormat.Png))))
            check(converted.Width == 4 && converted.Height == 3, "matrix: multi-scan progressive JPEG remains supported");

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(new byte[4 * 1024 * 1024 + 1]);
            Reject("profile-bomb.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
                ("iCCP", "fixture\0\0"u8.ToArray().Concat(compressed.ToArray()).ToArray())));
        }
        var badIcc = ColorProfiles.SRGB.ToByteArray();
        BinaryPrimitives.WriteUInt32BigEndian(badIcc.AsSpan(128), uint.MaxValue);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(badIcc);
            Reject("invalid-icc.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
                ("iCCP", "fixture\0\0"u8.ToArray().Concat(compressed.ToArray()).ToArray())));
        }
        var animatedPath = Path.Combine(matrix, "animated.webp");
        using (var frames = new MagickImageCollection())
        {
            for (var i = 0; i < 2; i++)
            {
                var frame = new MagickImage(valid) { AnimationDelay = 10 };
                if (i == 1) frame.Negate();
                frames.Add(frame);
            }
            frames.Write(animatedPath, MagickFormat.WebP);
        }
        Reject("real-animation.webp", File.ReadAllBytes(animatedPath));
        var largePath = Save("preview-limit.png", PngFixture.Create(2048, 1024, 8, 2, (x, y) => [(ushort)(x * 16), (ushort)(y * 32), 20000]));
        var largeFacts = adapter.Probe(new(Guid.NewGuid(), largePath), CancellationToken.None);
        var bounded = adapter.Preview(new(largeFacts, new(ImageFormat.WebP)), CancellationToken.None);
        check(bounded.Width == 512 && bounded.Height == 256 && bounded.BgraPixels.Length == 512 * 256 * 4,
            "matrix: real large-image encoded preview is limited to 512 pixels per side");
        var duplicateExif = new ExifProfile();
        duplicateExif.SetValue(ExifTag.Artist, "first");
        var secondExif = new ExifProfile();
        secondExif.SetValue(ExifTag.Artist, "second");
        Reject("conflicting-exif.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("eXIf", duplicateExif.ToByteArray()[6..]), ("eXIf", secondExif.ToByteArray()[6..])));
        var resolutionExif = new ExifProfile();
        resolutionExif.SetValue(ExifTag.XResolution, new Rational(240));
        resolutionExif.SetValue(ExifTag.YResolution, new Rational(120));
        resolutionExif.SetValue(ExifTag.ResolutionUnit, (ushort)2);
        resolutionExif.SetValue(ExifTag.Orientation, (ushort)6);
        var rotatedPath = Save("rotated-resolution.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0], ("eXIf", resolutionExif.ToByteArray()[6..])));
        var rotated = adapter.Probe(new(Guid.NewGuid(), rotatedPath), CancellationToken.None);
        var rotatedOutput = adapter.Probe(new(Guid.NewGuid(), Execute(rotated, new(ImageFormat.WebP))), CancellationToken.None);
        check(rotatedOutput.Width == 3 && rotatedOutput.Height == 4 && rotatedOutput.Resolution == new ImageResolution(120, 240, 2),
            "matrix: auto-orientation swaps directional resolution with pixel axes");
        var conflictDensity = new byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(conflictDensity, 1000);
        BinaryPrimitives.WriteUInt32BigEndian(conflictDensity.AsSpan(4), 1000);
        conflictDensity[8] = 1;
        Reject("conflicting-resolution.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("pHYs", conflictDensity), ("eXIf", resolutionExif.ToByteArray()[6..])));

        foreach (var orientation in Enumerable.Range(1, 8))
        foreach (var mode in Enum.GetValues<ImageMetadataMode>())
        {
            var xmp = Xmp($"<tiff:Orientation>{orientation}</tiff:Orientation>");
            var path = Save($"xmp-orientation-{orientation}-{mode}.png", PngFixture.Create(3, 2, 8, 2,
                (x, y) => [(ushort)((x + y * 3 + 1) * 7710), 0, 0], ("iTXt", xmp)));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            check(facts.Orientation == orientation, "matrix: XMP-only orientation enters the immutable plan");
            using var output = new MagickImage(Execute(facts, new(ImageFormat.WebP, WebPLossless: true, Metadata: mode)));
            var pixels = output.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            var matches = output.Width == (orientation >= 5 ? 2 : 3) && output.Height == (orientation >= 5 ? 3 : 2);
            for (var y = 0; y < output.Height; y++)
            for (var x = 0; x < output.Width; x++)
            {
                var (sx, sy) = orientation switch
                {
                    1 => (x, y), 2 => (2 - x, y), 3 => (2 - x, 1 - y), 4 => (x, 1 - y),
                    5 => (y, x), 6 => (y, 1 - x), 7 => (2 - y, 1 - x), 8 => (2 - y, x),
                    _ => throw new InvalidOperationException()
                };
                matches &= pixels[(y * (int)output.Width + x) * 4] == (sx + sy * 3 + 1) * 30;
            }
            check(matches, $"matrix: XMP orientation {orientation}/{mode} matches independently mapped pixels");
        }
        Reject("conflicting-xmp-orientation.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("eXIf", resolutionExif.ToByteArray()[6..]), ("iTXt", Xmp("<tiff:Orientation>3</tiff:Orientation>"))));
        Reject("duplicate-xmp-orientation.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("iTXt", Xmp("<tiff:Orientation>3</tiff:Orientation><tiff:Orientation>6</tiff:Orientation>"))));
        Reject("invalid-xmp-orientation.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("iTXt", Xmp("<tiff:Orientation>9</tiff:Orientation>"))));
        foreach (var payload in new[] { "ICC_PROFILE\0", "Exif\0\0", "JFIF\0", "Adobe", "http://ns.adobe.com/xap/1.0/\0" })
        {
            var data = Encoding.ASCII.GetBytes(payload);
            var segment = new byte[data.Length + 4];
            segment[0] = 255; segment[1] = 227; // APP3 is not a defined location for any of these profiles.
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(data.Length + 2));
            data.CopyTo(segment, 4);
            Reject("misplaced-profile-" + Guid.NewGuid().ToString("N") + ".jpg", jpegBytes[..2].Concat(segment).Concat(jpegBytes[2..]).ToArray());
        }

        foreach (var properties in new[] {
            "<tiff:Orientation>6</tiff:Orientation><tiff:XResolution>300/7</tiff:XResolution><tiff:YResolution>150/7</tiff:YResolution><tiff:ResolutionUnit>2</tiff:ResolutionUnit>",
            "<rdf:Description tiff:Orientation='6' tiff:XResolution='300/7' tiff:YResolution='150/7' tiff:ResolutionUnit='2'/>" })
        {
            var path = Save("xmp-resolution-" + Guid.NewGuid().ToString("N") + ".png", PngFixture.Create(4, 3, 8, 2, (_, _) => [10000,20000,30000], ("iTXt", Xmp(properties))));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
            check(facts.Resolution == new ImageResolution(300, 150, 2, 7), "matrix: XMP-only rational resolution is represented exactly");
            foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
            {
                var output = adapter.Probe(new(Guid.NewGuid(), Execute(facts, new(target))), CancellationToken.None);
                check(output.Resolution == new ImageResolution(150, 300, 2, 7) && output.Width == 3 && output.Height == 4,
                    "matrix: rotated EXIF/XMP resolution agrees after conversion to " + target);
                var roundTrip = adapter.Probe(new(Guid.NewGuid(), Execute(output, new(ImageFormat.Png))), CancellationToken.None);
                check(roundTrip.Resolution == output.Resolution, "matrix: XMP rational resolution survives PNG round trip");
                var removed = adapter.Probe(new(Guid.NewGuid(), Execute(facts, new(target, Metadata: ImageMetadataMode.RemoveDescriptive))), CancellationToken.None);
                check(removed.Resolution is null, "matrix: removal clears XMP-only resolution");
            }
        }
        foreach (var properties in new[] {
            "<tiff:XResolution>300/0</tiff:XResolution><tiff:YResolution>150/7</tiff:YResolution><tiff:ResolutionUnit>2</tiff:ResolutionUnit>",
            "<tiff:XResolution>300/7</tiff:XResolution>",
            "<rdf:Description tiff:XResolution='300/7' tiff:YResolution='150/7' tiff:ResolutionUnit='4'/>",
            "<rdf:Description tiff:XResolution='300/7' tiff:YResolution='150/7' tiff:ResolutionUnit='2'/><tiff:XResolution>240</tiff:XResolution>" })
            Reject("invalid-xmp-density-" + Guid.NewGuid().ToString("N") + ".png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0], ("iTXt", Xmp(properties))));
        Reject("conflicting-xmp-density.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [0,0,0],
            ("eXIf", resolutionExif.ToByteArray()[6..]),
            ("iTXt", Xmp("<rdf:Description tiff:XResolution='2401/10' tiff:YResolution='120' tiff:ResolutionUnit='2'/>"))));

        var thumbnailExif = new byte[68 + jpegBytes.Length];
        "II"u8.CopyTo(thumbnailExif);
        BinaryPrimitives.WriteUInt16LittleEndian(thumbnailExif.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(thumbnailExif.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(thumbnailExif.AsSpan(8), 1);
        Entry(10, 0x112, 3, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(thumbnailExif.AsSpan(22), 26);
        BinaryPrimitives.WriteUInt16LittleEndian(thumbnailExif.AsSpan(26), 3);
        Entry(28, 0x103, 3, 6);
        Entry(40, 0x201, 4, 68);
        Entry(52, 0x202, 4, (uint)jpegBytes.Length);
        jpegBytes.CopyTo(thumbnailExif, 68);
        var thumbnailPath = Save("embedded-thumbnails.png", PngFixture.Create(4, 3, 8, 2, (_, _) => [45000,20000,10000],
            ("eXIf", thumbnailExif), ("iTXt", Xmp("<xmp:Thumbnails xmlns:xmp='http://ns.adobe.com/xap/1.0/'><rdf:Alt>" +
                "<rdf:li rdf:parseType='Resource' xmlns:g='http://ns.adobe.com/xap/1.0/g/img/'>" +
                "<g:width>4</g:width><g:height>3</g:height><g:format>JPEG</g:format><g:image>" +
                System.Convert.ToBase64String(jpegBytes) + "</g:image></rdf:li></rdf:Alt></xmp:Thumbnails>"))));
        using (var original = new MagickImage(thumbnailPath))
            check(original.GetExifProfile()?.ThumbnailLength == jpegBytes.Length, "matrix: independent IFD1 fixture contains a real JPEG thumbnail");
        var thumbnailFacts = adapter.Probe(new(Guid.NewGuid(), thumbnailPath), CancellationToken.None);
        foreach (var mode in Enum.GetValues<ImageMetadataMode>())
        foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP })
        {
            using var output = new MagickImage(Execute(thumbnailFacts, new(target, MaximumDimension: 2, Metadata: mode)));
            check((output.GetExifProfile()?.ThumbnailLength ?? 0) == 0 &&
                (output.GetXmpProfile() is not { } xmp || !Encoding.UTF8.GetString(xmp.ToByteArray()).Contains("Thumbnails", StringComparison.Ordinal)),
                $"matrix: stale embedded EXIF/XMP thumbnails removed for {target}/{mode}");
        }
        void Entry(int offset, ushort tag, ushort type, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(thumbnailExif.AsSpan(offset), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(thumbnailExif.AsSpan(offset + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(thumbnailExif.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(thumbnailExif.AsSpan(offset + 8), value);
        }
        static byte[] Xmp(string properties) => Encoding.UTF8.GetBytes("XML:com.adobe.xmp\0\0\0\0\0" +
            "<x:xmpmeta xmlns:x='adobe:ns:meta/' xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>" +
            "<rdf:RDF><rdf:Description xmlns:tiff='http://ns.adobe.com/tiff/1.0/'>" + properties + "</rdf:Description></rdf:RDF></x:xmpmeta>");

        string Save(string name, byte[] bytes)
        {
            var path = Path.Combine(matrix, name); File.WriteAllBytes(path, bytes); return path;
        }
        string Execute(ImageSourceFacts facts, ImageConversionOptions options)
        {
            var original = SHA256.HashData(File.ReadAllBytes(facts.Path));
            var planned = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
            if (!planned.HasExecutableItems) throw new InvalidDataException(Path.GetFileName(facts.Path) + ": " + planned.Items[0].BlockReason);
            var plan = planned.Confirm(true, false, false);
            var folder = Path.Combine(matrix, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $".context-suite-{facts.ItemId:N}.tmp");
            using (File.Create(path)) { }
            adapter.Convert(new(plan.Plan.Items[0], options, path), CancellationToken.None);
            if (!SHA256.HashData(File.ReadAllBytes(facts.Path)).SequenceEqual(original)) throw new InvalidOperationException("Source changed in matrix execution");
            return path;
        }
        void Reject(string name, byte[] bytes)
        {
            var path = Save(name, bytes);
            try { adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException or MagickException or System.Xml.XmlException)
            { check(true, "matrix: rejects " + name); return; }
            throw new InvalidOperationException("Accepted invalid/unsupported fixture: " + name);
        }
    }
}
