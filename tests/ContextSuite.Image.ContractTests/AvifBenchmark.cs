using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;

internal static class AvifBenchmark
{
    public static void Run(string root, uint width, uint height, string pattern)
    {
        if (Directory.Exists(root) || width is 0 or > 16384 || height is 0 or > 16384 ||
            (ulong)width * height > 40_000_000 || pattern is not ("scene" or "noise" or "lineart"))
            throw new ArgumentException("Use a fresh bounded benchmark and known pattern.");
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "authored.png");
        using (var file = File.Create(input))
        {
            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var header = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header, width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8; header[9] = 6; file.Write(PngFixture.Chunk("IHDR", header));
            using var compressed = new MemoryStream();
            using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            {
                var row = new byte[width * 4 + 1];
                var filtered = new byte[row.Length]; filtered[0] = 1;
                for (var y = 0u; y < height; y++)
                {
                    for (var x = 0u; x < width; x++) Fill(row.AsSpan(checked((int)x * 4 + 1), 4), x, y, width, height, pattern);
                    // PNG Sub filtering keeps the maximum-pixel fixture inside
                    // the existing compressed-source byte cap without changing pixels.
                    for (var at = 1; at < row.Length; at++)
                        filtered[at] = unchecked((byte)(row[at] - (at > 4 ? row[at - 4] : 0)));
                    zip.Write(filtered);
                }
            }
            file.Write(PngFixture.Chunk("IDAT", compressed.ToArray())); file.Write(PngFixture.Chunk("IEND", []));
        }
        var original = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        var facts = adapter.Probe(new(Guid.NewGuid(), input), default);
        var options = new ImageConversionOptions(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality, Metadata: ImageMetadataMode.Automatic);
        var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new()));
        if (!plan.Items[0].CanExecute) throw new InvalidDataException(plan.Items[0].BlockReason);
        var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp"); using (File.Create(temporary)) { }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var timer = Stopwatch.StartNew();
        var result = adapter.Convert(new(plan.Items[0], options, temporary), deadline.Token);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        if (elapsed >= 120000) throw new InvalidOperationException("AVIF benchmark exceeded the worker conversion deadline.");
        var decoded = HeifCodec.Decode(File.ReadAllBytes(temporary), default);
        double squared = 0; long samples = 0; int maximum = 0, alphaMismatches = 0;
        Span<byte> expected = stackalloc byte[4];
        for (var y = 0u; y < height; y++)
        for (var x = 0u; x < width; x++)
        {
            Fill(expected, x, y, width, height, pattern);
            var at = checked((int)((y * width + x) * 8));
            if (BinaryPrimitives.ReadUInt16LittleEndian(decoded.Pixels.AsSpan(at + 6, 2)) != expected[3] * 257) alphaMismatches++;
            if (expected[3] == 0) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var actual = (BinaryPrimitives.ReadUInt16LittleEndian(decoded.Pixels.AsSpan(at + channel * 2, 2)) + 128) / 257;
                var error = Math.Abs(actual - expected[channel]); squared += error * error; samples++; maximum = Math.Max(maximum, error);
            }
        }
        var rmse = Math.Sqrt(squared / Math.Max(1, samples));
        var sourceUnchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) == original;
        var passed = alphaMismatches == 0 && rmse <= 6 && sourceUnchanged;
        using var process = Process.GetCurrentProcess();
        var report = new { Passed = passed, SourceUnchanged = sourceUnchanged, Pattern = pattern, Width = width, Height = height, Pixels = (ulong)width * height,
            Quality = ImageConversionOptions.AvifQuality, Speed = 6, Threads = 2, InputBytes = facts.FileBytes, OutputBytes = result.OutputBytes,
            ConversionMilliseconds = elapsed, VisibleRgbRmse = rmse, MaximumVisibleRgbError = maximum, AlphaMismatches = alphaMismatches,
            ProcessPeakPagedMemoryBytes = process.PeakPagedMemorySize64, ProcessPeakWorkingSetBytes = process.PeakWorkingSet64,
            InputSha256 = original, OutputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporary))) };
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (!passed) throw new InvalidOperationException("AVIF benchmark fidelity or source retention failed; see results.json.");
    }

    private static void Fill(Span<byte> pixel, uint x, uint y, uint width, uint height, string pattern)
    {
        uint noise = unchecked((x + y * width + 1) * 747796405u + 2891336453u);
        noise = unchecked(((noise >> (int)((noise >> 28) + 4)) ^ noise) * 277803737u); noise = (noise >> 22) ^ noise;
        if (pattern == "noise")
        { pixel[0] = (byte)noise; pixel[1] = (byte)(noise >> 8); pixel[2] = (byte)(noise >> 16); pixel[3] = 255; return; }
        if (pattern == "lineart")
        {
            var line = x % 37 < 2 || y % 29 < 2;
            pixel[0] = line ? (byte)245 : (byte)15; pixel[1] = line ? (byte)25 : (byte)225;
            pixel[2] = (x / 9 + y / 11) % 2 == 0 ? (byte)40 : (byte)220;
            pixel[3] = (byte)((x % 256)); return;
        }
        var detail = (int)(noise & 15) - 7;
        pixel[0] = (byte)Math.Clamp((int)(x * 255 / Math.Max(1u, width - 1)) + detail, 0, 255);
        pixel[1] = (byte)Math.Clamp((int)(y * 255 / Math.Max(1u, height - 1)) + detail, 0, 255);
        pixel[2] = (byte)Math.Clamp(100 + (int)(50 * Math.Sin(x / 97d) * Math.Cos(y / 131d)) + detail, 0, 255);
        pixel[3] = 255;
    }
}
