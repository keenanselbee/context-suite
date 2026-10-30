using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Private.Images;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class ImagePdfPrecisionResourceContracts
{
    public static async Task RunAsync(string validatorDirectory, string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root))
            throw new IOException("Use a fresh absolute repository evidence directory.");
        Directory.CreateDirectory(root);
        var imageScratch = Path.Combine(root, "image scratch");
        var validatorScratch = Path.Combine(root, "validator scratch");
        var adapter = new ImageAdapter(imageScratch);
        using var validator = new ImagePdfValidator(validatorDirectory, validatorScratch);
        var rgb = ColorProfiles.SRGB.ToByteArray();
        var gray = PngFixture.GrayProfile(rgb);
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();
        var observations = new List<object>();
        foreach (var depth in new[] { 8, 16 })
        foreach (var components in new[] { 1, 3 })
        foreach (var alpha in new[] { false, true })
            await Complete(4000, 4000, depth, components, alpha);
        await Complete(19, 11, 16, 3, true);
        Console.WriteLine("Passed nine image-PDF precision resource cases, full color/alpha/profile hashes and source/scratch preservation.");

        async Task Complete(int width, int height, int depth, int components, bool alpha)
        {
            var name = $"samples-{width}-{height}-{depth}-{components}-{alpha}";
            var path = Path.Combine(root, name + ".png");
            var profile = components == 1 ? gray : rgb;
            var expected = CreatePng(path, width, height, depth, components, alpha, profile);
            originals.Add(path, (HashFile(path), File.GetLastWriteTimeUtc(path)));
            var watch = Stopwatch.StartNew();
            var source = adapter.Probe(new(Guid.NewGuid(), path), default);
            var confirmed = ImagePdfPlan.Create(Guid.NewGuid(), [source], new BatchSettings("convert", new(ReplaceOriginals: true))).Confirm(false);
            var page = confirmed.Plan.Pages.Single();
            if (page.Width != width || page.Height != height || page.BitsPerComponent != depth ||
                page.ColorComponents != components || page.Source.HasTransparency != alpha)
                throw new Exception("The precision fixture was not admitted with its authored dimensions/components/alpha.");
            var candidate = ImagePdfWriter.Create(confirmed, adapter, default);
            var samples = candidate.Samples.Single();
            if (samples.ColorSha256 != expected.Color || samples.AlphaSha256 != expected.Alpha ||
                samples.ProfileSha256 != Convert.ToHexString(SHA256.HashData(profile)) ||
                samples.ProfileBytes != profile.Length || candidate.Bytes.Length > ImagePdfPlan.MaximumOutputBytes)
                throw new Exception("Full-resolution color, invisible colors, alpha or exact ICC profile changed.");
            var verified = await validator.ValidateAsync(candidate, confirmed, default);
            if (verified != Convert.ToHexString(SHA256.HashData(candidate.Bytes)))
                throw new Exception("Independent validator returned a different PDF hash.");
            var output = Path.Combine(root, name + ".pdf");
            await File.WriteAllBytesAsync(output, candidate.Bytes);
            foreach (var (original, identity) in originals)
            {
                using var lease = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None);
                if (Convert.ToHexString(SHA256.HashData(lease)) != identity.Hash || File.GetLastWriteTimeUtc(original) != identity.Written)
                    throw new Exception("A generated original changed or retained a read lease.");
            }
            if (Directory.EnumerateFileSystemEntries(validatorScratch).Any() ||
                Directory.EnumerateFileSystemEntries(imageScratch).Any(entry => Path.GetFileName(entry) != "configuration"))
                throw new Exception("Precision processing retained owned image or validator scratch.");
            using var process = Process.GetCurrentProcess();
            observations.Add(new { name, width, height, depth, components, alpha,
                sourceBytes = new FileInfo(path).Length, outputBytes = candidate.Bytes.Length, pdfSha256 = verified,
                colorSha256 = expected.Color, alphaSha256 = expected.Alpha, samples.ProfileSha256,
                milliseconds = watch.Elapsed.TotalMilliseconds, cumulativeHostPeakWorkingSetBytes = process.PeakWorkingSet64,
                hostPrivateBytes = process.PrivateMemorySize64 });
            File.WriteAllText(Path.Combine(root, "image-pdf-precision-resources.json"), JsonSerializer.Serialize(new
            {
                observations, originals = originals.Select(item => new { path = item.Key, item.Value.Hash, item.Value.Written }),
                limits = "Authored patterned PNGs at the pixel limit; no resize or precision reduction. Host peaks exclude validator children and are cumulative. Not output-cap, native-allocation failure, worker publication, rendered appearance or visible acceptance."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {name} ({watch.Elapsed.TotalSeconds:F2}s)");
        }
    }

    private static (string Color, string? Alpha) CreatePng(string path, int width, int height, int depth,
        int components, bool alpha, byte[] profile)
    {
        // Row-sized authored samples and independent hashes. The engine under
        // test neither creates the PNG nor supplies the expected decoded pixels.
        var sampleBytes = depth / 8;
        var channels = components + (alpha ? 1 : 0);
        var row = new byte[1 + width * channels * sampleBytes];
        var colors = new byte[width * components * sampleBytes];
        var alphas = new byte[alpha ? width * sampleBytes : 0];
        using var colorHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var alphaHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    for (var component = 0; component < components; component++)
                    {
                        var value = (ushort)((x * 251 + y * 17 + component * 8191) & 65535);
                        Write(colors.AsSpan((x * components + component) * sampleBytes), value);
                        Write(row.AsSpan(1 + (x * channels + component) * sampleBytes), value);
                    }
                    if (alpha)
                    {
                        var value = (ushort)(((x + y) % 4) switch { 0 => 0, 1 => 65535, _ => (x * 131 + y * 257) & 65535 });
                        Write(alphas.AsSpan(x * sampleBytes), value);
                        Write(row.AsSpan(1 + (x * channels + components) * sampleBytes), value);
                    }
                }
                colorHash.AppendData(colors);
                if (alpha) alphaHash.AppendData(alphas);
                zlib.Write(row);
            }
        }
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = (byte)depth;
        header[9] = (byte)(components == 1 ? alpha ? 4 : 0 : alpha ? 6 : 2);
        file.Write(PngFixture.Chunk("IHDR", header));
        using var profileData = new MemoryStream();
        profileData.Write("Authored\0\0"u8);
        using (var zlib = new ZLibStream(profileData, CompressionLevel.SmallestSize, true)) zlib.Write(profile);
        file.Write(PngFixture.Chunk("iCCP", profileData.ToArray()));
        file.Write(PngFixture.Chunk("IDAT", compressed.ToArray()));
        file.Write(PngFixture.Chunk("IEND", []));
        return (Convert.ToHexString(colorHash.GetHashAndReset()), alpha ? Convert.ToHexString(alphaHash.GetHashAndReset()) : null);

        void Write(Span<byte> destination, ushort value)
        {
            if (depth == 16) BinaryPrimitives.WriteUInt16BigEndian(destination, value);
            else destination[0] = (byte)(value >> 8);
        }
    }

    private static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
