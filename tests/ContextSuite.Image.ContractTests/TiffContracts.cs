using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Tiff;
using ContextSuite.Private.Pdf;
using ContextSuite.Private.Tiff;
using ImageMagick;

internal static class TiffContracts
{
    public static async Task RunAsync(string root, string fixtures, string validatorDirectory, Action<bool, string> check)
    {
        using var validator = new ImagePdfValidator(validatorDirectory, Path.Combine(root, "validator"));
        var adapter = new TiffAdapter(Path.Combine(root, "scratch"), validator);
        foreach (var input in Directory.GetFiles(fixtures, "*.tif").Order())
        {
            var name = Path.GetFileNameWithoutExtension(input);
            var original = SHA256.HashData(File.ReadAllBytes(input));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            var count = name == "orientation-1-to-8" ? 8 : name == "icc-bgra" ? 1 : 2;
            check(facts.Pages.Length == count && facts.Pages.All(p => p.UnsupportedReason is null), name + ": complete supported page inventory");
            foreach (var page in facts.Pages)
            {
                var orientation = name == "orientation-1-to-8" ? page.Index + 1 : page.Index == 0 ? 1 : 6;
                var kind = name == "orientation-1-to-8" ? "bgr24" : name == "icc-bgra" ? "bgra32" : name.Split('-')[0];
                var seed = name == "orientation-1-to-8" ? orientation % 2 : page.Index;
                check(page.Orientation == orientation && page.HasTransparency == (kind == "bgra32"), name + ": orientation and alpha facts " + page.Index);
                foreach (var target in new[] { TiffTarget.Png, TiffTarget.Jpeg })
                {
                    var output = await Execute(facts, target, page.Index, target == TiffTarget.Jpeg ? 0xffffffu : null);
                    using var decoded = new MagickImage(output);
                    check(decoded.Width == page.OutputWidth && decoded.Height == page.OutputHeight &&
                        decoded.Format == (target == TiffTarget.Png ? MagickFormat.Png : MagickFormat.Jpeg), name + ": " + target + " dimensions and format " + page.Index);
                    if (target == TiffTarget.Png && name != "icc-bgra")
                        check(decoded.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(Expected(kind, seed, orientation)), name + ": exact independently oriented PNG pixels " + page.Index);
                    if (target == TiffTarget.Jpeg) check(decoded.IsOpaque, name + ": explicit JPEG matte is opaque " + page.Index);
                    var dx = orientation >= 5 ? 75 : 150; var dy = orientation >= 5 ? 150 : 75;
                    var density = decoded.Density;
                    var factor = density.Units == DensityUnit.PixelsPerCentimeter ? 2.54 : 1;
                    check(Math.Abs(density.X * factor - dx) < .03 && Math.Abs(density.Y * factor - dy) < .03,
                        name + ": oriented physical resolution " + target + " " + page.Index);
                }
                if (page.HasTransparency)
                {
                    await Reject(() => Execute(facts, TiffTarget.Jpeg, page.Index, null), name + ": missing matte refused");
                    var preview = adapter.Preview(new(facts, page.Index, 0xffffff), default);
                    check(preview.Width == page.OutputWidth && preview.Height == page.OutputHeight, name + ": matte preview dimensions");
                }
            }
            var pdf = await Execute(facts, TiffTarget.Pdf, -1, null);
            check(File.ReadAllBytes(pdf).AsSpan(0, 5).SequenceEqual("%PDF-"u8), name + ": ordered complete PDF independently validated by qpdf");
            if (name != "icc-bgra")
            {
                var expectedSamples = facts.Pages.Select(page =>
                {
                    var orientation = name == "orientation-1-to-8" ? page.Index + 1 : page.Index == 0 ? 1 : 6;
                    var kind = name == "orientation-1-to-8" ? "bgr24" : name.Split('-')[0];
                    var pixels = Expected(kind, name == "orientation-1-to-8" ? orientation % 2 : page.Index, orientation).Chunk(4).ToArray();
                    return new ImagePdfSamples(page.Index,
                        Convert.ToHexString(SHA256.HashData(pixels.SelectMany(p => new[] { p[2], p[1], p[0] }).ToArray())),
                        kind == "bgra32" ? Convert.ToHexString(SHA256.HashData(pixels.Select(p => p[3]).ToArray())) : null,
                        Convert.ToHexString(SHA256.HashData(ColorProfiles.SRGB.ToByteArray())), ColorProfiles.SRGB.ToByteArray().Length);
                }).ToImmutableArray();
                // qpdf parses the completed PDF again against the independent
                // fixture formula, not the production writer's sample inventory.
                var geometries = facts.Pages.Select(page => new RasterPdfPage(page.OutputWidth, page.OutputHeight, 8, 3,
                    page.OutputWidth * 72d / (page.Orientation >= 5 ? 75 : 150),
                    page.OutputHeight * 72d / (page.Orientation >= 5 ? 150 : 75), page.HasTransparency)).ToArray();
                _ = await validator.ValidateAsync(new(File.ReadAllBytes(pdf), expectedSamples), geometries, default);
                check(true, name + ": PDF page order, physical size, pixels, alpha and sRGB match independent fixture oracle");
            }
            check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(original), name + ": source bytes unchanged");
        }

        foreach (var little in new[] { true, false })
        foreach (var variant in new[] { "rgb", "associated", "unassociated", "white-gray", "black-gray", "white-bilevel", "black-bilevel", "reverse-bilevel" })
        {
            var bilevel = variant.EndsWith("bilevel", StringComparison.Ordinal);
            var gray = bilevel || variant.EndsWith("gray", StringComparison.Ordinal);
            var alpha = variant is "associated" or "unassociated";
            var pixels = new List<byte>(); var expected = new List<byte>();
            for (var y = 0; y < 4; y++)
            {
                if (bilevel) pixels.Add(variant == "reverse-bilevel" ? (byte)0xaa : (byte)0x55);
                for (var x = 0; x < 8; x++)
                {
                    if (gray)
                    {
                        var value = bilevel ? (byte)(x % 2 * 255) : (byte)(x * 24 + y * 12);
                        if (!bilevel) pixels.Add(value);
                        if (variant.StartsWith("white", StringComparison.Ordinal)) value = (byte)(255 - value);
                        expected.AddRange([value, value, value, 255]);
                    }
                    else
                    {
                        byte r = (byte)(x * 24), g = (byte)(y * 60), b = 90;
                        pixels.AddRange(variant == "associated" ? [(byte)(r / 3), (byte)(g / 3), (byte)(b / 3)] : [r, g, b]);
                        if (alpha) pixels.Add(85);
                        expected.AddRange([b, g, r, alpha ? (byte)85 : (byte)255]);
                    }
                }
            }
            var input = Path.Combine(root, $"raw-{little}-{variant}.tif");
            File.WriteAllBytes(input, TiffRawFixture.Create(little, gray ? variant.StartsWith("white", StringComparison.Ordinal) ? (ushort)0 : (ushort)1 : (ushort)2,
                bilevel ? (ushort)1 : (ushort)8, gray ? (ushort)1 : alpha ? (ushort)4 : (ushort)3,
                variant == "associated" ? (ushort)1 : (ushort)2, variant == "reverse-bilevel" ? (ushort)2 : (ushort)1, pixels.ToArray()));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            check(facts.Pages[0].UnsupportedReason is null, $"TIFF raw {little}/{variant} supported");
            var output = await Execute(facts, TiffTarget.Png, 0, null);
            using var decoded = new MagickImage(output);
            check(decoded.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(expected), $"TIFF raw {little}/{variant} exact color and alpha");
        }

        foreach (var samples in new ushort[] { 1, 3, 4 })
        foreach (var compression in new ushort[] { 5, 8 })
        foreach (var little in new[] { true, false })
        {
            var kind = samples == 1 ? "gray8" : samples == 3 ? "bgr24" : "bgra32";
            var expected = Expected(kind, 0, 1);
            var pixels = expected.Chunk(4).SelectMany(pixel => samples == 1 ? [pixel[0]] : samples == 3 ?
                new[] { pixel[2], pixel[1], pixel[0] } : [pixel[2], pixel[1], pixel[0], pixel[3]]).ToArray();
            for (var y = 0; y < 4; y++)
            for (var at = 8 * samples - 1; at >= samples; at--)
                pixels[y * 8 * samples + at] = unchecked((byte)(pixels[y * 8 * samples + at] - pixels[y * 8 * samples + at - samples]));
            byte[] compressed;
            if (compression == 8)
            {
                using var encoded = new MemoryStream();
                using (var zip = new ZLibStream(encoded, CompressionLevel.SmallestSize, true)) zip.Write(pixels);
                compressed = encoded.ToArray();
            }
            else
            {
                // Legal TIFF LZW stream with literals only, kept below the
                // first code-width increase; independent of WIC's encoder.
                var codes = new[] { 256 }.Concat(pixels.Select(b => (int)b)).Append(257).ToArray();
                compressed = new byte[(codes.Length * 9 + 7) / 8];
                var bit = 0;
                foreach (var code in codes)
                for (var shift = 8; shift >= 0; shift--, bit++)
                    if ((code & (1 << shift)) != 0) compressed[bit / 8] |= (byte)(1 << (7 - bit % 8));
            }
            var input = Path.Combine(root, $"predictor-{little}-{samples}-{compression}.tif");
            File.WriteAllBytes(input, TiffRawFixture.Create(little, samples == 1 ? (ushort)1 : (ushort)2, 8, samples, 2, 1,
                compressed, compression: compression, predictor: 2));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            check(facts.Pages[0].UnsupportedReason is null, "TIFF horizontal predictor admitted " + Path.GetFileName(input));
            using var decoded = new MagickImage(await Execute(facts, TiffTarget.Png, 0, null));
            check(decoded.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(expected),
                "TIFF horizontal predictor exact independently authored pixels " + Path.GetFileName(input));
        }

        foreach (var little in new[] { true, false })
        {
            var expected = Expected("bgra32", 0, 1);
            var pixels = expected.Chunk(4).SelectMany(p => new[] { p[2], p[1], p[0], p[3] }).ToArray();
            var input = Path.Combine(root, $"multi-strip-{little}.tif");
            File.WriteAllBytes(input, TiffRawFixture.Create(little, 2, 8, 4, 2, 1, pixels, rowsPerStrip: 2));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            using var decoded = new MagickImage(await Execute(facts, TiffTarget.Png, 0, null));
            check(decoded.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(expected), "TIFF multiple strips retain exact RGBA in byte order " + little);
        }

        foreach (var gray in new[] { false, true })
        {
            var profile = gray ? PngFixture.GrayProfile(ColorProfiles.SRGB.ToByteArray()) : ColorProfiles.AdobeRGB1998.ToByteArray();
            var pixels = gray ? Enumerable.Range(0, 32).Select(i => (byte)(i * 7)).ToArray() :
                Enumerable.Range(0, 32).SelectMany(i => new byte[] { (byte)(i * 7), 90, 180 }).ToArray();
            var input = Path.Combine(root, $"profile-{gray}.tif");
            File.WriteAllBytes(input, TiffRawFixture.Create(true, gray ? (ushort)1 : (ushort)2, 8, gray ? (ushort)1 : (ushort)3, 0, 1, pixels, profile));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            check(facts.Pages[0].IccSha256 == Convert.ToHexString(SHA256.HashData(profile)) && facts.Pages[0].UnsupportedReason is null,
                "TIFF binds supported " + (gray ? "gray" : "RGB") + " ICC profile");
            using var expected = new MagickImage();
            expected.ReadPixels(pixels, new PixelReadSettings(8, 4, StorageType.Char, gray ? "I" : "RGB"));
            expected.ColorSpace = gray ? ColorSpace.Gray : ColorSpace.sRGB;
            expected.TransformColorSpace(new ColorProfile(profile), ColorProfiles.SRGB, ColorTransformMode.HighRes); expected.Depth = 8;
            using var decoded = new MagickImage(await Execute(facts, TiffTarget.Png, 0, null));
            check(decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(expected.GetPixels().ToByteArray(PixelMapping.RGBA)!),
                "TIFF ICC tone/color transformed to sRGB before quantization");
            var pdf = await Execute(facts, TiffTarget.Pdf, -1, null);
            var pdfSamples = new ImagePdfSamples(0, Convert.ToHexString(SHA256.HashData(expected.GetPixels().ToByteArray(PixelMapping.RGB)!)), null,
                Convert.ToHexString(SHA256.HashData(ColorProfiles.SRGB.ToByteArray())), ColorProfiles.SRGB.ToByteArray().Length);
            _ = await validator.ValidateAsync(new(File.ReadAllBytes(pdf), [pdfSamples]), [new RasterPdfPage(8, 4, 8, 3, 3.84, 3.84, false)], default);
            check(true, "TIFF ICC PDF samples match independently transformed color oracle");
        }

        var mattePath = Path.Combine(root, "matte-solid.tif");
        File.WriteAllBytes(mattePath, TiffRawFixture.Create(true, 2, 8, 4, 2, 1,
            Enumerable.Range(0, 32).SelectMany(_ => new byte[] { 36, 90, 180, 85 }).ToArray()));
        var matteFacts = adapter.Probe(new(Guid.NewGuid(), mattePath), default);
        foreach (var matte in new uint[] { 0, 0xffffff, 0x00ff00 })
        {
            using var decoded = new MagickImage(await Execute(matteFacts, TiffTarget.Jpeg, 0, matte));
            var expected = new[] { 12 + (int)((matte >> 16) & 255) * 2 / 3,
                30 + (int)((matte >> 8) & 255) * 2 / 3, 60 + (int)(matte & 255) * 2 / 3 };
            check(decoded.GetPixels().ToByteArray(PixelMapping.RGB)!.Select((value, index) => Math.Abs(value - expected[index % 3])).Max() <= 2,
                "TIFF JPEG samples match explicit matte blend within two sample levels " + matte);
        }

        foreach (var unit in new ushort[] { 1, 3 })
        {
            var pixels = Enumerable.Repeat((byte)120, 96).ToArray();
            var input = Path.Combine(root, $"resolution-unit-{unit}.tif");
            File.WriteAllBytes(input, TiffRawFixture.Create(true, 2, 8, 3, 0, 1, pixels, resolutionUnit: unit));
            var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
            foreach (var target in new[] { TiffTarget.Png, TiffTarget.Jpeg })
            {
                var output = await Execute(facts, target, 0, null);
                var imageAdapter = new ContextSuite.Private.Images.ImageAdapter(Path.Combine(root, "scratch"));
                var density = imageAdapter.Probe(new(Guid.NewGuid(), output), default).Resolution;
                check(density is not null && density.Unit == unit && (double)density.X / density.Divisor == 150 &&
                    (double)density.Y / density.Divisor == 75, "TIFF unitless/centimeter density retained in " + target + "/" + unit);
            }
            var pdf = await Execute(facts, TiffTarget.Pdf, -1, null);
            var sample = new ImagePdfSamples(0, Convert.ToHexString(SHA256.HashData(pixels)), null,
                Convert.ToHexString(SHA256.HashData(ColorProfiles.SRGB.ToByteArray())), ColorProfiles.SRGB.ToByteArray().Length);
            var side = unit == 1 ? 6d : 3.84 / 2.54;
            _ = await validator.ValidateAsync(new(File.ReadAllBytes(pdf), [sample]), [new RasterPdfPage(8, 4, 8, 3, side, side, false)], default);
            check(true, "TIFF unitless/centimeter PDF physical size matches independent policy calculation " + unit);
        }

        var raw = File.ReadAllBytes(Path.Combine(fixtures, "bw1-lzw.tif"));
        var partial = raw.ToArray(); SetTag(partial, 1, 259, 65000);
        var partialPath = Path.Combine(root, "unsupported-second-page.tif"); File.WriteAllBytes(partialPath, partial);
        var partialFacts = adapter.Probe(new(Guid.NewGuid(), partialPath), default);
        check(partialFacts.Pages[0].UnsupportedReason is null && partialFacts.Pages[1].UnsupportedReason is not null,
            "TIFF preserves page-specific unsupported compression");
        _ = await Execute(partialFacts, TiffTarget.Png, 0, null);
        await Reject(() => Execute(partialFacts, TiffTarget.Pdf, -1, null), "partial document cannot publish incomplete PDF");
        var oversized = raw.ToArray(); SetTag(oversized, 0, 256, 16385);
        await RejectProbe(oversized, "oversized dimensions before codec");
        var badOrientation = raw.ToArray(); SetTag(badOrientation, 0, 274, 9);
        await RejectProbe(badOrientation, "invalid orientation before codec");
        var sixteen = TiffRawFixture.Create(true, 1, 16, 1, 0, 1, new byte[64]);
        var sixteenPath = Path.Combine(root, "precision16.tif"); File.WriteAllBytes(sixteenPath, sixteen);
        check(adapter.Probe(new(Guid.NewGuid(), sixteenPath), default).Pages[0].UnsupportedReason is not null,
            "TIFF 16-bit precision explicitly unsupported without quantization");
        var badProfile = ColorProfiles.SRGB.ToByteArray(); "CMYK"u8.CopyTo(badProfile.AsSpan(16));
        var badProfilePath = Path.Combine(root, "unsupported-profile.tif");
        File.WriteAllBytes(badProfilePath, TiffRawFixture.Create(true, 2, 8, 3, 0, 1, new byte[96], badProfile));
        check(adapter.Probe(new(Guid.NewGuid(), badProfilePath), default).Pages[0].UnsupportedReason is not null,
            "TIFF unsupported ICC is a page-specific refusal");
        foreach (var length in new[] { raw.Length - 1, raw.Length / 2, 16 })
            await RejectProbe(raw[..length], "truncated directory or strip " + length);
        var cycle = raw.ToArray();
        var first = (int)BinaryPrimitives.ReadUInt32LittleEndian(cycle.AsSpan(4));
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(cycle.AsSpan(first));
        BinaryPrimitives.WriteUInt32LittleEndian(cycle.AsSpan(first + 2 + entries * 12), (uint)first);
        await RejectProbe(cycle, "cyclic page chain");
        var big = raw.ToArray(); big[2] = 43;
        await RejectProbe(big, "BigTIFF boundary");
        var changedPath = Path.Combine(root, "changed.tif"); File.WriteAllBytes(changedPath, raw);
        var changedFacts = adapter.Probe(new(Guid.NewGuid(), changedPath), default);
        File.AppendAllText(changedPath, "changed");
        await Reject(() => Execute(changedFacts, TiffTarget.Png, 0, null), "changed source refused");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Reject(() => Task.Run(() => adapter.Probe(new(Guid.NewGuid(), changedPath), cancelled.Token)), "pre-cancelled TIFF probe");

        var safe = adapter.Probe(new(Guid.NewGuid(), Path.Combine(fixtures, "bw1-lzw.tif")), default);
        var occupiedId = Guid.NewGuid(); var occupied = Path.Combine(root, $".context-suite-{occupiedId:N}.tmp");
        File.WriteAllText(occupied, "existing");
        await Reject(() => adapter.ConvertAsync(new(safe, new(TiffTarget.Png), 0, occupiedId, occupied), default), "nonempty reserved output");
        check(File.ReadAllText(occupied) == "existing", "TIFF refuses to overwrite an occupied reservation");

        async Task<string> Execute(TiffSourceFacts source, TiffTarget target, int page, uint? matte)
        {
            var id = Guid.NewGuid(); var output = Path.Combine(root, $".context-suite-{id:N}.tmp");
            using (File.Create(output)) { }
            var result = await adapter.ConvertAsync(new(source, new(target, matte), page, id, output), default);
            check(result.SourceId == source.ItemId && result.PageIndex == page && result.PageCount == (target == TiffTarget.Pdf ? source.Pages.Length : 1) &&
                result.OutputBytes == new FileInfo(output).Length, "TIFF validated reply binds output and ordered source pages");
            return output;
        }
        async Task RejectProbe(byte[] bytes, string label)
        {
            var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tif"); File.WriteAllBytes(path, bytes);
            await Reject(() => Task.Run(() => adapter.Probe(new(Guid.NewGuid(), path), default)), label);
        }
        async Task Reject(Func<Task> action, string label)
        {
            var rejected = false;
            try { await action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or OperationCanceledException or ContextSuite.Core.Images.ImageFailureException) { rejected = true; }
            check(rejected, "TIFF rejects " + label);
        }
    }

    private static void SetTag(byte[] bytes, int page, ushort tag, uint value)
    {
        var at = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        for (var i = 0; ; i++)
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            if (i != page) { at = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 2 + count * 12)); continue; }
            for (var item = 0; item < count; item++)
            {
                var entry = at + 2 + item * 12;
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry)) != tag) continue;
                var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 2));
                if (type == 3) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry + 8), (ushort)value);
                else if (type == 4) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), value);
                else throw new InvalidDataException("Fixture mutation needs a scalar integer.");
                return;
            }
            throw new InvalidDataException("Fixture tag not found.");
        }
    }

    // Independently authored fixture formula and EXIF coordinate mapping; no
    // TIFF decoder or production orientation helper participates in this oracle.
    private static byte[] Expected(string kind, int seed, int orientation)
    {
        var width = orientation >= 5 ? 4 : 8; var height = orientation >= 5 ? 8 : 4;
        var result = new byte[width * height * 4];
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 8; x++)
        {
            var (dx, dy) = orientation switch
            {
                1 => (x, y), 2 => (7 - x, y), 3 => (7 - x, 3 - y), 4 => (x, 3 - y),
                5 => (y, x), 6 => (3 - y, x), 7 => (3 - y, 7 - x), 8 => (y, 7 - x),
                _ => throw new InvalidDataException()
            };
            var at = (dy * width + dx) * 4;
            if (kind is "bw1" or "gray8")
            {
                var gray = kind == "bw1" ? ((x + y + seed) % 2) * 255 : x * 23 + y * 17 + seed * 7;
                result[at] = result[at + 1] = result[at + 2] = (byte)gray;
            }
            else
            {
                result[at] = (byte)(x * 20 + seed * 7); result[at + 1] = (byte)(y * 50 + seed * 7);
                result[at + 2] = (byte)(x * 10 + y * 15 + seed * 7);
            }
            result[at + 3] = kind == "bgra32" && (x + y + seed) % 2 != 0 ? (byte)80 : (byte)255;
        }
        return result;
    }
}
