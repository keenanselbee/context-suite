using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Private.Images;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class ImagePdfContracts
{
    private sealed record Fixture(string Path, uint Width, uint Height, byte Depth, int Components,
        bool HasAlpha, ushort[][] Pixels, byte[] Profile);

    public static async Task RunAsync(string qpdf, string pdfium, string root, string? validatorDirectory = null)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh image-PDF evidence directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        var observations = new List<object>();
        var adapter = new ImageAdapter(Path.Combine(root, "image scratch"));
        var fixtures = new List<Fixture>();
        var srgb = ColorProfiles.SRGB.ToByteArray();
        var gray = PngFixture.GrayProfile(srgb);
        foreach (var depth in new byte[] { 8, 16 })
        foreach (var components in new[] { 1, 3 })
        foreach (var alpha in new[] { false, true })
        {
            var values = Enumerable.Range(0, 12).Select(i => Enumerable.Range(0, components + (alpha ? 1 : 0))
                .Select(c => (ushort)(depth == 8 ? ((i * 43 + c * 61) % 256) * 257 : (i * 4321 + c * 12345) % 65536)).ToArray()).ToArray();
            Add($"samples-{depth}-{components}-{alpha}.png", 4, 3, depth, components, alpha, values, components == 1 ? gray : srgb);
        }
        Add("wide color ü.png", 4, 3, 8, 3, false, Enumerable.Range(0, 12).Select(i => new ushort[] { 45000, 20000, 10000 }
            .Select(v => (ushort)(v / 257 * 257)).ToArray()).ToArray(), ColorProfiles.AdobeRGB1998.ToByteArray());
        var bareValues = Enumerable.Range(0, 12).Select(i => new ushort[] { (ushort)(i * 20 * 257), 25700, 51400 }).ToArray();
        var barePath = Path.Combine(root, "no-profile.png");
        File.WriteAllBytes(barePath, PngFixture.Create(4, 3, 8, 2, (x, y) => bareValues[y * 4 + x]));
        fixtures.Add(new(barePath, 4, 3, 8, 3, false, bareValues, srgb));
        foreach (var format in new[] { MagickFormat.Bmp3, MagickFormat.Tga, MagickFormat.WebP })
        {
            var path = Path.Combine(root, "common." + (format == MagickFormat.Bmp3 ? "bmp" : format.ToString().ToLowerInvariant()));
            using var image = new MagickImage(barePath);
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            image.Write(path, format);
            fixtures.Add(new(path, 4, 3, 8, 3, false, bareValues, srgb));
        }
        foreach (var format in new[] { MagickFormat.Tga, MagickFormat.WebP })
        {
            var path = Path.Combine(root, "alpha." + format.ToString().ToLowerInvariant());
            using var image = new MagickImage(fixtures[3].Path);
            image.RemoveProfile("icc");
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            image.Write(path, format);
            fixtures.Add(new(path, 4, 3, 8, 3, true, fixtures[3].Pixels, srgb));
        }
        for (ushort orientation = 1; orientation <= 8; orientation++)
        {
            var path = Path.Combine(root, $"orientation-{orientation}.jpg");
            using (var image = new MagickImage(barePath))
            {
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, orientation);
                image.SetProfile(exif); image.Orientation = (OrientationType)orientation;
                image.Density = new Density(96, 96, DensityUnit.PixelsPerInch);
                image.Write(path, MagickFormat.Jpeg);
            }
            using var decoded = new MagickImage(path);
            var samples = decoded.GetPixels().ToByteArray(PixelMapping.RGB)!;
            var width = orientation >= 5 ? 3u : 4u;
            var height = orientation >= 5 ? 4u : 3u;
            var values = new ushort[12][];
            // Independent EXIF coordinate mapping; do not use AutoOrient as an oracle.
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 4; x++)
            {
                var (dx, dy) = orientation switch { 2 => (3 - x, y), 3 => (3 - x, 2 - y), 4 => (x, 2 - y),
                    5 => (y, x), 6 => (2 - y, x), 7 => (2 - y, 3 - x), 8 => (y, 3 - x), _ => (x, y) };
                values[dy * width + dx] = samples.AsSpan((y * 4 + x) * 3, 3).ToArray().Select(v => (ushort)(v * 257)).ToArray();
            }
            fixtures.Add(new(path, width, height, 8, 3, false, values, srgb));
        }
        var inputs = fixtures.ToDictionary(f => f.Path, f => Hash(File.ReadAllBytes(f.Path)));
        var sources = fixtures.Select(f => adapter.Probe(new(Guid.NewGuid(), f.Path), default)).ToArray();
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var plan = ImagePdfPlan.Create(Guid.NewGuid(), sources, settings).Confirm(true);
        var candidate = ImagePdfWriter.Create(plan, adapter, default);
        var result = candidate.Bytes;
        if (validatorDirectory is not null)
            await ImagePdfValidationContracts.RunAsync(candidate, plan, adapter, validatorDirectory, Path.Combine(root, "validation"));
        var output = Path.Combine(root, "combined ü.pdf"); File.WriteAllBytes(output, result);
        Check(result.AsSpan().SequenceEqual(ImagePdfWriter.Create(plan, adapter, default).Bytes), "repeated serialization is deterministic");
        await Run(qpdf, "--suppress-recovery", "--check", output);
        Check(true, "qpdf independently accepts the complete PDF without warnings or repair");
        using var json = JsonDocument.Parse(await Run(qpdf, "--suppress-recovery", "--json-output=2", "--decode-level=generalized", "--json-stream-data=inline", output));
        var objects = json.RootElement.GetProperty("qpdf")[1];
        var pageTree = objects.GetProperty("obj:2 0 R").GetProperty("value");
        var kids = pageTree.GetProperty("/Kids");
        Check(pageTree.GetProperty("/Count").GetInt32() == fixtures.Count && kids.GetArrayLength() == fixtures.Count, "one combined document contains all selected images in reviewed order");
        var renderedPages = new List<(JsonElement Geometry, string Path)>();
        // The existing independent probe permits 16 pages per call. qpdf makes
        // bounded page subsets from the actual combined PDF, preserving its
        // content/resources; the original combined file remains unchanged.
        for (var first = 0; first < fixtures.Count; first += 16)
        {
            var last = Math.Min(first + 16, fixtures.Count);
            var subset = Path.Combine(root, $"subset-{first}.pdf");
            await Run(qpdf, output, "--pages", ".", $"{first + 1}-{last}", "--", subset);
            var prefix = Path.Combine(root, $"rendered-{first}");
            using var rendering = JsonDocument.Parse(await Run(pdfium, "render", subset, prefix, "96", "transparent", "none"));
            var pages = rendering.RootElement.GetProperty("pages");
            for (var i = 0; i < pages.GetArrayLength(); i++) renderedPages.Add((pages[i].Clone(), prefix + $"-{i + 1}.bgra"));
        }
        Check(renderedPages.Count == fixtures.Count && Hash(File.ReadAllBytes(output)) == Hash(result), "PDFium independently renders every combined page; combined bytes stay unchanged");
        for (var index = 0; index < fixtures.Count; index++)
        {
            var fixture = fixtures[index];
            var page = objects.GetProperty("obj:" + kids[index].GetString()).GetProperty("value");
            var imageRef = page.GetProperty("/Resources").GetProperty("/XObject").GetProperty("/Im0").GetString();
            var stream = objects.GetProperty("obj:" + imageRef).GetProperty("stream");
            var dictionary = stream.GetProperty("dict");
            var iccRef = dictionary.GetProperty("/ColorSpace")[1].GetString();
            var icc = objects.GetProperty("obj:" + iccRef).GetProperty("stream");
            Check(icc.GetProperty("dict").GetProperty("/N").GetInt32() == fixture.Components &&
                Bytes(icc).AsSpan().SequenceEqual(fixture.Profile), "exact ICC and color components: " + Path.GetFileName(fixture.Path));
            Check(dictionary.GetProperty("/BitsPerComponent").GetInt32() == fixture.Depth &&
                dictionary.GetProperty("/Width").GetUInt32() == fixture.Width && dictionary.GetProperty("/Height").GetUInt32() == fixture.Height &&
                Bytes(stream).AsSpan().SequenceEqual(Plane(fixture, false)), "independently decoded samples and oriented geometry: " + Path.GetFileName(fixture.Path));
            if (fixture.HasAlpha)
            {
                var alphaRef = dictionary.GetProperty("/SMask").GetString();
                var alphaStream = objects.GetProperty("obj:" + alphaRef).GetProperty("stream");
                Check(Bytes(alphaStream).AsSpan().SequenceEqual(Plane(fixture, true)), "exact alpha soft-mask samples: " + Path.GetFileName(fixture.Path));
            }
            var geometry = renderedPages[index].Geometry;
            Check(geometry.GetProperty("width").GetUInt32() == fixture.Width && geometry.GetProperty("height").GetUInt32() == fixture.Height,
                "independent rendered page size: " + Path.GetFileName(fixture.Path));
            using var expected = new MagickImage(PngFixture.Create(fixture.Width, fixture.Height, fixture.Depth,
                (byte)(fixture.Components == 1 ? fixture.HasAlpha ? 4 : 0 : fixture.HasAlpha ? 6 : 2),
                (x, y) => fixture.Pixels[y * fixture.Width + x], ("iCCP", ProfileChunk(fixture.Profile))));
            expected.TransformColorSpace(ColorProfiles.SRGB);
            var expectedPixels = expected.GetPixels().ToByteArray(PixelMapping.BGRA)!;
            var actual = File.ReadAllBytes(renderedPages[index].Path);
            var maxError = 0;
            for (var pixel = 0; pixel < actual.Length; pixel += 4)
            {
                maxError = Math.Max(maxError, Math.Abs(actual[pixel + 3] - expectedPixels[pixel + 3]));
                for (var channel = 0; channel < 3; channel++)
                    maxError = Math.Max(maxError, (int)Math.Round(Math.Abs(actual[pixel + channel] * actual[pixel + 3] -
                        expectedPixels[pixel + channel] * expectedPixels[pixel + 3]) / 255.0));
            }
            observations.Add(new { fixture = Path.GetFileName(fixture.Path), maxPremultipliedChannelError = maxError });
            Check(maxError <= 4, "PDFium appearance agrees with independent ICC/sample conversion: " + Path.GetFileName(fixture.Path) + " (error " + maxError + ")");
        }
        var changed = sources[0] with { Sha256 = new('0', 64) };
        Reject(() => ImagePdfWriter.Create(ImagePdfPlan.Create(Guid.NewGuid(), [changed], settings).Confirm(false), adapter, default), "stale source digest refused");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { ImagePdfWriter.Create(plan, adapter, cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { Check(true, "pre-cancellation produces no PDF"); }
        var longRoot = root;
        while (longRoot.Length < 270) longRoot = Path.Combine(longRoot, "generated Unicode ü long path");
        Directory.CreateDirectory(longRoot);
        var longPath = Path.Combine(longRoot, "original.png"); File.Copy(barePath, longPath);
        var longSource = adapter.Probe(new(Guid.NewGuid(), longPath), default);
        var longResult = ImagePdfWriter.Create(ImagePdfPlan.Create(Guid.NewGuid(), [longSource], settings).Confirm(false), adapter, default);
        Check(longResult.Bytes.Length > 0 && File.ReadAllBytes(longPath).AsSpan().SequenceEqual(File.ReadAllBytes(barePath)), "long local Unicode path retains the source");
        var largePath = Path.Combine(root, "cancellation.png");
        using (var large = new MagickImage(barePath))
        { large.Resize(2500, 2500); large.Depth = 8; large.Write(largePath, MagickFormat.Png); }
        var largeHash = Hash(File.ReadAllBytes(largePath));
        var largeSource = adapter.Probe(new(Guid.NewGuid(), largePath), default);
        var cancellable = ImagePdfPlan.Create(Guid.NewGuid(), [largeSource, sources[0]], settings).Confirm(true);
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = Task.Run(() => ImagePdfWriter.Create(cancellable, adapter, cancellation.Token));
            var observed = false;
            var watch = Stopwatch.StartNew();
            while (!pending.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                try { using var access = new FileStream(sources[0].Path, FileMode.Open, FileAccess.Write, FileShare.Read); }
                catch (IOException) { observed = true; break; }
                await Task.Delay(1);
            }
            Check(observed, "combined writer leases later images before decoding the first image");
            try { File.Move(sources[0].Path, sources[0].Path + ".moved"); throw new Exception("Original was movable during combined creation."); }
            catch (IOException) { Check(true, "all original names are protected during serialization"); }
            cancellation.Cancel();
            try { await pending; throw new Exception("In-flight cancellation ignored."); }
            catch (OperationCanceledException) { Check(true, "cancellation after all source leases are acquired returns no PDF"); }
        }
        Check(Hash(File.ReadAllBytes(largePath)) == largeHash, "large original remains unchanged after interruption");
        using (var access = new FileStream(largePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        foreach (var fixture in fixtures)
        {
            Check(Hash(File.ReadAllBytes(fixture.Path)) == inputs[fixture.Path], "source unchanged: " + Path.GetFileName(fixture.Path));
            using var writable = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Check(true, "all source leases are released after success and refusal");
        File.WriteAllText(Path.Combine(root, "image-pdf.json"), JsonSerializer.Serialize(new { policy = ImagePdfPlan.Policy,
            outputSha256 = Hash(result), checks, observations }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} image-PDF candidate checks. Evidence: {root}");

        void Add(string name, uint width, uint height, byte depth, int components, bool alpha, ushort[][] values, byte[] profile)
        {
            var path = Path.Combine(root, name);
            File.WriteAllBytes(path, PngFixture.Create(width, height, depth, (byte)(components == 1 ? alpha ? 4 : 0 : alpha ? 6 : 2),
                (x, y) => values[y * width + x], ("iCCP", ProfileChunk(profile))));
            fixtures.Add(new(path, width, height, depth, components, alpha, values, profile));
        }
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks.Add(name); }
        void Reject(Action action, string name)
        {
            try { action(); throw new Exception("Unexpected acceptance: " + name); }
            catch (InvalidDataException) { Check(true, name); }
            catch (ImageFailureException error) when (error.Failure == ImageFailure.SourceChanged) { Check(true, name); }
        }
        async Task<string> Run(string executable, params string[] arguments)
        {
            using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root } };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            process.Start();
            async Task<string> Read(StreamReader reader)
            {
                var buffer = new char[4 * 1024 * 1024]; var count = 0;
                while (count < buffer.Length)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(count), deadline.Token);
                    if (read == 0) return new(buffer, 0, count);
                    count += read;
                }
                if (!process.HasExited) process.Kill(true);
                throw new InvalidDataException("Evaluation diagnostic limit exceeded.");
            }
            try
            {
                var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
                if (process.ExitCode != 0) throw new Exception("Independent reader failed: " + await stderr + await stdout);
                return await stdout;
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        }
    }

    private static byte[] Plane(Fixture fixture, bool alpha)
    {
        using var stream = new MemoryStream();
        Span<byte> word = stackalloc byte[2];
        foreach (var pixel in fixture.Pixels)
        foreach (var value in alpha ? pixel.Skip(fixture.Components) : pixel.Take(fixture.Components))
        {
            if (fixture.Depth == 16) { BinaryPrimitives.WriteUInt16BigEndian(word, value); stream.Write(word); }
            else stream.WriteByte((byte)(value / 257));
        }
        return stream.ToArray();
    }
    private static byte[] ProfileChunk(byte[] profile)
    {
        using var stream = new MemoryStream(); stream.Write("profile\0\0"u8);
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, true)) zlib.Write(profile);
        return stream.ToArray();
    }
    private static byte[] Bytes(JsonElement stream) => Convert.FromBase64String(stream.GetProperty("data").GetString()!);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
