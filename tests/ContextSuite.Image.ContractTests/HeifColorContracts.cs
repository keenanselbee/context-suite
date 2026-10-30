using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class HeifColorContracts
{
    public static void Run(string root, string evidence)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a new absolute evidence directory.");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(evidence, "results.json")));
        if (!baseline.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Passing native color evidence required.");
        var checks = 0; var outputs = new List<object>();
        foreach (var row in baseline.RootElement.GetProperty("Rows").EnumerateArray())
        {
            var path = row.GetProperty("file").GetString()!;
            var original = Hash(path); var written = File.GetLastWriteTimeUtc(path);
            Check(original == row.GetProperty("sha256").GetString(), "Bound color fixture");
            if (row.GetProperty("hdr").GetBoolean())
            {
                try { adapter.Probe(new(Guid.NewGuid(), path), default); throw new InvalidOperationException("HDR admitted as SDR"); }
                catch (NotSupportedException) { checks++; }
                catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput) { checks++; }
                Check(Hash(path) == original && File.GetLastWriteTimeUtc(path) == written, "Refused original retained");
                continue;
            }
            var facts = adapter.Probe(new(Guid.NewGuid(), path), default);
            var info = row.GetProperty("info");
            Check(facts.Width == info.GetProperty("width").GetUInt32() && facts.Height == info.GetProperty("height").GetUInt32() &&
                facts.BitDepth == info.GetProperty("bits").GetUInt32(), "Color-only change preserves geometry and depth");
            if ((info.GetProperty("flags").GetUInt32() & 2048) != 0)
                Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic), new("convert", new(ReplaceOriginals: true)),
                    replaceOriginal: true).ReplaceOriginal, "Color fallback forces copy despite overwrite preference");
            var expected = File.ReadAllBytes(path + ".rgba");
            Check(Hash(path + ".rgba") == row.GetProperty("expectedPixelsSha256").GetString(), "Bound independent samples");
            foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg })
            {
                var options = new ImageConversionOptions(target, Metadata: ImageMetadataMode.Automatic);
                var batch = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
                var directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                var output = Path.Combine(directory, $".context-suite-{facts.ItemId:N}.tmp");
                using (File.Create(output)) { }
                var result = adapter.Convert(new(batch.Confirm(true, false, false).Plan.Items[0], options, output), default);
                Check(result.Validation.MatchesPlan, "Converted output validates");
                using var image = new MagickImage();
                image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true); image.Read(output);
                Check(image.Width == facts.Width && image.Height == facts.Height && image.GetColorProfile() is not null, "Output color and dimensions");
                if (row.TryGetProperty("expectedIccSha256", out var profile))
                    Check(Convert.ToHexString(SHA256.HashData(image.GetColorProfile()!.ToByteArray())) == profile.GetString(), "ICC-only output preserves exact profile");
                if (target == ImageFormat.Png)
                {
                    image.ColorType = ColorType.TrueColorAlpha;
                    var actual = image.GetPixels().ToArray()!;
                    Check(actual.Length * 2 == expected.Length && actual.Select((value, index) => value == BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(index * 2))).All(value => value),
                        "Exact PNG samples with missing or unspecified color");
                    Check(result.BitDepth == (facts.BitDepth > 8 ? 16 : 8), "PNG retains sample precision");
                }
                else
                {
                    using var samples = new MagickImage();
                    samples.ReadPixels(expected, new PixelReadSettings(facts.Width, facts.Height, StorageType.Short, PixelMapping.RGBA));
                    samples.ColorSpace = ColorSpace.sRGB; samples.SetProfile(image.GetColorProfile()!);
                    samples.Alpha(AlphaOption.Remove); samples.Depth = 8; samples.Quality = 90;
                    using var encoded = new MemoryStream(); samples.Write(encoded, MagickFormat.Jpeg);
                    using var oracle = new MagickImage(encoded.ToArray());
                    Check(image.GetPixels().ToByteArray(PixelMapping.RGB)!.SequenceEqual(oracle.GetPixels().ToByteArray(PixelMapping.RGB)!),
                        "JPEG matches encoding of independent samples");
                }
                outputs.Add(new { Source = path, Target = target.ToString(), Output = output });
            }
            Check(Hash(path) == original && File.GetLastWriteTimeUtc(path) == written, "Converted original retained");
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Outputs = outputs }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks} HEIF color conversion checks; {outputs.Count} outputs.");
        void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); checks++; }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
