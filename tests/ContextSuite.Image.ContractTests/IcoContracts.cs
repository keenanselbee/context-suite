using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class IcoContracts
{
    public static void Run(string root, Action<bool, string> check)
    {
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var options = new ImageConversionOptions(ImageFormat.Ico, Metadata: ImageMetadataMode.Automatic);
        var outputs = new List<string>();
        foreach (var (width, height) in new (uint, uint)[] { (1, 1), (3, 2), (20, 10), (32, 32), (128, 64), (512, 256), (256, 512) })
        {
            var path = Path.Combine(root, $"solid-{width}-{height}.png");
            File.WriteAllBytes(path, PngFixture.Create(width, height, 8, 6, (_, _) => [65535, 0, 0, 32896]));
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var before = SHA256.HashData(File.ReadAllBytes(path));
            var encoded = Execute(facts, options);
            var frames = ReadFrames(encoded);
            var layout = IconFrameLayout.Create(width, height);
            check(frames.Count == layout.Length && frames.Select(f => f.Size).SequenceEqual(layout.Select(f => f.Size)),
                $"ICO {width}x{height}: standard sizes capped by source");
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i]; var fit = layout[i];
                var expected = new byte[frame.Bgra.Length];
                for (var y = fit.Top; y < fit.Top + fit.Height; y++)
                for (var x = fit.Left; x < fit.Left + fit.Width; x++)
                { expected[(y * frame.Size + x) * 4 + 2] = 255; expected[(y * frame.Size + x) * 4 + 3] = 128; }
                check(frame.Bgra.SequenceEqual(expected), $"ICO {width}x{height}/{frame.Size}: exact sRGB, alpha, aspect fit and centered padding");
            }
            var preview = adapter.Preview(new(facts, options), default);
            check(preview.Width == frames[^1].Size && preview.Height == frames[^1].Size && preview.BgraPixels.SequenceEqual(frames[^1].Bgra),
                "ICO preview equals the encoded largest frame");
            check(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(before), "ICO original bytes retained");
        }

        var basePath = Path.Combine(root, "pixels.png");
        var edgePath = Path.Combine(root, "alpha-edges.png");
        File.WriteAllBytes(edgePath, PngFixture.Create(512, 512, 8, 6,
            (x, y) => x is >= 128 and < 384 && y is >= 128 and < 384 ? [65535, 0, 0, 65535] : [0, 0, 65535, 0]));
        var edgeFacts = adapter.Probe(new(Guid.NewGuid(), edgePath), default);
        foreach (var frame in ReadFrames(Execute(edgeFacts, options)))
        {
            var center = (int)((frame.Size / 2 * frame.Size + frame.Size / 2) * 4);
            check(frame.Bgra.AsSpan(center, 4).SequenceEqual(new byte[] { 0, 0, 255, 255 }) && frame.Bgra[3] == 0,
                "ICO downsize retains the solid center and transparent exterior: " + frame.Size);
            var partial = frame.Bgra.Chunk(4).Where(p => p[3] is > 16 and < 240).ToArray();
            check(partial.Length > 0 && partial.All(p => p[0] <= 2 && p[1] <= 2 && p[2] >= 253),
                "ICO filtered alpha edge has no hidden-blue or dark halo: " + frame.Size);
        }
        File.WriteAllBytes(basePath, PngFixture.Create(3, 2, 8, 6,
            (x, y) => [(ushort)(x * 257 * 90), (ushort)(y * 257 * 170), 65535, (ushort)((x + y) * 257 * 80)]));
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Bmp3, MagickFormat.Tga })
        {
            var path = Path.Combine(root, "input-" + format);
            using (var image = new MagickImage(basePath))
            {
                if (format is MagickFormat.Jpeg or MagickFormat.Bmp3) { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaOption.Remove); }
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
                image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
                image.Write(path, format);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var frame = ReadFrames(Execute(facts, options))[0];
            using var source = new MagickImage(path, format);
            var expected = source.GetPixels().ToByteArray(PixelMapping.BGRA)!;
            check(ExtractArtwork(frame, 3, 2).SequenceEqual(expected), "ICO admitted source pixels retained: " + format);
        }
        foreach (var orientation in Enumerable.Range(1, 8))
        {
            var path = Path.Combine(root, $"orientation-{orientation}.png");
            using (var image = new MagickImage(basePath))
            {
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)orientation);
                image.SetProfile(exif); image.Orientation = (OrientationType)orientation;
                image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
                image.Write(path, MagickFormat.Png);
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            using var expected = new MagickImage(path); expected.AutoOrient();
            var frame = ReadFrames(Execute(facts, options))[0];
            check(ExtractArtwork(frame, expected.Width, expected.Height).SequenceEqual(expected.GetPixels().ToByteArray(PixelMapping.BGRA)!),
                "ICO orientation applied once: " + orientation);
        }
        var profilePath = Path.Combine(root, "profile-16.png");
        File.WriteAllBytes(profilePath, PngFixture.Create(3, 2, 16, 6, (_, _) => [32000, 17000, 9000, 32896]));
        using (var image = new MagickImage(profilePath))
        { image.SetProfile(ColorProfiles.AdobeRGB1998); image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true); image.Write(profilePath, MagickFormat.Png); }
        var profiled = adapter.Probe(new(Guid.NewGuid(), profilePath), default);
        var profileFrame = ReadFrames(Execute(profiled, options))[0];
        using (var image = new MagickImage(profilePath))
        {
            image.TransformColorSpace(ColorProfiles.AdobeRGB1998, ColorProfiles.SRGB, ColorTransformMode.HighRes);
            image.Depth = 8;
            check(ExtractArtwork(profileFrame, 3, 2).SequenceEqual(image.GetPixels().ToByteArray(PixelMapping.BGRA)!),
                "ICO transforms tagged 16-bit color to sRGB before eight-bit output");
        }
        var changed = adapter.Probe(new(Guid.NewGuid(), basePath), default);
        File.AppendAllText(basePath, "changed");
        Reject(() => Execute(changed, options), "changed source");
        var preserved = adapter.Probe(new(Guid.NewGuid(), profilePath), default);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        RejectCancellation(() => adapter.Preview(new(preserved, options), cancelled.Token));

        // Corruptions are independent of the writer; the production output reader
        // must reject bad directory geometry, masks, pixels, offsets and tails.
        var good = File.ReadAllBytes(outputs[1]);
        var golden = ReadFrames(good);
        foreach (var at in new[] { 0, 2, 4, 6, 8, 10, 12, 14, 18, 22, 26, 30, 34, 36, 38, 42, good.Length - 1 })
        {
            var corrupted = (byte[])good.Clone(); corrupted[at] ^= 1;
            Reject(() => IcoOutput.Validate(corrupted, golden, default), "ICO output corruption at " + at);
        }
        Reject(() => IcoOutput.Validate(good[..^1], golden, default), "truncated ICO");
        Reject(() => IcoOutput.Validate(good.Concat(new byte[] { 0 }).ToArray(), golden, default), "unaccounted ICO tail");
        File.WriteAllText(Path.Combine(root, "outputs.json"), JsonSerializer.Serialize(outputs, new JsonSerializerOptions { WriteIndented = true }));

        byte[] Execute(ImageSourceFacts facts, ImageConversionOptions selected)
        {
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], selected, new("convert", new())).Items[0];
            var directory = Path.Combine(root, "out-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $".context-suite-{facts.ItemId:N}.tmp"); File.WriteAllBytes(path, []);
            var result = adapter.Convert(new(plan, selected, path), default);
            var bytes = File.ReadAllBytes(path);
            check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)) &&
                result.Width == plan.OutputWidth && result.Height == plan.OutputHeight && result.OutputBytes == bytes.Length,
                "ICO result binds exact validated bytes and planned dimensions");
            outputs.Add(path); return bytes;
        }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException)
            { check(true, "ICO refuses " + reason); return; }
            throw new InvalidOperationException("ICO accepted " + reason);
        }
        void RejectCancellation(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { check(true, "ICO pre-cancelled request stops"); return; }
            throw new InvalidOperationException("ICO ignored cancellation");
        }
    }

    private static byte[] ExtractArtwork(IconPixels frame, uint width, uint height)
    {
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            frame.Bgra.AsSpan((int)(((y + (frame.Size - height) / 2) * frame.Size + (frame.Size - width) / 2) * 4), (int)(width * 4))
                .CopyTo(result.AsSpan((int)(y * width * 4)));
        return result;
    }

    private static List<IconPixels> ReadFrames(byte[] bytes)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        var frames = new List<IconPixels>();
        for (var index = 0; index < count; index++)
        {
            var entry = 6 + index * 16;
            uint size = bytes[entry] == 0 ? 256u : bytes[entry];
            var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12)));
            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
                bytes.AsSpan(offset + 40 + (int)((size - y - 1) * size * 4), (int)(size * 4))
                    .CopyTo(pixels.AsSpan((int)(y * size * 4)));
            frames.Add(new(size, pixels));
        }
        return frames;
    }
}
