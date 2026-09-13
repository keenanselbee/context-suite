using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

internal static class PdfPageGeometryContracts
{
    public static async Task RunAsync(string root, string executable, Action<bool, string> check)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new IOException("Use fresh absolute PDF geometry scratch.");
        Directory.CreateDirectory(root);
        var observations = new List<object>();
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();
        var records = Path.Combine(root, "records");
        var outputDirectory = root;
        var publisher = new OutputPublisher(records, new ForbiddenRecycle(), replacementVerified: true);
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        var executor = new PdfPageConversionExecutor(worker, publisher, new LocalTrialStore(Path.Combine(root, "trial.json")));
        foreach (var rotation in new[] { 0, 90, 180, 270 })
            await Complete("cropped-rotate-" + rotation, "0 0 288 144", "/CropBox [72 36 216 108] /Rotate " + rotation,
                Quadrants(288, 144), rotation % 180 == 0 ? 300 : 150, rotation % 180 == 0 ? 150 : 300, rotation / 90, false);
        await Complete("maximum-pixels", "0 0 1920 1920", "", Quadrants(1920, 1920), 4000, 4000, 0, false);
        // Float page dimensions are deliberately just below the pixel edge;
        // ceil at 150 DPI produces exactly the admitted 16,384-pixel dimension.
        await Complete("maximum-width", "0 0 7864.319 0.48", "", "0 1 0 rg -1 -1 8000 8000 re f\n", 16384, 1, 0, true);
        await Complete("maximum-height", "0 0 0.48 7864.319", "", "0 1 0 rg -1 -1 8000 8000 re f\n", 1, 16384, 0, true);
        foreach (var (name, box) in new[] { ("over-pixels", "0 0 1921 1920"), ("over-width", "0 0 7865 0.48"), ("over-height", "0 0 0.48 7865") })
        {
            var path = WritePdf(name, box, "", "0 1 0 rg 0 0 1 1 re f\n");
            var before = Directory.GetFiles(outputDirectory).Length;
            var watch = Stopwatch.StartNew();
            try { await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), path), default); throw new Exception("Oversized page accepted: " + name); }
            catch (MediaWorkerException error)
            {
                check(error.Failure == ImageFailure.InvalidInput, "PDF geometry: renderer refuses out-of-bound page " + name);
                observations.Add(new { Name = name, Failure = error.Failure.ToString(), ElapsedMs = watch.ElapsedMilliseconds });
            }
            check(Directory.GetFiles(outputDirectory).Length == before && !Directory.EnumerateFiles(records).Any(),
                "PDF geometry: rejected inspection starts no publication " + name);
        }
        await Complete("after-refusals", "0 0 144 72", "", Quadrants(144, 72), 300, 150, 0, false);
        foreach (var (path, expected) in originals)
            check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))) == expected.Hash && File.GetLastWriteTimeUtc(path) == expected.Written,
                "PDF geometry: original hash and timestamp retained " + Path.GetFileName(path));
        check(!Directory.GetFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() && !Directory.EnumerateFiles(records).Any(),
            "PDF geometry: output reservations and journals cleaned");
        await File.WriteAllTextAsync(Path.Combine(root, "pdf-page-geometry.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));

        async Task Complete(string name, string box, string extras, string content, int width, int height, int rotation, bool solid)
        {
            var path = WritePdf(name, box, extras, content); var watch = Stopwatch.StartNew();
            var source = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), path), default);
            var page = source.Document.Pages.Single();
            check(page.Width == width && page.Height == height && page.Rotation == rotation,
                "PDF geometry: native crop/rotation/size agrees with authored page " + name);
            var expectedPixels = ExpectedPixels(width, height, rotation, solid);
            var settings = new ContextSuite.Core.Settings.BatchSettings("convert", new(ReplaceOriginals: true));
            var plan = PdfPageConversionPlan.Create(Guid.NewGuid(), [source], settings).Confirm();
            var completed = await executor.ExecuteAsync(plan, null, default);
            var result = completed.Pages.Single().Result;
            check(result.State == OperationState.Succeeded && result.Publication?.Outcome == PublicationOutcome.CopyCreated,
                "PDF geometry: validated page copy despite overwrite preference " + name + " " + result.Message);
            var output = result.Publication!.OutputPath!;
            check(Path.GetDirectoryName(output) == root, "PDF geometry: copy remains beside the original without an alternate-folder exception " + name);
            var decoded = DecodePng(output);
            check(decoded.Width == width && decoded.Height == height && decoded.Hash == expectedPixels,
                "PDF geometry: published PNG has exact authored color/alpha orientation " + name);
            using var process = Process.GetProcessById(worker.ProcessId!.Value);
            observations.Add(new { Name = name, Input = path, Output = output, Width = width, Height = height, Rotation = rotation,
                ExpectedPixels = expectedPixels, DecodedPixels = decoded.Hash, SourceSha256 = originals[path].Hash,
                OutputSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output))), ElapsedMs = watch.ElapsedMilliseconds,
                WorkerPeakWorkingSet = process.PeakWorkingSet64 });
            Console.WriteLine("Verified PDF geometry " + name);
        }
        string WritePdf(string name, string box, string extras, string content)
        {
            var path = Path.Combine(root, name + ".pdf");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var offsets = new List<long> { 0 };
                Write("%PDF-1.4\n");
                Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
                Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
                Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [" + box + "] " + extras + " /Resources << >> /Contents 4 0 R >>");
                Object(4, "<< /Length " + content.Length + " >>\nstream\n" + content + "endstream");
                var xref = file.Position; Write("xref\n0 5\n0000000000 65535 f \n");
                foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                void Write(string text) => file.Write(Encoding.ASCII.GetBytes(text));
                void Object(int id, string body) { offsets.Add(file.Position); Write(id + " 0 obj\n" + body + "\nendobj\n"); }
            }
            originals.Add(path, (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path)));
            return path;
        }
    }

    private static string Quadrants(int width, int height) =>
        $"1 0 0 rg 0 {height / 2} {width / 2} {height / 2} re f\n0 0 1 rg {width / 2} {height / 2} {width / 2} {height / 2} re f\n0 1 0 rg 0 0 {width / 2} {height / 2} re f\n";

    private static string ExpectedPixels(int width, int height, int rotation, bool solid)
    {
        // Source quadrants in displayed top-down order: red, blue, green,
        // transparent. PDF Rotate is clockwise. Hash rows without a full bitmap.
        int[][] orders = [[0, 1, 2, 3], [2, 0, 3, 1], [3, 2, 1, 0], [1, 3, 0, 2]];
        byte[][] colors = [[0, 0, 255, 255], [255, 0, 0, 255], [0, 255, 0, 255], [0, 0, 0, 0]];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++) colors[solid ? 2 : orders[rotation][(y < height / 2 ? 0 : 2) + (x < width / 2 ? 0 : 1)]].CopyTo(row, x * 4);
            hash.AppendData(row);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static (int Width, int Height, string Hash) DecodePng(string path)
    {
        using var input = File.OpenRead(path);
        var bitmap = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return (converted.PixelWidth, converted.PixelHeight, Convert.ToHexString(SHA256.HashData(pixels)));
    }
    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF geometry tests cannot recycle originals.");
    }
}
