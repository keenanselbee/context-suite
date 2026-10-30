using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Settings;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class IcoInputContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(root, "engine-scratch"));
        foreach (var depth in new[] { 1, 4, 8, 24, 32 })
        {
            var frame = IcoInput.Read(Icon((2, 2, depth, Dib(depth))), default);
            check(frame.Width == 2 && frame.Height == 2 && frame.Bgra.Length == 16,
                $"ICO {depth}-bit DIB dimensions and pixel count");
            check(frame.Bgra.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 255, depth == 32 ? (byte)128 : (byte)255 }),
                $"ICO {depth}-bit DIB top-left color and alpha");
            check(frame.Bgra[7] == (depth == 32 ? 255 : 0), $"ICO {depth}-bit uses alpha plane when present, otherwise the AND mask");
            check(frame.Bgra.AsSpan(8, 4).SequenceEqual(new byte[] { depth == 1 ? (byte)0 : (byte)255, 0, 0, 255 }),
                $"ICO {depth}-bit DIB bottom-left color and bottom-up rows");
        }

        var legacy32 = Dib(32);
        for (var offset = 43; offset < 56; offset += 4) legacy32[offset] = 0;
        var legacyFrame = IcoInput.Read(Icon((2, 2, 32, legacy32)), default);
        check(legacyFrame.Bgra[3] == 255 && legacyFrame.Bgra[7] == 0,
            "ICO all-zero 32-bit alpha falls back to legacy opacity and AND mask");
        var invert = Dib(24);
        invert[40 + 8 + 3] = 255;
        Refuses(Icon((2, 2, 24, invert)), "background-dependent XOR inversion cannot be preserved in PNG");
        var png = PngFixture.Create(2, 2, 8, 6, (x, y) => x == 0 && y == 0
            ? [65535, 0, 0, 32896] : [0, 0, 65535, 65535]);
        var pngFrame = IcoInput.Read(Icon((2, 2, 32, png)), default);
        check(pngFrame.Bgra.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 255, 128 }),
            "ICO embedded PNG retains straight alpha");
        byte[] taggedPng;
        using (var tagged = new MagickImage(png))
        {
            tagged.SetProfile(ColorProfiles.AdobeRGB1998);
            tagged.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            tagged.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            using var taggedStream = new MemoryStream();
            tagged.Write(taggedStream, MagickFormat.Png);
            taggedPng = taggedStream.ToArray();
        }
        var taggedFrame = IcoInput.Read(Icon((2, 2, 32, taggedPng)), default);
        using (var expectedColor = new MagickImage(taggedPng))
        {
            expectedColor.TransformColorSpace(ColorProfiles.AdobeRGB1998, ColorProfiles.SRGB, ColorTransformMode.HighRes);
            expectedColor.Depth = 8;
            check(taggedFrame.HadColorProfile && taggedFrame.Bgra.SequenceEqual(expectedColor.GetPixels().ToByteArray(PixelMapping.BGRA)!),
                "ICO tagged PNG converts ICC colors to eight-bit sRGB without losing alpha");
        }
        var taggedPath = Path.Combine(root, "tagged.ico");
        File.WriteAllBytes(taggedPath, Icon((2, 2, 32, taggedPng)));
        var taggedFacts = adapter.Probe(new(Guid.NewGuid(), taggedPath), default);
        var taggedOptions = new ImageConversionOptions(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic);
        var taggedPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [taggedFacts], taggedOptions,
            new BatchSettings("convert", new())).Items[0];
        check(taggedFacts.ProfileNames.Contains("icc") && taggedFacts.HasOtherMetadata && taggedPlan.CanExecute &&
            taggedPlan.Warnings.Any(warning => warning.Code == "metadata-removal"),
            "ICO ICC normalization is visible in source facts and automatic plan");
        var preservePlan = ImageConversionPlanner.Create(Guid.NewGuid(), [taggedFacts],
            taggedOptions with { Metadata = ImageMetadataMode.Preserve }, new BatchSettings("convert", new())).Items[0];
        check(!preservePlan.CanExecute, "ICO ICC profile cannot be silently omitted under Preserve");
        var taggedOutput = Path.Combine(root, $".context-suite-{taggedFacts.ItemId:N}.tmp");
        File.WriteAllBytes(taggedOutput, []);
        _ = adapter.Convert(new(taggedPlan, taggedOptions, taggedOutput), default);
        using (var decodedColor = new MagickImage(File.ReadAllBytes(taggedOutput), MagickFormat.Png))
            check(decodedColor.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(taggedFrame.Bgra),
                "ICO tagged PNG conversion emits the normalized sRGB and alpha samples");
        var small = IcoInput.Read(Icon((1, 1, 32, Dib(32, 1, 1)), (2, 2, 24, Dib(24))), default);
        check(small.Width == 2 && small.Depth == 8, "ICO selects largest area before bit depth");
        var tied = IcoInput.Read(Icon((2, 2, 24, Dib(24)), (2, 2, 32, Dib(32))), default);
        check(tied.Bgra[3] == 128, "ICO breaks equal-area ties by bit depth");
        var inferredPng = IcoInput.Read(Icon((2, 2, 24, Dib(24)), (2, 2, 0, png)), default);
        check(inferredPng.Bgra[3] == 128, "ICO infers zero-declared PNG depth for equal-area selection");
        var first = IcoInput.Read(Icon((2, 2, 32, Dib(32)), (2, 2, 32, Dib(32, firstBlue: true))), default);
        check(first.Bgra[2] == 255, "ICO breaks full ties by original directory order");

        Refuses(Icon((2, 2, 32, Dib(32)))[..^1], "truncated selected mask");
        var badOffset = Icon((2, 2, 32, Dib(32)));
        BinaryPrimitives.WriteUInt32LittleEndian(badOffset.AsSpan(18), uint.MaxValue);
        Refuses(badOffset, "out-of-range directory offset");
        var wrongHeight = Icon((2, 2, 32, Dib(32)));
        BinaryPrimitives.WriteUInt32LittleEndian(wrongHeight.AsSpan(22 + 8), 2);
        Refuses(wrongHeight, "DIB combined height disagrees with directory");
        var unsupportedLargest = Icon((1, 1, 32, Dib(32, 1, 1)), (2, 2, 32, new byte[40]));
        Refuses(unsupportedLargest, "unsupported largest image cannot fall back to smaller entry");

        var input = Path.Combine(root, "source.ico");
        var original = Icon((2, 2, 32, Dib(32)));
        File.WriteAllBytes(input, original);
        var source = adapter.Probe(new(Guid.NewGuid(), input), default);
        check(source.Format == ImageFormat.Ico && source.Width == 2 && source.Height == 2 && source.HasTransparency,
            "ICO adapter probes selected pixels and transparency");
        var options = new ImageConversionOptions(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic);
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new BatchSettings("convert", new())).Items[0];
        check(plan.CanExecute && plan.OutputWidth == 2 && plan.OutputHeight == 2,
            "ICO to PNG plan keeps selected dimensions without enlargement");
        var preview = adapter.Preview(new(source, options), default);
        check(preview.Width == 2 && preview.Height == 2 && preview.BgraPixels.SequenceEqual(IcoInput.Read(original, default).Bgra),
            "ICO PNG preview retains selected pixels");
        var output = Path.Combine(root, $".context-suite-{source.ItemId:N}.tmp");
        File.WriteAllBytes(output, []);
        var result = adapter.Convert(new(plan, options, output), default);
        var encoded = File.ReadAllBytes(output);
        check(result.Width == 2 && result.Height == 2 && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(encoded)),
            "ICO PNG output is hash-bound and has selected dimensions");
        using (var decoded = new MagickImage(encoded, MagickFormat.Png))
            check(decoded.GetPixels().ToByteArray(PixelMapping.BGRA)!.SequenceEqual(IcoInput.Read(original, default).Bgra),
                "ICO PNG output preserves selected BGRA pixels and alpha");
        check(SHA256.HashData(File.ReadAllBytes(input)).SequenceEqual(SHA256.HashData(original)),
            "ICO conversion preserves source bytes");
        try
        {
            adapter.Convert(new(plan, options, output), new CancellationToken(true));
            check(false, "ICO canceled conversion leaves output untouched");
        }
        catch (OperationCanceledException)
        {
            check(File.ReadAllBytes(output).SequenceEqual(encoded), "ICO canceled conversion leaves output untouched");
        }
        File.WriteAllBytes(input, Icon((2, 2, 24, Dib(24))));
        try { adapter.Preview(new(source), default); check(false, "ICO changed source rejected"); }
        catch (ContextSuite.Core.Images.ImageFailureException)
        { check(true, "ICO changed source rejected"); }

        void Refuses(byte[] bytes, string description)
        {
            try { _ = IcoInput.Read(bytes, default); check(false, "ICO refuses " + description); }
            catch (Exception error) when (error is InvalidDataException or ContextSuite.Core.Images.ImageFailureException)
            { check(true, "ICO refuses " + description); }
        }
    }

    private static byte[] Icon(params (int Width, int Height, int Depth, byte[] Image)[] images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Length);
        var offset = 6 + images.Length * 16;
        foreach (var image in images)
        {
            writer.Write((byte)(image.Width == 256 ? 0 : image.Width));
            writer.Write((byte)(image.Height == 256 ? 0 : image.Height));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1);
            writer.Write((ushort)image.Depth); writer.Write(image.Image.Length); writer.Write(offset);
            offset += image.Image.Length;
        }
        foreach (var image in images) writer.Write(image.Image);
        return stream.ToArray();
    }

    private static byte[] Dib(int depth, int width = 2, int height = 2, bool firstBlue = false)
    {
        var colors = depth <= 8 ? 1 << depth : 0;
        var stride = (width * depth + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40); writer.Write(width); writer.Write(height * 2); writer.Write((ushort)1); writer.Write((ushort)depth);
        writer.Write(0); writer.Write(stride * height + maskStride * height); writer.Write(0); writer.Write(0);
        writer.Write(colors); writer.Write(0);
        for (var i = 0; i < colors; i++) writer.Write(i == 0 ? 0x00ff0000u : i == 1 && depth != 1 ? 0x000000ffu : 0u);
        for (var y = height - 1; y >= 0; y--)
        {
            var row = new byte[stride];
            if (y != 0)
            {
                if (depth == 1) row[0] = 0x80;
                else if (depth == 4) row[0] = 0x10;
                else if (depth == 8) row[0] = 1;
                else { row[0] = 255; if (depth == 32) row[3] = 255; }
            }
            else if (depth <= 8 && width > 1)
            {
                if (depth == 1) row[0] = 0x40;
                else if (depth == 4) row[0] = 0x02;
                else row[1] = 2;
            }
            else if (depth > 8)
            {
                row[2] = firstBlue ? (byte)0 : (byte)255;
                row[0] = firstBlue ? (byte)255 : (byte)0;
                if (depth == 32) { row[3] = 128; if (width > 1) row[7] = 255; }
            }
            writer.Write(row);
        }
        for (var y = height - 1; y >= 0; y--)
        {
            var mask = new byte[maskStride];
            if (y == 0 && width > 1) mask[0] = 0x40;
            writer.Write(mask);
        }
        return stream.ToArray();
    }
}
