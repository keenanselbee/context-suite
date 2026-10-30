using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Private.Images;
using ContextSuite.Private.Pdf;

internal static class ImagePdfResourceContracts
{
    public static async Task RunAsync(string validatorDirectory, string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root))
            throw new IOException("Use a fresh absolute repository evidence directory.");
        Directory.CreateDirectory(root);
        var imageScratch = Path.Combine(root, "image scratch");
        var adapter = new ImageAdapter(imageScratch);
        var validatorScratch = Path.Combine(root, "validator scratch");
        using var validator = new ImagePdfValidator(validatorDirectory, validatorScratch);
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var observations = new List<object>();
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();

        var maximum = Bitmap("maximum-page.bmp", 4000, 4000);
        await Complete("maximum-page", maximum.Path, maximum.Pixels, true);
        var wide = Bitmap("maximum-width.bmp", 16384, 1);
        await Complete("maximum-width", wide.Path, wide.Pixels, false);

        var second = Path.Combine(root, "second-page.bmp");
        var third = Path.Combine(root, "third-page.bmp");
        File.Copy(maximum.Path, second); File.Copy(maximum.Path, third);
        Remember(second); Remember(third);
        var sources = new[] { maximum.Path, second, third }
            .Select(path => adapter.Probe(new(Guid.NewGuid(), path), default)).ToArray();
        var combined = ImagePdfPlan.Create(Guid.NewGuid(), sources, settings).Confirm(true);
        var timer = Stopwatch.StartNew();
        try
        {
            _ = ImagePdfWriter.Create(combined, adapter, default);
            throw new Exception("Three incompressible maximum pages unexpectedly fit the output cap.");
        }
        catch (ImageFailureException error) when (error.Failure == ImageFailure.ResourceLimit)
        {
            CheckOriginals();
            Record("three-page output-cap refusal", timer.Elapsed, null);
        }

        var excessive = Bitmap("over-pixel-budget.bmp", 4001, 4000);
        timer.Restart();
        var oversizedFacts = adapter.Probe(new(Guid.NewGuid(), excessive.Path), default);
        try
        {
            _ = ImagePdfPlan.Create(Guid.NewGuid(), [oversizedFacts], settings);
            throw new Exception("A page above 16 million pixels was admitted.");
        }
        catch (NotSupportedException)
        {
            CheckOriginals();
            Record("over-pixel-budget plan refusal", timer.Elapsed, null);
        }

        var small = Bitmap("after-refusal.bmp", 32, 24);
        await Complete("after-refusal", small.Path, small.Pixels, false);
        CheckOriginals();
        Console.WriteLine($"Passed {observations.Count} image-PDF resource cases; originals and source leases verified.");

        async Task Complete(string name, string path, string expectedPixels, bool retain)
        {
            var watch = Stopwatch.StartNew();
            var source = adapter.Probe(new(Guid.NewGuid(), path), default);
            var confirmed = ImagePdfPlan.Create(Guid.NewGuid(), [source], settings).Confirm(true);
            var candidate = ImagePdfWriter.Create(confirmed, adapter, default);
            if (candidate.Samples.Length != 1 || candidate.Samples[0].ColorSha256 != expectedPixels ||
                candidate.Bytes.Length > ImagePdfPlan.MaximumOutputBytes)
                throw new Exception("Full-resolution original samples or bounded output differ.");
            var hash = await validator.ValidateAsync(candidate, confirmed, default);
            if (hash != Convert.ToHexString(SHA256.HashData(candidate.Bytes)))
                throw new Exception("Independent validation returned a different output digest.");
            if (retain) await File.WriteAllBytesAsync(Path.Combine(root, name + ".pdf"), candidate.Bytes);
            CheckOriginals();
            Record(name, watch.Elapsed, candidate.Bytes.Length);
        }

        (string Path, string Pixels) Bitmap(string name, int width, int height)
        {
            var path = Path.Combine(root, name);
            var stride = checked((width * 3 + 3) & ~3);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write((ushort)0x4d42); writer.Write(checked(54 + stride * height));
                writer.Write(0); writer.Write(54); writer.Write(40); writer.Write(width);
                writer.Write(-height); // Top-down BMP: authored row order is the PDF sample oracle.
                writer.Write((ushort)1); writer.Write((ushort)24); writer.Write(0);
                writer.Write(stride * height); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                var rgb = new byte[width * 3];
                var bgr = new byte[stride];
                var random = new Random(1729);
                for (var y = 0; y < height; y++)
                {
                    random.NextBytes(rgb); hash.AppendData(rgb);
                    for (var x = 0; x < width; x++)
                    {
                        bgr[x * 3] = rgb[x * 3 + 2]; bgr[x * 3 + 1] = rgb[x * 3 + 1]; bgr[x * 3 + 2] = rgb[x * 3];
                    }
                    writer.Write(bgr);
                }
            }
            Remember(path);
            return (path, Convert.ToHexString(hash.GetHashAndReset()));
        }

        void Remember(string path)
        {
            using var file = File.OpenRead(path);
            originals.Add(path, (Convert.ToHexString(SHA256.HashData(file)), File.GetLastWriteTimeUtc(path)));
        }

        void CheckOriginals()
        {
            foreach (var (path, expected) in originals)
            {
                // Exclusive read proves all source leases were released without modifying the file.
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                if (Convert.ToHexString(SHA256.HashData(file)) != expected.Hash || File.GetLastWriteTimeUtc(path) != expected.Written)
                    throw new Exception("A generated original changed.");
            }
            if (Directory.EnumerateFileSystemEntries(validatorScratch).Any())
                throw new Exception("Validation retained owned temporary files.");
            if (Directory.EnumerateFileSystemEntries(imageScratch).Any(path => Path.GetFileName(path) != "configuration"))
                throw new Exception("Image decoding retained temporary pixels or snapshots.");
        }

        void Record(string name, TimeSpan elapsed, int? outputBytes)
        {
            using var process = Process.GetCurrentProcess();
            observations.Add(new { name, milliseconds = elapsed.TotalMilliseconds, outputBytes,
                cumulativeHostPeakWorkingSetBytes = process.PeakWorkingSet64, hostPrivateBytes = process.PrivateMemorySize64 });
            File.WriteAllText(Path.Combine(root, "image-pdf-resources.json"), JsonSerializer.Serialize(new
            {
                observations, originals = originals.Select(item => new { path = item.Key, item.Value.Hash, item.Value.Written }),
                limits = "Host memory excludes native validator children; cumulative peaks are not per-case peaks. Writer/validator evidence, not app publication, native deadline or whole-worker memory acceptance."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {name} ({elapsed.TotalSeconds:F2}s)");
        }
    }
}
