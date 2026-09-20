using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static class PdfReviewContracts
{
    private sealed record Fixture(string Name, string Path, int Width, int Height, byte[]? Pixels, string Instructions);

    internal static async Task RunAsync(string evidence, string executable)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\pdf-review\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "runs" || Directory.Exists(evidence))
            throw new ArgumentException("Use fresh isolated PDF review evidence.");
        Directory.CreateDirectory(evidence);
        var sources = Path.Combine(evidence, "sources"); Directory.CreateDirectory(sources);
        var fixtures = Prepare(sources);
        var originals = Directory.GetFiles(sources).ToDictionary(path => path,
            path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path) });
        var output = Path.Combine(evidence, "outputs"); Directory.CreateDirectory(output);
        var records = Path.Combine(evidence, "records"); var scratch = Path.Combine(evidence, "worker");
        var observations = new List<object>();
        var checks = new List<string>();
        Process? process = null;
        try
        {
            await using (var worker = new WorkerClient(executable, scratch))
            await using (var vm = new MainViewModel(worker,
                new SuiteSettings { Convert = new(OutputDirectory: output), PlayCompletionSound = false },
                new OutputPublisher(records, new ForbiddenRecycle()), new LocalTrialStore(Path.Combine(evidence, "trial.json"))))
            {
                vm.ConversionRequested += (_, _) => throw new InvalidOperationException("PDF review commands must not open a planner.");
                vm.Admit(new(Guid.NewGuid(), "convert", "png", fixtures.Select(item => item.Path).ToImmutableArray()));
                await vm.WaitForIdleAsync();
                Check(vm.Rows.Count == 4 && !vm.HasProblems, "four PDF scenes complete in one direct batch");
                process = Process.GetProcessById(worker.ProcessId ?? throw new IOException("Worker did not start."));
                foreach (var fixture in fixtures)
                {
                    var row = vm.Rows.Single(item => item.Path == fixture.Path);
                    Check(row.Result.State == OperationState.Succeeded && row.SavedPdfPages == 1 && row.HasOutput,
                        fixture.Name + ": exactly one validated page publishes");
                    var path = Path.Combine(output, fixture.Name + " - Page 001.png");
                    Check(File.Exists(path) && row.Result.EngineIdentity is not null, fixture.Name + ": named output and validation identity retained");
                    var (width, height, pixels) = Decode(path);
                    Check(width == fixture.Width && height == fixture.Height, fixture.Name + ": fixed 150-DPI geometry matches");
                    if (fixture.Pixels is not null)
                        Check(pixels.SequenceEqual(fixture.Pixels), fixture.Name + ": every authored pixel matches the independent bitmap expectation");
                    else
                    {
                        var red = 0; var blue = 0; var ink = 0;
                        for (var index = 0; index < pixels.Length; index += 4)
                        {
                            if (pixels[index] == 0 && pixels[index + 1] == 0 && pixels[index + 2] == 255 && pixels[index + 3] == 255) red++;
                            if (pixels[index] == 255 && pixels[index + 1] == 0 && pixels[index + 2] == 0 && pixels[index + 3] == 255) blue++;
                            if (pixels[index] < 32 && pixels[index + 1] < 32 && pixels[index + 2] < 32 && pixels[index + 3] == 255) ink++;
                        }
                        Check(red > 4000 && blue > 4000 && ink > 300, fixture.Name + ": both vector markers and text/rule ink remain visible");
                    }
                    observations.Add(new { fixture.Name, Source = fixture.Path, SourceSha256 = Hash(fixture.Path),
                        Output = path, OutputSha256 = Hash(path), Width = width, Height = height,
                        DecodedPixelsSha256 = Convert.ToHexString(SHA256.HashData(pixels)),
                        ExpectedPixelsSha256 = fixture.Pixels is null ? null : Convert.ToHexString(SHA256.HashData(fixture.Pixels)),
                        Reference = fixture.Pixels is null ? null : Path.Combine(sources, fixture.Name + " reference.png"),
                        ReferenceSha256 = fixture.Pixels is null ? null : Hash(Path.Combine(sources, fixture.Name + " reference.png")),
                        fixture.Instructions, AutomatedScope = fixture.Pixels is null ? "Geometry, vector markers and visible ink; exact typography remains human review." : "Complete authored bitmap equality." });
                }
            }
            Check(process is not null && process.HasExited, "worker exits after model disposal");
            Check(Directory.GetFiles(output).Length == 4, "only the four named page copies exist");
            Check(originals.All(pair => Hash(pair.Key) == pair.Value.Sha256 && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
                "all PDF and reference originals retain bytes and modification times");
            Check(!Directory.EnumerateFiles(records).Any() && !Directory.EnumerateDirectories(scratch).Any() &&
                !Directory.EnumerateFiles(evidence, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(),
                "no publication journal, worker scratch or temporary output remains");
            await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Checks = checks, Worker = Path.GetFullPath(executable), WorkerSha256 = Hash(executable),
                Sources = originals, Observations = observations, HumanAcceptance = "Not reviewed",
                Scope = "Four authored PDF scenes exported through the actual application command. Synthetic scan only; no real-document, optimization, human or final-candidate acceptance."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { process?.Dispose(); }
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks.Add(message); Console.WriteLine("PASS: " + message);
        }
    }

    private static Fixture[] Prepare(string folder)
    {
        var scan = new byte[300 * 400 * 4];
        for (var y = 0; y < 400; y++)
        for (var x = 0; x < 300; x++)
        {
            var offset = (y * 300 + x) * 4;
            var shade = (byte)(255 - (x * 17 + y * 31) % 7);
            if (x is >= 25 and < 275 && y is >= 100 and < 340 && y % 24 < 3) shade = 30;
            if (y is >= 365 and < 380 && x is >= 25 and < 100) shade = 80;
            scan[offset] = scan[offset + 1] = scan[offset + 2] = shade; scan[offset + 3] = 255;
        }
        // Independently authored 5x7 block letters: a bitmap scan has no PDF text layer.
        string[] glyphs = ["01111/10000/10000/01110/00001/00001/11110", "01110/10001/10000/10000/10000/10001/01110",
            "01110/10001/10001/11111/10001/10001/10001", "10001/11001/10101/10011/10001/10001/10001"];
        for (var letter = 0; letter < glyphs.Length; letter++)
        {
            var rows = glyphs[letter].Split('/');
            for (var y = 0; y < 7; y++)
            for (var x = 0; x < 5; x++)
                if (rows[y][x] == '1')
                    for (var dy = 0; dy < 4; dy++)
                    for (var dx = 0; dx < 4; dx++)
                    {
                        var offset = ((30 + y * 4 + dy) * 300 + 25 + letter * 28 + x * 4 + dx) * 4;
                        scan[offset] = scan[offset + 1] = scan[offset + 2] = 20;
                    }
        }
        var alpha = new byte[100 * 100 * 4];
        byte[][] colors = [[0, 0, 255, 255], [255, 0, 0, 128], [0, 255, 0, 64], [0, 0, 0, 0]];
        for (var y = 0; y < 100; y++)
        for (var x = 0; x < 100; x++) colors[(y < 50 ? 0 : 2) + (x < 50 ? 0 : 1)].CopyTo(alpha, (y * 100 + x) * 4);
        var rotated = new byte[150 * 300 * 4];
        byte[][] clockwise = [[0, 255, 0, 255], [0, 0, 255, 255], [0, 0, 0, 0], [255, 0, 0, 255]];
        for (var y = 0; y < 300; y++)
        for (var x = 0; x < 150; x++) clockwise[(y < 150 ? 0 : 2) + (x < 75 ? 0 : 1)].CopyTo(rotated, (y * 150 + x) * 4);
        var definitions = new[]
        {
            new Fixture("Text and vectors", Path.Combine(folder, "Text and vectors.pdf"), 600, 800, null,
                "Read PDF REVIEW, Text and vector content, and Page 1 of 1. Check red/blue rectangles and the horizontal rule. Compare the original PDF with the PNG; automated ink checks do not prove every glyph."),
            new Fixture("Synthetic scanned page", Path.Combine(folder, "Synthetic scanned page.pdf"), 300, 400, scan,
                "This is an authored image-only scan control, not a real scan. Compare SCAN, ruled lines, gray marker and paper texture with the retained bitmap. There is no OCR or selectable PDF text."),
            new Fixture("Rotated cropped page", Path.Combine(folder, "Rotated cropped page.pdf"), 150, 300, rotated,
                "Crop the central half of the page and rotate clockwise 90 degrees. Expected displayed quadrants: green/red across the top, transparent/blue across the bottom."),
            new Fixture("Transparent image", Path.Combine(folder, "Transparent image.pdf"), 100, 100, alpha,
                "Compare red at full opacity, blue at alpha 128, green at alpha 64 and the fully transparent quadrant. Viewer white backgrounds can hide transparency; inspect the PNG in an alpha-aware viewer.")
        };
        WritePdf(definitions[0].Path, "0 0 288 384", "", "BT /F1 20 Tf 24 344 Td (PDF REVIEW) Tj ET\nBT /F1 12 Tf 24 316 Td (Text and vector content) Tj ET\nBT /F1 10 Tf 24 24 Td (Page 1 of 1) Tj ET\n1 0 0 rg 24 220 96 36 re f\n0 0 1 rg 150 220 96 36 re f\n0 0 0 RG 1 w 24 190 m 264 190 l S\n", null, 0, 0);
        WritePdf(definitions[1].Path, "0 0 144 192", "", "q 144 0 0 192 0 0 cm /Im0 Do Q\n", scan, 300, 400);
        WritePdf(definitions[2].Path, "0 0 288 144", "/CropBox [72 36 216 108] /Rotate 90", "1 0 0 rg 0 72 144 72 re f\n0 0 1 rg 144 72 144 72 re f\n0 1 0 rg 0 0 144 72 re f\n", null, 0, 0);
        WritePdf(definitions[3].Path, "0 0 48 48", "", "q 48 0 0 48 0 0 cm /Im0 Do Q\n", alpha, 100, 100);
        foreach (var fixture in definitions.Where(item => item.Pixels is not null))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(fixture.Width, fixture.Height, 150, 150,
                PixelFormats.Bgra32, null, fixture.Pixels!, fixture.Width * 4)));
            using var stream = new FileStream(Path.Combine(folder, fixture.Name + " reference.png"), FileMode.CreateNew);
            encoder.Save(stream);
        }
        return definitions;
    }

    private static void WritePdf(string path, string media, string pageOptions, string content, byte[]? image, int width, int height)
    {
        static byte[] Text(string value) => Encoding.ASCII.GetBytes(value);
        static byte[] Stream(string dictionary, byte[] data) => [.. Text($"<< {dictionary} /Length {data.Length} >>\nstream\n"), .. data, .. Text("\nendstream")];
        var objects = new List<byte[]>
        {
            Text("<< /Type /Catalog /Pages 2 0 R >>"), Text("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [{media}] {pageOptions} /Resources << /Font << /F1 5 0 R >>" +
                (image is null ? "" : " /XObject << /Im0 6 0 R >>") + " >> /Contents 4 0 R >>"),
            Stream("", Text(content)), Text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
        };
        if (image is not null)
        {
            var rgb = new byte[width * height * 3]; var alpha = new byte[width * height];
            for (var index = 0; index < alpha.Length; index++)
            {
                rgb[index * 3] = image[index * 4 + 2]; rgb[index * 3 + 1] = image[index * 4 + 1]; rgb[index * 3 + 2] = image[index * 4]; alpha[index] = image[index * 4 + 3];
            }
            objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Interpolate false /SMask 7 0 R", rgb));
            objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Interpolate false", alpha));
        }
        using var file = new FileStream(path, FileMode.CreateNew);
        file.Write(Text("%PDF-1.4\n")); var positions = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            positions.Add(file.Position); file.Write(Text($"{index + 1} 0 obj\n")); file.Write(objects[index]); file.Write(Text("\nendobj\n"));
        }
        var xref = file.Position; file.Write(Text($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var position in positions) file.Write(Text(position.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        file.Write(Text($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
    }

    private static (int Width, int Height, byte[] Pixels) Decode(string path)
    {
        using var input = File.OpenRead(path);
        var frame = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return (bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF review originals must remain copies.");
    }
}
