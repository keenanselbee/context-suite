using System.Buffers.Binary;
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
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

internal static class PdfPerformanceContracts
{
    private sealed record Output(string Path, string Sha256, long Bytes);
    private sealed record Sample(string Operation, int Iteration, double Seconds, int WorkerPid,
        long WorkerPeakWorkingSetBytes, int OrderPrompts, Output[] Outputs);

    internal static async Task RunAsync(string evidence, string executable)
    {
        evidence = Path.GetFullPath(evidence);
        if (!evidence.Contains("\\.codex-temp\\pdf-performance\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(evidence) != "runs" || Directory.Exists(evidence))
            throw new ArgumentException("Use fresh isolated PDF performance evidence.");
        Directory.CreateDirectory(evidence);
        var sources = Path.Combine(evidence, "sources"); Directory.CreateDirectory(sources);
        var pdf = Path.Combine(sources, "Ten text and vector pages.pdf");
        WritePdf(pdf);
        var images = Enumerable.Range(1, 10).Select(index => Path.Combine(sources, $"Image {index:D2}.bmp")).ToArray();
        for (var index = 0; index < images.Length; index++) WriteBitmap(images[index], index + 1);
        var originals = images.Append(pdf).ToDictionary(path => path,
            path => new { Sha256 = Hash(path), Written = File.GetLastWriteTimeUtc(path), Bytes = new FileInfo(path).Length });
        var samples = new List<Sample>();
        // Interleave the two operations; every batch creates a fresh application
        // model and worker. Fixture generation and post-timing audits are excluded.
        for (var iteration = 0; iteration < 5; iteration++)
            foreach (var combine in new[] { false, true })
            {
                samples.Add(await MeasureAsync(combine, iteration));
                await File.WriteAllTextAsync(Path.Combine(evidence, "samples.json"),
                    JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{samples[^1].Operation} / {iteration + 1}: {samples[^1].Seconds:F3} s");
            }
        var groups = samples.GroupBy(sample => sample.Operation).Select(group =>
        {
            var times = group.Select(sample => sample.Seconds).Order().ToArray();
            return new { Operation = group.Key, Runs = times.Length, MedianSeconds = times[2],
                MaximumSeconds = times[^1], Passed = times.Length == 5 && times[2] <= 20 && times[^1] <= 40 };
        }).ToArray();
        var preserved = originals.All(pair => Hash(pair.Key) == pair.Value.Sha256 &&
            File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written);
        var passed = preserved && samples.Count == 10 && groups.Length == 2 && groups.All(group => group.Passed);
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, OriginalsPreserved = preserved, MedianLimitSeconds = 20, MaximumLimitSeconds = 40,
            Groups = groups, Samples = samples, Sources = originals,
            Machine = new { OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(),
                LogicalProcessors = Environment.ProcessorCount, Is64Bit = Environment.Is64BitProcess },
            Scope = "Ten Letter text/vector pages to PNG at 150 DPI; ten 1600x1200 RGB BMP images to one ordered PDF. " +
                "Fresh-worker application command timing includes probe, access, automated order confirmation, validation and publication. " +
                "Excludes fixture generation, source/output audit hashing, post-export inspection, disposal and human decision time. " +
                "Managed worker peak working set only; native job peak, cold storage and human/final-candidate acceptance are unmeasured."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("PDF performance budget missed; retain recorded samples.");
        Console.WriteLine("Both PDF batch operations meet the declared budgets across ten fresh workers.");

        async Task<Sample> MeasureAsync(bool combine, int iteration)
        {
            var operation = combine ? "images-to-pdf" : "pdf-to-png";
            var directory = Path.Combine(evidence, operation + "-" + iteration); Directory.CreateDirectory(directory);
            var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(outputs);
            var records = Path.Combine(directory, "publications");
            var scratch = Path.Combine(directory, "worker");
            var prompts = 0;
            Process? process = null;
            Sample sample;
            var watch = Stopwatch.StartNew();
            try
            {
                await using (var worker = new WorkerClient(executable, scratch))
                await using (var vm = new MainViewModel(worker,
                    new SuiteSettings { Convert = new(OutputDirectory: outputs), PlayCompletionSound = false },
                    new OutputPublisher(records, new ForbiddenRecycle()), new LocalTrialStore(Path.Combine(directory, "trial.json"))))
                {
                    vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Routine PDF commands must not open a planner.");
                    vm.ImagePdfOrderRequested += async (decision, token) =>
                    {
                        token.ThrowIfCancellationRequested(); prompts++;
                        if (!combine || !decision.Pages.Select(page => page.Page.Source.Path).SequenceEqual(images))
                            throw new InvalidDataException("Unexpected image order review.");
                        await decision.RefreshAccessAsync();
                        ConfirmedImagePdf? confirmed = null;
                        decision.Confirmed += value => confirmed = value;
                        decision.ConfirmCommand.Execute(null);
                        return confirmed ?? throw new InvalidOperationException("Benchmark order review was not confirmed.");
                    };
                    vm.Admit(new(Guid.NewGuid(), "convert", combine ? "pdf" : "png", combine ? images.ToImmutableArray() : [pdf]));
                    await vm.WaitForIdleAsync();
                    watch.Stop();
                    var row = vm.Rows.Single();
                    if (row.Result.State != OperationState.Succeeded || vm.HasProblems || prompts != (combine ? 1 : 0) ||
                        row.Result.EngineIdentity is null || !row.HasOutput || (!combine && row.SavedPdfPages != 10))
                        throw new InvalidDataException("PDF benchmark failed to publish its complete batch: " + row.Status);
                    process = Process.GetProcessById(worker.ProcessId ?? throw new IOException("Worker did not start."));
                    process.Refresh();
                    var peak = process.PeakWorkingSet64;
                    var paths = Directory.GetFiles(outputs).Order(StringComparer.Ordinal).ToArray();
                    if (paths.Length != (combine ? 1 : 10)) throw new InvalidDataException("Unexpected output count.");
                    if (combine)
                    {
                        if (!row.ReviewedImagePdf!.Pages.Select(page => page.Source.Path).SequenceEqual(images) ||
                            Path.GetFileName(paths[0]) != "Image 01 - Combined.pdf" ||
                            row.Result.Publication?.Outcome != PublicationOutcome.CopyCreated)
                            throw new InvalidDataException("Combined PDF order or copy publication changed.");
                        var probe = await worker.ProbePdfPagesAsync(new(Guid.NewGuid(), paths[0]), default);
                        if (probe.Document.Pages.Length != 10 || probe.Document.Pages.Any(page =>
                            page.WidthPoints != 1200 || page.HeightPoints != 900))
                            throw new InvalidDataException("Combined PDF page count or physical dimensions changed.");
                    }
                    else
                        for (var index = 0; index < paths.Length; index++) CheckPage(paths[index], index + 1);
                    sample = new(operation, iteration, watch.Elapsed.TotalSeconds, process.Id, peak, prompts,
                        paths.Select(path => new Output(path, Hash(path), new FileInfo(path).Length)).ToArray());
                }
                if (!process.HasExited || Directory.EnumerateFiles(records).Any() ||
                    Directory.EnumerateDirectories(scratch).Any() ||
                    Directory.EnumerateFiles(directory, ".context-suite-*.tmp", SearchOption.AllDirectories).Any() ||
                    originals.Any(pair => Hash(pair.Key) != pair.Value.Sha256 || File.GetLastWriteTimeUtc(pair.Key) != pair.Value.Written))
                    throw new IOException("PDF benchmark changed a source or left a worker/publication artifact.");
                return sample;
            }
            finally { process?.Dispose(); }
        }
    }

    private static void CheckPage(string path, int number)
    {
        if (Path.GetFileName(path) != $"Ten text and vector pages - Page {number:D3}.png")
            throw new InvalidDataException("Unexpected page filename/order.");
        using var input = File.OpenRead(path);
        var frame = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth != 1275 || bitmap.PixelHeight != 1650) throw new InvalidDataException("PDF PNG geometry changed.");
        var pixels = new byte[1275 * 1650 * 4]; bitmap.CopyPixels(pixels, 1275 * 4, 0);
        // Center of the authored page marker: PDF (306,678) points, top-down PNG.
        var offset = (237 * 1275 + 637) * 4;
        if (pixels[offset] != 0 || pixels[offset + 1] != 0 || Math.Abs(pixels[offset + 2] - number * 20) > 1 || pixels[offset + 3] != 255)
            throw new InvalidDataException("Page marker content/order changed.");
        var ink = 0;
        for (var y = 330; y < 1250; y++)
        for (var x = 145; x < 1150; x++)
        {
            offset = (y * 1275 + x) * 4;
            if (pixels[offset] < 32 && pixels[offset + 1] < 32 && pixels[offset + 2] < 32 && pixels[offset + 3] == 255) ink++;
        }
        if (ink < 1000) throw new InvalidDataException("Expected text/vector ink is missing.");
    }

    private static void WriteBitmap(string path, int number)
    {
        const int width = 1600, height = 1200;
        var bytes = new byte[54 + width * height * 3]; bytes[0] = 66; bytes[1] = 77;
        foreach (var (offset, value) in new[] { (2, bytes.Length), (10, 54), (14, 40), (18, width), (22, height), (34, width * height * 3) })
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        bytes[26] = 1; bytes[28] = 24;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = 54 + ((height - 1 - y) * width + x) * 3;
            bytes[offset] = (byte)(number * 20); bytes[offset + 1] = (byte)(y * 255 / (height - 1));
            bytes[offset + 2] = (byte)(x * 255 / (width - 1));
        }
        using var file = new FileStream(path, FileMode.CreateNew); file.Write(bytes);
    }

    private static void WritePdf(string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew);
        var offsets = new List<long> { 0 };
        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Count 10 /Kids [" + string.Join(" ", Enumerable.Range(0, 10).Select(index => $"{4 + index * 2} 0 R")) + "] >>");
        Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for (var page = 1; page <= 10; page++)
        {
            var id = 2 + page * 2;
            Object(id, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {id + 1} 0 R >>");
            var content = new StringBuilder("1 1 1 rg 0 0 612 792 re f\n");
            content.Append((page * 20 / 255d).ToString("R", CultureInfo.InvariantCulture)).Append(" 0 0 rg 72 660 468 36 re f\n");
            content.Append("0 0 0 rg BT /F1 18 Tf 72 720 Td (Context Suite PDF performance - page ").Append(page).Append(") Tj ET\n");
            for (var line = 0; line < 30; line++)
                content.Append("BT /F1 12 Tf 72 ").Append(620 - line * 14).Append(" Td (Page ").Append(page)
                    .Append(" line ").Append(line + 1).Append(": Text and vector content must retain its page order.) Tj ET\n");
            content.Append("0 0 0 RG 1 w 72 190 m 540 190 l S\n");
            Object(id + 1, "<< /Length " + content.Length + " >>\nstream\n" + content + "endstream");
        }
        var xref = file.Position; Write("xref\n0 24\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size 24 /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        void Write(string value) => file.Write(Encoding.ASCII.GetBytes(value));
        void Object(int id, string body) { offsets.Add(file.Position); Write(id + " 0 obj\n" + body + "\nendobj\n"); }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input));
    }

    private sealed class ForbiddenRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("PDF performance sources must remain copies.");
    }
}
