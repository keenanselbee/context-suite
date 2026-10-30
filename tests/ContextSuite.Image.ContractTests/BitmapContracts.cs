using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class BitmapContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "bitmaps");
        Directory.CreateDirectory(directory);
        var adapter = new ImageAdapter(Path.Combine(directory, "scratch"));
        byte[] pixels = [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255, 0,0,0,255, 255,0,255,255];
        var inputs = new Dictionary<ImageFormat, string>();
        foreach (var topDown in new[] { false, true })
        {
            var path = Save("bmp-" + topDown, Bmp(pixels, topDown));
            CheckInput(path, ImageFormat.Bmp, pixels);
            inputs[ImageFormat.Bmp] = path;
        }
        foreach (var origin in new byte[] { 0, 16, 32, 48 })
        foreach (var rle in new[] { false, true })
        foreach (var alpha in new[] { false, true })
        {
            var expected = (byte[])pixels.Clone();
            if (alpha) { expected[3] = 0; expected[7] = 128; }
            var path = Save($"tga-{origin}-{rle}-{alpha}", Tga(expected, origin, rle, alpha));
            CheckInput(path, ImageFormat.Tga, expected);
            if (!alpha) inputs[ImageFormat.Tga] = path;
        }
        // Independent bitmap input establishes the original expected pixel layout.
        using (var source = new MagickImage(inputs[ImageFormat.Bmp], MagickFormat.Bmp))
        foreach (var (format, magick) in new[] { (ImageFormat.Png, MagickFormat.Png), (ImageFormat.Jpeg, MagickFormat.Jpeg), (ImageFormat.WebP, MagickFormat.WebP) })
        {
            source.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            source.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            var path = Path.Combine(directory, "input-" + format);
            source.Write(path, magick);
            inputs[format] = path;
        }
        foreach (var source in inputs)
        foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga }.Where(f => f != source.Key))
        {
            var before = SHA256.HashData(File.ReadAllBytes(source.Value));
            var facts = adapter.Probe(new(Guid.NewGuid(), source.Value), default);
            var options = new ImageConversionOptions(target, WebPLossless: target == ImageFormat.WebP, Metadata: ImageMetadataMode.RemoveDescriptive);
            var output = Execute(facts, options);
            var outputFacts = adapter.Probe(new(Guid.NewGuid(), output), default);
            check(outputFacts.Format == target && outputFacts.Width == 3 && outputFacts.Height == 2 && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source.Value))),
                $"bitmap matrix: {source.Key} -> {target}, validated signature, dimensions and unchanged source");
            if (target != ImageFormat.Jpeg)
            {
                using var a = new MagickImage(source.Value, source.Key == ImageFormat.Tga ? MagickFormat.Tga : MagickFormat.Unknown);
                using var b = new MagickImage(output, target == ImageFormat.Tga ? MagickFormat.Tga : MagickFormat.Unknown);
                check(a.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(b.GetPixels().ToByteArray(PixelMapping.RGBA)!),
                    $"bitmap matrix: {source.Key} -> {target} exact lossless pixels");
            }
            if (target == ImageFormat.Bmp)
            {
                var bytes = File.ReadAllBytes(output);
                check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14)) == 40 && bytes[28] == 24 && bytes[30] == 0,
                    "BMP output is explicitly 24-bit uncompressed INFOHEADER, even for few-color images");
            }
        }
        var transparent = (byte[])pixels.Clone(); transparent[3] = 0; transparent[7] = 128;
        var transparentPath = Save("transparent", Tga(transparent, 32, false, true));
        var alphaFacts = adapter.Probe(new(Guid.NewGuid(), transparentPath), default);
        check(!Plan(alphaFacts, new(ImageFormat.Bmp)).Items[0].CanExecute, "transparent BMP requires an explicit matte");
        foreach (var target in new[] { ImageFormat.Png, ImageFormat.WebP, ImageFormat.Bmp })
        {
            var options = new ImageConversionOptions(target, WebPLossless: target == ImageFormat.WebP, MatteRgb: target == ImageFormat.Bmp ? 0xffffffu : null);
            var output = Execute(alphaFacts, options);
            using var image = new MagickImage(output);
            var actual = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            check(target == ImageFormat.Bmp ? actual.AsSpan(0, 4).SequenceEqual(new byte[] { 255,255,255,255 }) : actual.SequenceEqual(transparent),
                $"TGA alpha -> {target}: matte or exact straight-alpha pixels");
            var preview = adapter.Preview(new(alphaFacts, options), default);
            check(preview.Width == 3 && preview.Height == 2 && preview.BgraPixels[2] == actual[0] && preview.BgraPixels[3] == actual[3],
                $"TGA -> {target}: encoded preview matches actual output");
        }
        var pngAlpha = Execute(alphaFacts, new(ImageFormat.Png));
        var backToTga = Execute(adapter.Probe(new(Guid.NewGuid(), pngAlpha), default), new(ImageFormat.Tga, Metadata: ImageMetadataMode.RemoveDescriptive));
        CheckInput(backToTga, ImageFormat.Tga, transparent);

        var withId = Tga(pixels, 32, false, false);
        withId = withId[..18].Concat(new byte[] { 65 }).Concat(withId[18..]).ToArray(); withId[0] = 1;
        var metadata = adapter.Probe(new(Guid.NewGuid(), Save("id", withId)), default);
        check(!Plan(metadata, new(ImageFormat.Png)).HasExecutableItems, "TGA image ID cannot silently disappear");
        _ = Execute(metadata, new(ImageFormat.Png, Metadata: ImageMetadataMode.RemoveDescriptive));
        check(true, "TGA image ID can be removed with explicit policy");
        var dpi = Bmp(pixels, false); BinaryPrimitives.WriteUInt32LittleEndian(dpi.AsSpan(38), 3780); BinaryPrimitives.WriteUInt32LittleEndian(dpi.AsSpan(42), 3780);
        var dpiFacts = adapter.Probe(new(Guid.NewGuid(), Save("dpi", dpi)), default);
        check(dpiFacts.Resolution == new ImageResolution(3780,3780,3,100), "BMP pixels/metre resolution admitted exactly");
        var dpiPng = adapter.Probe(new(Guid.NewGuid(), Execute(dpiFacts, new(ImageFormat.Png))), default);
        check(dpiPng.Resolution is { } resolution && resolution.Unit == 3 &&
            (ulong)resolution.X * 100 == 3780UL * resolution.Divisor && (ulong)resolution.Y * 100 == 3780UL * resolution.Divisor,
            "BMP resolution preserved exactly through normalized PNG rationals");
        check(!Plan(dpiFacts, new(ImageFormat.Tga)).HasExecutableItems, "TGA output blocks unsupported resolution preservation");

        var profilePath = Path.Combine(directory, "profile.png");
        using (var image = new MagickImage(inputs[ImageFormat.Png]))
        { image.SetProfile(ColorProfiles.AdobeRGB1998); image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true); image.Write(profilePath, MagickFormat.Png); }
        var profileFacts = adapter.Probe(new(Guid.NewGuid(), profilePath), default);
        foreach (var target in new[] { ImageFormat.Bmp, ImageFormat.Tga })
        {
            var options = new ImageConversionOptions(target, Metadata: ImageMetadataMode.RemoveDescriptive, MaximumDimension: 2);
            check(Plan(profileFacts, options).Items[0].Warnings.Any(w => w.Code == "color-profile-conversion"), "bitmap profile conversion is disclosed");
            using var output = new MagickImage(Execute(profileFacts, options), target == ImageFormat.Tga ? MagickFormat.Tga : MagickFormat.Bmp);
            check(output.GetColorProfile() is null && output.Width == 2 && output.Height == 1, "bitmap ICC transform, profile removal and bounded resize");
            using var expected = new MagickImage(profilePath);
            expected.Resize(new MagickGeometry(2, 1) { IgnoreAspectRatio = true });
            expected.TransformColorSpace(ColorProfiles.AdobeRGB1998, ColorProfiles.SRGB);
            var expectedPixels = expected.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            var actualPixels = output.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            check(expectedPixels.Zip(actualPixels).All(p => Math.Abs(p.First - p.Second) <= 1), "bitmap pixels are color-transformed, not just relabeled");
        }
        foreach (var value in new byte[] { 0, 127, 255 })
        {
            var path = Save("gray-" + value, PngFixture.Create(3, 2, 8, 0, (_, _) => [(ushort)(value * 257)]));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            foreach (var target in new[] { ImageFormat.Bmp, ImageFormat.Tga })
            {
                var encoded = Execute(facts, new(target));
                var data = File.ReadAllBytes(encoded);
                check(target == ImageFormat.Bmp ? data[28] == 24 : data[2] == 2 && data[16] == 24,
                    $"{target}: grayscale/bilevel input still emits the promised true-color variant");
            }
        }
        var rawRle = Tga(pixels, 32, false, false); rawRle[2] = 10;
        rawRle = rawRle[..18].Concat(new byte[] { 5 }).Concat(rawRle[18..]).ToArray();
        CheckInput(Save("raw-rle", rawRle), ImageFormat.Tga, pixels);
        var solid = Enumerable.Range(0, 6).SelectMany(_ => pixels[..4]).ToArray();
        var repeated = Tga(solid, 32, false, false); repeated[2] = 10;
        repeated = repeated[..18].Concat(new byte[] { 133 }).Concat(repeated[18..21]).ToArray();
        CheckInput(Save("multi-rle", repeated), ImageFormat.Tga, solid);

        var badBmp = Bmp(pixels, false); badBmp[28] = 32; Reject(badBmp, "ambiguous BMP alpha variant");
        badBmp = Bmp(pixels, false); badBmp[10] = 53; Reject(badBmp, "overlapping BMP pixels");
        badBmp = Bmp(pixels, false); badBmp[30] = 1; Reject(badBmp, "BMP RLE/palette variant");
        badBmp = Bmp(pixels, false); badBmp[18] = 255; Reject(badBmp, "BMP inconsistent size");
        Reject(Bmp(pixels, false)[..^1], "truncated BMP");
        var badTga = Tga(pixels, 32, false, true); badTga[17] = 32; Reject(badTga, "undefined TGA alpha semantics");
        badTga = Tga(pixels, 32, true, false); badTga[18] = 255; Reject(badTga, "TGA RLE overrun");
        badTga = Tga(pixels, 32, false, false); badTga[17] |= 64; Reject(badTga, "TGA interleave");
        Reject(Tga(pixels, 32, true, false)[..^1], "truncated TGA RLE");
        Reject(Tga(pixels, 32, false, false).Concat(new byte[] { 1 }).ToArray(), "unknown TGA trailing metadata");
        var footer = new byte[26]; "TRUEVISION-XFILE.\0"u8.CopyTo(footer.AsSpan(8));
        CheckInput(Save("footer", Tga(pixels, 32, false, false).Concat(footer).ToArray()), ImageFormat.Tga, pixels);
        footer[0] = 18; Reject(Tga(pixels, 32, false, false).Concat(footer).ToArray(), "unimplemented TGA extension area");

        string Save(string name, byte[] bytes) { var path = Path.Combine(directory, name + ".input"); File.WriteAllBytes(path, bytes); return path; }
        void CheckInput(string path, ImageFormat format, byte[] expected)
        {
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var preview = adapter.Preview(new(facts), default);
            var rgba = preview.BgraPixels.Chunk(4).SelectMany(p => new byte[] { p[2],p[1],p[0],p[3] }).ToArray();
            check(facts.Format == format && facts.Width == 3 && facts.Height == 2 && facts.UnsupportedReason is null && rgba.SequenceEqual(expected), "independent bitmap fixture: " + Path.GetFileName(path));
        }
        ImageBatchPlan Plan(ImageSourceFacts facts, ImageConversionOptions options) => ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
        string Execute(ImageSourceFacts facts, ImageConversionOptions options)
        {
            var item = Plan(facts, options).Confirm(true, false, false).Plan.Items[0];
            var folder = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $".context-suite-{facts.ItemId:N}.tmp"); File.WriteAllBytes(path, []);
            _ = adapter.Convert(new(item, options, path), default); return path;
        }
        void Reject(byte[] bytes, string reason)
        {
            try { adapter.Probe(new(Guid.NewGuid(), Save(Guid.NewGuid().ToString("N"), bytes)), default); }
            catch (Exception e) when (e is InvalidDataException or ImageFailureException) { check(true, "bitmap rejection: " + reason); return; }
            throw new InvalidOperationException("Accepted " + reason);
        }
    }

    private static byte[] Bmp(byte[] rgba, bool topDown)
    {
        var bytes = new byte[54 + 12 * 2]; bytes[0] = 66; bytes[1] = 77;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 54); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 3); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), topDown ? -2 : 2);
        bytes[26] = 1; bytes[28] = 24;
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
        { var source = ((topDown ? y : 1 - y) * 3 + x) * 4; var output = 54 + y * 12 + x * 3; bytes[output] = rgba[source + 2]; bytes[output + 1] = rgba[source + 1]; bytes[output + 2] = rgba[source]; }
        return bytes;
    }

    private static byte[] Tga(byte[] rgba, byte origin, bool rle, bool alpha)
    {
        using var stream = new MemoryStream(); var header = new byte[18]; header[2] = (byte)(rle ? 10 : 2); header[12] = 3; header[14] = 2;
        header[16] = (byte)(alpha ? 32 : 24); header[17] = (byte)(origin | (alpha ? 8 : 0)); stream.Write(header);
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
        {
            var index = (((origin & 32) != 0 ? y : 1 - y) * 3 + ((origin & 16) != 0 ? 2 - x : x)) * 4;
            if (rle) stream.WriteByte(128); // one-pixel run; boundary/overrun tests mutate this
            stream.WriteByte(rgba[index + 2]); stream.WriteByte(rgba[index + 1]); stream.WriteByte(rgba[index]); if (alpha) stream.WriteByte(rgba[index + 3]);
        }
        return stream.ToArray();
    }
}
