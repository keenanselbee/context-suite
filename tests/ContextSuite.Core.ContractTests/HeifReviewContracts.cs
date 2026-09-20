using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static class HeifReviewContracts
{
    internal sealed record Scene(string Name, int Width, int Height, int Bits, bool Alpha, int Orientation);
    private static readonly Scene[] Scenes = [new("photograph", 768, 512, 8, false, 1),
        new("orientation", 640, 400, 8, false, 6), new("transparency", 640, 400, 8, true, 1),
        new("gradient", 2048, 256, 12, false, 1)];

    internal static void Prepare(string pack, string photograph)
    {
        pack = Owned(pack);
        if (Directory.Exists(pack) || Hash(photograph) != "E25CA1FF2F0C0CB5FDFD5F9B0A0BB21AC4C3DE3C84A67F35B09A85D3306249DB")
            throw new ArgumentException("Use a new pack and the pinned Kodak photograph.");
        Directory.CreateDirectory(pack);
        var inputs = Path.Combine(pack, "sources"); Directory.CreateDirectory(inputs);
        var references = Path.Combine(pack, "references"); Directory.CreateDirectory(references);
        File.Copy(photograph, Path.Combine(references, "kodim03-original.png"));
        var photo = Pixels(photograph, out var pw, out var ph);
        if (pw != 768 || ph != 512) throw new InvalidDataException("Unexpected photograph geometry.");
        foreach (var scene in Scenes)
        {
            var samples = new ushort[scene.Width * scene.Height * 4]; var maximum = (1 << scene.Bits) - 1;
            for (var y = 0; y < scene.Height; y++) for (var x = 0; x < scene.Width; x++)
            {
                var offset = (y * scene.Width + x) * 4;
                int r, g, b, a = maximum;
                if (scene.Name == "photograph")
                    (r, g, b) = (photo[offset] / 257, photo[offset + 1] / 257, photo[offset + 2] / 257);
                else if (scene.Name == "gradient")
                {
                    // A 12-bit neutral ramp with >256 distinct levels and dark/bright bands.
                    var value = x * maximum / (scene.Width - 1);
                    r = g = b = y < 128 ? value : y < 192 ? value / 8 : maximum - value / 8;
                }
                else
                {
                    // Asymmetric corner colors and an upward arrow make a wrong rotation visible.
                    (r, g, b) = (x < 320, y < 200) switch
                    {
                        (true, true) => (220, 55, 40), (false, true) => (30, 175, 70),
                        (true, false) => (35, 85, 220), _ => (225, 190, 35)
                    };
                    if (Math.Abs(x - 320) < 14 && y is > 100 and < 330 || y is > 50 and < 150 && Math.Abs(x - 320) < y - 45)
                        r = g = b = 245;
                    if (x is > 25 and < 100 && y is > 25 and < 65) r = g = b = 15;
                    if (scene.Alpha) a = x * 255 / (scene.Width - 1);
                }
                samples[offset] = (ushort)r; samples[offset + 1] = (ushort)g;
                samples[offset + 2] = (ushort)b; samples[offset + 3] = (ushort)a;
            }
            var raw = new byte[samples.Length * 2];
            for (var index = 0; index < samples.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(index * 2), samples[index]);
            File.WriteAllBytes(Path.Combine(inputs, scene.Name + ".rgba16"), raw);
            var normalized = samples.Select(value => (ushort)((value * 65535L + maximum / 2) / maximum)).ToArray();
            SavePng(Path.Combine(references, scene.Name + "-stored.png"), scene.Width, scene.Height, normalized);
            var width = scene.Orientation == 6 ? scene.Height : scene.Width;
            var height = scene.Orientation == 6 ? scene.Width : scene.Height;
            var expected = new ushort[normalized.Length];
            for (var y = 0; y < scene.Height; y++) for (var x = 0; x < scene.Width; x++)
            {
                var targetX = scene.Orientation == 6 ? scene.Height - y - 1 : x;
                var targetY = scene.Orientation == 6 ? x : y;
                Array.Copy(normalized, (y * scene.Width + x) * 4, expected, (targetY * width + targetX) * 4, 4);
            }
            SavePng(Path.Combine(references, scene.Name + "-png.png"), width, height, expected);
            for (var pixel = 0; pixel < expected.Length; pixel += 4)
            {
                var alpha = expected[pixel + 3] / 65535.0;
                for (var channel = 0; channel < 3; channel++)
                    expected[pixel + channel] = (ushort)Math.Round(expected[pixel + channel] * alpha + 65535 * (1 - alpha));
                expected[pixel + 3] = 65535;
            }
            SavePng(Path.Combine(references, scene.Name + "-jpeg.png"), width, height, expected);
        }
        File.WriteAllText(Path.Combine(pack, "scenes.json"), JsonSerializer.Serialize(Scenes, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static async Task RunAsync(string pack, string executable)
    {
        pack = Owned(pack);
        var work = Path.Combine(Path.GetDirectoryName(pack)!, "work");
        if (Directory.Exists(work)) throw new IOException("Use fresh image review work.");
        Directory.CreateDirectory(work);
        var originals = Directory.GetFiles(Path.Combine(pack, "sources")).Concat(Directory.GetFiles(Path.Combine(pack, "references")))
            .ToDictionary(path => path, path => (Hash: Hash(path), Written: File.GetLastWriteTimeUtc(path)));
        var rows = new List<object>();
        foreach (var scene in Scenes)
        foreach (var extension in new[] { "heic", "avif" })
        foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg })
        {
            var name = scene.Name + "-" + extension + "-" + target.ToString().ToLowerInvariant();
            var source = Path.Combine(pack, "sources", scene.Name + "." + extension);
            var directory = Path.Combine(work, name); Directory.CreateDirectory(directory);
            var outputs = Path.Combine(pack, "outputs", name); Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "publications"); var scratch = Path.Combine(directory, "worker");
            var prompts = 0;
            await using (var worker = new WorkerClient(executable, scratch))
            await using (var vm = new MainViewModel(worker,
                new SuiteSettings { Convert = new(OutputDirectory: outputs), PlayCompletionSound = false },
                new OutputPublisher(records, new ForbiddenRecycle()), new LocalTrialStore(Path.Combine(directory, "trial.json"))))
            {
                vm.ConversionRequested += async (planner, token) =>
                {
                    prompts++; await planner.InitializeAsync();
                    if (!scene.Alpha || target != ImageFormat.Jpeg || !planner.NeedsBackgroundChoice || planner.CanConfirm)
                        throw new InvalidDataException("Only transparent JPEG should require a background decision.");
                    planner.MatteChoice = 1; planner.WarningsAcknowledged = true;
                    await planner.RefreshPreviewAsync(); token.ThrowIfCancellationRequested();
                    ConfirmedImageBatch? confirmed = null; planner.Confirmed += value => confirmed = value;
                    if (!planner.CanConfirm) throw new InvalidDataException("Explicit white background is not confirmable.");
                    planner.ConfirmCommand.Execute(null); return confirmed;
                };
                vm.Admit(new(Guid.NewGuid(), "convert", target == ImageFormat.Jpeg ? "jpeg" : "png", [source]));
                await vm.WaitForIdleAsync(); var row = vm.Rows.Single();
                await File.WriteAllTextAsync(Path.Combine(directory, "outcome.json"), JsonSerializer.Serialize(row.Result));
                if (row.Result.State != OperationState.Succeeded || vm.HasProblems || !row.HasOutput ||
                    row.Result.Publication?.Outcome != PublicationOutcome.CopyCreated || prompts != (scene.Alpha && target == ImageFormat.Jpeg ? 1 : 0))
                    throw new InvalidDataException("Image review conversion failed: " + row.Status);
                var output = Directory.GetFiles(outputs).Single();
                var facts = await worker.ProbeAsync(new(Guid.NewGuid(), output), default);
                var reference = Path.Combine(pack, "references", scene.Name + (target == ImageFormat.Png ? "-png.png" : "-jpeg.png"));
                var expected = Pixels(reference, out var width, out var height);
                var actual = Pixels(output, out var outputWidth, out var outputHeight);
                if (facts.Format != target || facts.Width != width || facts.Height != height || outputWidth != width || outputHeight != height ||
                    facts.BitDepth != (scene.Bits > 8 && target == ImageFormat.Png ? 16 : 8) ||
                    facts.HasTransparency != (scene.Alpha && target == ImageFormat.Png) || facts.Orientation is not (0 or 1) || !facts.ProfileNames.Contains("icc"))
                    throw new InvalidDataException("Review output geometry, alpha, precision, orientation or color profile differs.");
                double squares = 0; var count = 0; var levels = new HashSet<ushort>();
                for (var pixel = 0; pixel < actual.Length; pixel += 4)
                {
                    if (Math.Abs(actual[pixel + 3] - expected[pixel + 3]) > 257)
                        throw new InvalidDataException("Output alpha differs from the explicit source.");
                    levels.Add(actual[pixel]);
                    if (expected[pixel + 3] == 0) continue;
                    for (var channel = 0; channel < 3; channel++)
                    { squares += Math.Pow((actual[pixel + channel] - expected[pixel + channel]) / 257.0, 2); count++; }
                }
                var rmse = Math.Sqrt(squares / count);
                var limit = target == ImageFormat.Jpeg ? 8 : scene.Name == "gradient" ? 0.1 : 3;
                if (rmse > limit || scene.Name == "gradient" && target == ImageFormat.Png && levels.Count <= 1024)
                    throw new InvalidDataException($"Independent image comparison failed: {name}, RGB RMSE {rmse}, levels {levels.Count}.");
                rows.Add(new { Case = name, Scene = scene.Name, Input = source, InputSha256 = Hash(source),
                    Target = target.ToString(), Output = output, OutputSha256 = Hash(output), Reference = reference, ReferenceSha256 = Hash(reference),
                    Width = width, Height = height, facts.BitDepth, facts.HasTransparency, Matte = scene.Alpha && target == ImageFormat.Jpeg ? "White, explicitly confirmed" : "None",
                    RgbRmse = rmse, RgbRmseLimit = limit, RedLevels = levels.Count, HumanReview = "Not performed" });
            }
            if (Directory.EnumerateFileSystemEntries(records).Any() || Directory.EnumerateDirectories(scratch).Any() ||
                Directory.EnumerateFiles(outputs, ".context-suite-*.tmp").Any()) throw new IOException("Review scratch remains.");
            Console.WriteLine("Prepared image comparison: " + name);
        }
        if (rows.Count != 16 || originals.Any(item => Hash(item.Key) != item.Value.Hash || File.GetLastWriteTimeUtc(item.Key) != item.Value.Written))
            throw new IOException("Incomplete image review or changed original.");
        await File.WriteAllTextAsync(Path.Combine(pack, "results.json"), JsonSerializer.Serialize(new
        {
            AutomatedPassed = true, HumanReview = "Not performed", Comparisons = rows,
            Scope = "Sixteen copies from four scenes, two HEIF inputs and two targets; authored references and Windows output decoding. Human/display and final-candidate acceptance remain open."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Owned(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.Contains("\\.codex-temp\\heif-review\\", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) != "pack")
            throw new ArgumentException("Use an owned HEIF review pack.");
        return path;
    }

    private static ushort[] Pixels(string path, out int width, out int height)
    {
        using var input = File.OpenRead(path);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Rgba64, null, 0);
        width = bitmap.PixelWidth; height = bitmap.PixelHeight;
        var pixels = new ushort[width * height * 4]; bitmap.CopyPixels(pixels, width * 8, 0); return pixels;
    }

    private static void SavePng(string path, int width, int height, ushort[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgba64, null, pixels, width * 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew); encoder.Save(output);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Image review originals must be preserved.");
    }
}
