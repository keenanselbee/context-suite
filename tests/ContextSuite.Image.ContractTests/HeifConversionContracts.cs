using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class HeifConversionContracts
{
    public static void Run(string root, string[] fixtureDirectories, string? bridgeEvidence = null)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a new absolute evidence directory.");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var checks = 0;
        var outputs = new List<object>();
        foreach (var folder in fixtureDirectories)
        {
            using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "results.json")));
            Check(baseline.RootElement.GetProperty("Passed").GetBoolean(), "Passing native oracle required");
            foreach (var row in baseline.RootElement.GetProperty("Decoded").EnumerateArray())
            {
                var name = row.GetProperty("file").GetString()!;
                var path = Path.Combine(folder, "fixtures", name);
                var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                Check(originalHash == baseline.RootElement.GetProperty("Fixtures").GetProperty(path).GetString(), "Original fixture identity");
                var refused = name.Contains("-pq.") || name.Contains("-hlg.") || name.Contains("-collection.");
                if (refused)
                {
                    Reject(() => adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None), "Refuse unsupported content " + name);
                    continue;
                }
                var facts = adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None);
                Check(facts.UnsupportedReason is null && facts.Orientation == 1 && facts.Format == (name.EndsWith(".heic") ? ImageFormat.Heic : ImageFormat.Avif), "Source facts " + name);
                Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.WebP), new("convert", new())).HasExecutableItems, "No extra output pair");
                if (facts.HasTransparency)
                    Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Jpeg), new("convert", new())).HasExecutableItems, "JPEG requires explicit matte");
                if (facts.HasOtherMetadata || facts.ProfileNames.Any(name => name is "exif" or "xmp"))
                    Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic),
                        new("convert", new(ReplaceOriginals: true)), replaceOriginal: true).ReplaceOriginal, "Automatic metadata omission forces a copy");
                if (facts.Format == ImageFormat.Heic) Reject(() => new ImageConversionOptions(facts.Format).Validate(), "HEIC remains input-only");
                else Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality), new("convert", new())).HasExecutableItems,
                    "AVIF output does not admit same-format AVIF input re-encoding");
                Reject(() => ImagePdfPage.Create(facts), "No implicit image-to-PDF expansion");
                var raw = File.ReadAllBytes(Path.Combine(folder, name + "-decoded", "pixels.rgba"));
                Check(Convert.ToHexString(SHA256.HashData(raw)) == row.GetProperty("actualPixelsSha256").GetString() &&
                    row.GetProperty("maximumPixelDifference").GetInt32() <= 1, "Independently bounded sample oracle");
                var bits = (int)facts.BitDepth;
                var samples = new ushort[checked((int)(facts.Width * facts.Height * 4))];
                for (var index = 0; index < samples.Length; index++)
                {
                    var sample = bits == 8 ? raw[index] : BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(index * 2));
                    var maximum = (1u << bits) - 1;
                    samples[index] = (ushort)((sample * 65535u + maximum / 2) / maximum);
                }
                if (name.Contains("-color-p3.") || name.Contains("-color-bt709."))
                {
                    var preview = adapter.Preview(new(facts), CancellationToken.None);
                    var p3 = name.Contains("-color-p3.");
                    using var profileStream = typeof(ImageAdapter).Assembly.GetManifestResourceStream(
                        "ContextSuite.Private.Images.Profiles." + (p3 ? "DisplayP3" : "Bt709") + ".icc")!;
                    using var profileBuffer = new MemoryStream();
                    profileStream.CopyTo(profileBuffer);
                    var sourceMatrix = Matrix(profileBuffer.ToArray());
                    var targetProfile = ColorProfiles.SRGB.ToByteArray();
                    var targetInverse = Inverse(Matrix(targetProfile));
                    var targetCurve = Tag(targetProfile, "rTRC");
                    Check(targetCurve.AsSpan(0, 4).SequenceEqual("curv"u8), "Independent sRGB oracle expects sampled ICC curve");
                    var curveCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(targetCurve.AsSpan(8)));
                    var curve = Enumerable.Range(0, curveCount).Select(i => BinaryPrimitives.ReadUInt16BigEndian(targetCurve.AsSpan(12 + i * 2))).ToArray();
                    var maximumError = 0;
                    object? worst = null;
                    for (var index = 0; index < samples.Length; index += 4)
                    {
                        var linear = samples.AsSpan(index, 3).ToArray().Select(value =>
                        {
                            var x = value / 65535.0;
                            return p3 ? (x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4)) :
                                x < 0.081 ? x / 4.5 : Math.Pow((x + 0.099) / 1.099, 1 / 0.45);
                        }).ToArray();
                        linear = Multiply(targetInverse, Multiply(sourceMatrix, linear));
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var x = Math.Clamp(linear[channel], 0, 1);
                            var low = 0;
                            var high = curve.Length - 1;
                            while (high - low > 1)
                            {
                                var middle = (low + high) / 2;
                                if (curve[middle] / 65535.0 < x) low = middle; else high = middle;
                            }
                            var fraction = Math.Clamp((x * 65535 - curve[low]) / (curve[high] - curve[low]), 0, 1);
                            var expected = (int)Math.Round(255 * (low + fraction) / (curve.Length - 1));
                            var actual = preview.BgraPixels[index + 2 - channel];
                            var error = Math.Abs(actual - expected);
                            if (error > maximumError)
                            {
                                maximumError = error;
                                worst = new { index, channel, Expected = expected, Actual = actual, Samples = samples.AsSpan(index, 3).ToArray(), Linear = linear };
                            }
                        }
                    }
                    File.WriteAllText(Path.Combine(root, "appearance-" + bits + "-" + name + ".json"), JsonSerializer.Serialize(new { maximumError, worst }));
                    Check(maximumError <= 2, "Independent SDR appearance: " + name + " maximum byte error " + maximumError);
                }
                foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg })
                {
                    var options = new ImageConversionOptions(target, MatteRgb: target == ImageFormat.Jpeg ? 0xffffffu : null, Metadata: ImageMetadataMode.Automatic);
                    var batch = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
                    var plan = batch.Confirm(true, false, false).Plan.Items[0];
                    var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    var output = Path.Combine(directory, $".context-suite-{facts.ItemId:N}.tmp");
                    using (File.Create(output)) { }
                    ImageWorkResult result;
                    try { result = adapter.Convert(new(plan, options, output), CancellationToken.None); }
                    catch
                    {
                        Console.Error.WriteLine(name + " -> " + target + " at " + output);
                        if (new FileInfo(output).Length > 0)
                        {
                            using var diagnostic = new MagickImage();
                            diagnostic.Warning += (_, warning) => Console.Error.WriteLine(warning.Message);
                            diagnostic.Read(output);
                        }
                        throw;
                    }
                    Check(result.Validation.MatchesPlan && result.Width == facts.Width && result.Height == facts.Height, "Validated output " + name + " -> " + target);
                    using var image = new MagickImage();
                    image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
                    image.Read(output);
                    Check(image.GetColorProfile() is not null, "Color profile retained");
                    if (name.Contains("-icc-"))
                        Check(image.GetColorProfile()!.ToByteArray().SequenceEqual(File.ReadAllBytes(Path.Combine(folder, name + "-decoded", "profile.icc"))), "Original ICC exactly retained");
                    if (target == ImageFormat.Png)
                    {
                        image.ColorType = ColorType.TrueColorAlpha;
                        Check(image.GetPixels().ToArray()!.SequenceEqual(samples), "Exact PNG samples, including hidden RGB " + name);
                        Check(result.BitDepth == (bits > 8 ? 16 : 8), "PNG precision");
                    }
                    else
                    {
                        var pixelBytes = new byte[samples.Length * 2];
                        for (var index = 0; index < samples.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(pixelBytes.AsSpan(index * 2), samples[index]);
                        using var expected = new MagickImage();
                        expected.ReadPixels(pixelBytes, new PixelReadSettings(facts.Width, facts.Height, StorageType.Short, PixelMapping.RGBA));
                        expected.ColorSpace = ColorSpace.sRGB;
                        expected.SetProfile(image.GetColorProfile()!);
                        expected.BackgroundColor = MagickColors.White;
                        expected.Alpha(AlphaOption.Remove);
                        expected.Depth = 8;
                        expected.Quality = 90;
                        using var encoded = new MemoryStream();
                        expected.Write(encoded, MagickFormat.Jpeg);
                        using var oracle = new MagickImage(encoded.ToArray());
                        Check(image.GetPixels().ToByteArray(PixelMapping.RGB)!.SequenceEqual(oracle.GetPixels().ToByteArray(PixelMapping.RGB)!), "JPEG equals encoding of independent samples " + name);
                    }
                    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == originalHash, "Original unchanged");
                    outputs.Add(new { Source = path, Target = target.ToString(), Output = output, result.OutputBytes });
                }
            }
        }
        var refusals = 0;
        if (bridgeEvidence is not null)
        {
            using var boundaryResults = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bridgeEvidence, "results.json")));
            Check(boundaryResults.RootElement.GetProperty("Passed").GetBoolean(), "Passing bridge boundaries required");
            var recoverySource = Path.Combine(fixtureDirectories[0], "fixtures", "orientation-1-color-grid.heic");
            foreach (var row in boundaryResults.RootElement.GetProperty("GridRefusals").EnumerateArray())
            {
                var path = row.GetProperty("file").GetString()!;
                var hash = row.GetProperty("sha256").GetString();
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, "Malformed grid identity");
                Reject(() => adapter.Probe(new(Guid.NewGuid(), path), CancellationToken.None), "Refuse malformed grid");
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, "Malformed source unchanged");
                Check(adapter.Probe(new(Guid.NewGuid(), recoverySource), CancellationToken.None).UnsupportedReason is null,
                    "Adapter recovers after malformed grid");
                refusals++;
            }
            Check(refusals > 0, "Boundary evidence is not empty");
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Outputs = outputs, GridRefusals = refusals }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{checks} HEIF conversion checks passed; {outputs.Count} JPEG/PNG outputs validated.");

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks++;
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or
                ImageFailureException { Failure: ImageFailure.InvalidInput or ImageFailure.UnsupportedInput or ImageFailure.ResourceLimit }) { checks++; return; }
            throw new InvalidDataException(message);
        }
    }

    private static byte[] Tag(byte[] profile, string name)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(profile.AsSpan(128));
        for (var index = 0; index < count; index++)
        {
            var entry = profile.AsSpan(132 + index * 12, 12);
            if (System.Text.Encoding.ASCII.GetString(entry[..4]) != name) continue;
            return profile.AsSpan(checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..])),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]))).ToArray();
        }
        throw new InvalidDataException("Missing oracle ICC tag " + name);
    }

    private static double[,] Matrix(byte[] profile)
    {
        var result = new double[3, 3];
        for (var column = 0; column < 3; column++)
        {
            var tag = Tag(profile, new[] { "rXYZ", "gXYZ", "bXYZ" }[column]);
            for (var row = 0; row < 3; row++) result[row, column] = BinaryPrimitives.ReadInt32BigEndian(tag.AsSpan(8 + row * 4)) / 65536.0;
        }
        return result;
    }

    private static double[] Multiply(double[,] matrix, double[] vector) => Enumerable.Range(0, 3)
        .Select(row => Enumerable.Range(0, 3).Sum(column => matrix[row, column] * vector[column])).ToArray();

    private static double[,] Inverse(double[,] matrix)
    {
        var result = new double[3, 3];
        var determinant = 0.0;
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            var minorRows = Enumerable.Range(0, 3).Where(i => i != row).ToArray();
            var minorColumns = Enumerable.Range(0, 3).Where(i => i != column).ToArray();
            var cofactor = (matrix[minorRows[0], minorColumns[0]] * matrix[minorRows[1], minorColumns[1]] -
                matrix[minorRows[0], minorColumns[1]] * matrix[minorRows[1], minorColumns[0]]) * ((row + column) % 2 == 0 ? 1 : -1);
            result[column, row] = cofactor;
            if (row == 0) determinant += matrix[row, column] * cofactor;
        }
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++) result[row, column] /= determinant;
        return result;
    }
}
