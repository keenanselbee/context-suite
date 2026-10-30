using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Dds;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class DdsAdapterContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(scratch, "magick-scratch"));
        var source = Path.Combine(scratch, "input ü.dds");
        var pixels = new byte[64];
        for (var p = 0; p < 64; p += 4) { pixels[p] = 255; pixels[p + 3] = 255; }
        var dds = new byte[212];
        "DDS "u8.CopyTo(dds);
        foreach (var field in new (int Offset, uint Value)[] { (4, 124), (8, 0x1007), (12, 4), (16, 4), (76, 32),
            (80, 4), (84, 0x30315844), (108, 0x1000), (128, 29), (132, 3), (140, 1), (144, 1) })
            BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(field.Offset), field.Value);
        pixels.CopyTo(dds, 148); File.WriteAllBytes(source, dds);
        var facts = adapter.Probe(new(Guid.NewGuid(), source), default);
        check(facts.Texture is { Format: DdsFormat.Rgba8Srgb } && facts.Format == ImageFormat.Dds && !facts.HasTransparency,
            "DDS adapter probes actual pixels and independently validated header");
        var texture = new DdsConversionOptions(Format: DdsFormat.Bc7Srgb, Mips: DdsMipPolicy.Generate);
        var options = new ImageConversionOptions(ImageFormat.Dds, Texture: texture);
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
        check(plan.HasExecutableItems && plan.Items[0].Warnings.Any(w => w.Code == "dds-mips"), "DDS image plan requires acknowledged mip consequences");
        var output = Path.Combine(scratch, $".context-suite-{facts.ItemId:N}.tmp"); File.WriteAllBytes(output, []);
        var result = adapter.Convert(new(plan.Items[0], options, output), default);
        var encoded = File.ReadAllBytes(output);
        check(result.Validation.MatchesPlan && DdsParser.Parse(encoded, encoded.Length) is { Format: DdsFormat.Bc7Srgb, MipLevels: 3 },
            "DDS adapter encodes and validates only the reserved temporary output");
        var preview = adapter.Preview(new(facts, options), default);
        check(preview.Width == 4 && preview.Height == 4 && preview.BgraPixels[2] > 250, "DDS adapter preview decodes actual proposed compressed pixels");
        var generatedPath = Path.Combine(scratch, "with-mips.dds"); File.WriteAllBytes(generatedPath, encoded);
        var generatedFacts = adapter.Probe(new(Guid.NewGuid(), generatedPath), default);
        var export = new ImageConversionOptions(ImageFormat.Png, Texture: texture with { Format = DdsFormat.Rgba8Srgb, Mips = DdsMipPolicy.Remove }, ExtractMip: 1);
        var exportPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [generatedFacts], export, new("convert", new()));
        check(exportPlan.HasExecutableItems && exportPlan.Items[0].OutputWidth == 2, "DDS export explicitly plans selected mip dimensions");
        var pngTemp = Path.Combine(scratch, $".context-suite-{generatedFacts.ItemId:N}.tmp"); File.WriteAllBytes(pngTemp, []);
        adapter.Convert(new(exportPlan.Items[0], export, pngTemp), default);
        using (var image = new MagickImage(pngTemp))
            check(image.Format == MagickFormat.Png && image.Width == 2 && image.Height == 2, "DDS adapter exports the explicit mip to PNG");
        var png = Path.Combine(scratch, "ordinary.png");
        using (var image = new MagickImage())
        {
            image.ReadPixels(pixels, new PixelReadSettings(4, 4, StorageType.Char, PixelMapping.RGBA));
            image.ColorSpace = ColorSpace.sRGB; image.Write(png, MagickFormat.Png);
        }
        var pngFacts = adapter.Probe(new(Guid.NewGuid(), png), default);
        var fromPng = options with { Texture = texture with { SourceInterpretation = DdsInterpretation.Srgb }, Metadata = ImageMetadataMode.RemoveDescriptive };
        var pngPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [pngFacts], fromPng, new("convert", new()));
        check(pngPlan.HasExecutableItems, "PNG-to-DDS plan has explicit interpretation and metadata policy");
        var ddsTemp = Path.Combine(scratch, $".context-suite-{pngFacts.ItemId:N}.tmp"); File.WriteAllBytes(ddsTemp, []);
        adapter.Convert(new(pngPlan.Items[0], fromPng, ddsTemp), default);
        check(DdsParser.Parse(File.ReadAllBytes(ddsTemp), new FileInfo(ddsTemp).Length).Format == DdsFormat.Bc7Srgb, "ordinary PNG enters curated DDS conversion path");
        check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(dds)), "DDS adapter never changes original DDS bytes");
        var reservedFacts = facts with { ItemId = Guid.NewGuid() };
        var reservedPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [reservedFacts], options, new("convert", new())).Items[0];
        var occupied = Path.Combine(scratch, $".context-suite-{reservedFacts.ItemId:N}.tmp");
        byte[] sentinel = [1, 2, 3, 4]; File.WriteAllBytes(occupied, sentinel);
        try { adapter.Convert(new(reservedPlan, options, occupied), default); check(false, "DDS adapter rejects occupied temporary output"); }
        catch (InvalidDataException) { check(File.ReadAllBytes(occupied).SequenceEqual(sentinel), "DDS adapter rejects occupied temporary output without overwriting it"); }
        var stale = facts with { Sha256 = new string('0', 64) };
        var stalePlan = ImageConversionPlanner.Create(Guid.NewGuid(), [stale], options, new("convert", new())).Items[0];
        try { adapter.Convert(new(stalePlan, options, occupied), default); check(false, "DDS adapter rejects stale source"); }
        catch (ImageFailureException failure) { check(failure.Failure == ImageFailure.SourceChanged, "DDS adapter detects source changes before execution"); }
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Bmp, MagickFormat.Tga })
        {
            var input = Path.Combine(scratch, "pair-" + format + "." + format.ToString().ToLowerInvariant());
            using (var image = new MagickImage())
            {
                image.ReadPixels(pixels, new PixelReadSettings(4, 4, StorageType.Char, PixelMapping.RGBA));
                image.Depth = 8; image.ColorType = ColorType.TrueColor; image.Settings.ColorType = ColorType.TrueColor;
                image.HasAlpha = false; image.Settings.Compression = CompressionMethod.NoCompression;
                image.Write(input, format == MagickFormat.Bmp ? MagickFormat.Bmp3 : format);
            }
            var pairSource = adapter.Probe(new(Guid.NewGuid(), input), default);
            var pairPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [pairSource], fromPng, new("convert", new()));
            check(pairPlan.HasExecutableItems, "DDS pair plans ordinary " + format);
            var pairOutput = Path.Combine(scratch, $".context-suite-{pairSource.ItemId:N}.tmp"); File.WriteAllBytes(pairOutput, []);
            adapter.Convert(new(pairPlan.Items[0], fromPng, pairOutput), default);
            check(DdsParser.Parse(File.ReadAllBytes(pairOutput), new FileInfo(pairOutput).Length) is { Format: DdsFormat.Bc7Srgb, MipLevels: 3 },
                "DDS pair converts and validates ordinary " + format);
        }
    }
}
