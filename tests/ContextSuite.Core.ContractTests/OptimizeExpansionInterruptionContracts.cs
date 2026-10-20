using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class OptimizeExpansionInterruptionContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "optimize-expansion-interruption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var jpegPath = Path.Combine(root, "noise.jpg");
        WriteJpeg(jpegPath);
        var pngPath = Path.Combine(root, "noise.png");
        ImageInterruptionContracts.WriteNoisePng(pngPath, 2048);
        var packagePath = Path.Combine(root, "authored.docx");
        WritePackage(packagePath);
        var records = Path.Combine(root, "records");
        var publisher = new OutputPublisher(records, new NoRecycle());
        var clock = new ImageInterruptionContracts.DeadlineClock();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"), clock);
        var webpPath = Path.Combine(root, "noise.webp");
        var png = await worker.ProbeAsync(new(Guid.NewGuid(), pngPath), default);
        var options = new ImageConversionOptions(ImageFormat.WebP, Quality: 100, WebPLossless: true);
        var conversion = ImageConversionPlanner.Create(Guid.NewGuid(), [png], options, new("convert", new()));
        var imageItem = conversion.Confirm(true, false, false).Plan.Items.Single();
        // Build one owned fixture without duplicating a production payload or
        // copying the large generated image between test directories.
        var converted = Path.Combine(root, $".context-suite-{png.ItemId:N}.tmp");
        await File.WriteAllBytesAsync(converted, []);
        await worker.ConvertAsync(new(imageItem, options, converted), default);
        File.Move(converted, webpPath);
        foreach (var (family, path) in new[] { ("JPEG", jpegPath), ("WebP", webpPath), ("Office", packagePath) })
        {
            var originalHash = SHA256.HashData(File.ReadAllBytes(path));
            var timestamp = File.GetLastWriteTimeUtc(path);
            foreach (var fault in new[] { "cancel", "timeout", "crash" })
            {
                ImageSourceFacts? image = null;
                OfficePackageSource? package = null;
                if (family == "Office") package = await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), path), default);
                else image = await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), path), default);
                var itemId = package?.ItemId ?? image!.ItemId;
                var extension = Path.GetExtension(path)[1..];
                var reservation = await publisher.ReserveAsync(new(itemId, path, extension, new("optimize", new())));
                var processId = worker.ProcessId ?? throw new InvalidOperationException("Probe did not start the owned worker.");
                using var active = Process.GetProcessById(processId);
                var cpuBefore = active.TotalProcessorTime;
                using var cancellation = new CancellationTokenSource();
                Task operation = package is not null
                    ? worker.OptimizeOfficePackageAsync(new(package, reservation.TemporaryPath), cancellation.Token)
                    : worker.OptimizeLosslessImageAsync(new(image!, reservation.TemporaryPath, LosslessImageOptimizationPlan.PolicyFor(image!.Format)), cancellation.Token);
                PublicationResult? abandoned = null;
                try
                {
                    var elapsed = Stopwatch.StartNew();
                    var observedActive = false;
                    while (!operation.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(20))
                    {
                        await Task.Delay(10);
                        if (operation.IsCompleted) break;
                        active.Refresh();
                        if (active.HasExited) break;
                        if (active.TotalProcessorTime - cpuBefore >= TimeSpan.FromMilliseconds(30))
                        {
                            observedActive = true;
                            break;
                        }
                    }
                    if (!observedActive || operation.IsCompleted)
                        throw new InvalidOperationException($"Did not observe active owned-worker CPU before {family} {fault}; no interruption pass recorded.");
                    var timer = clock.Current ?? throw new InvalidOperationException("The active command did not create its deadline timer.");
                    check(timer.Due == TimeSpan.FromSeconds(120), $"Optimize interruption: {family} uses its production 120-second execution deadline: {fault}");
                    check(!operation.IsCompleted && active.TotalProcessorTime > cpuBefore,
                        $"Optimize interruption: owned {family} worker consumes CPU during the unfinished command: {fault}");
                    if (operation.IsCompleted)
                        throw new InvalidOperationException($"{family} completed before the {fault} fault was applied.");
                    if (fault == "cancel") cancellation.Cancel();
                    else if (fault == "timeout") timer.Expire();
                    else active.Kill(entireProcessTree: true);
                    try
                    {
                        await operation.WaitAsync(TimeSpan.FromSeconds(15));
                        throw new InvalidOperationException($"Interrupted {family} command returned success: {fault}");
                    }
                    catch (OperationCanceledException) when (fault == "cancel")
                    { check(true, $"Optimize interruption: {family} retains user cancellation semantics"); }
                    catch (MediaWorkerException error) when (fault != "cancel")
                    {
                        check(error.Failure == (fault == "timeout" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated),
                            $"Optimize interruption: {family} reports the distinct {fault} failure");
                    }
                    active.Refresh();
                    check(active.HasExited, $"Optimize interruption: {family} owned worker exits before the interrupted request returns: {fault}");
                }
                finally
                {
                    if (!operation.IsCompleted)
                    {
                        cancellation.Cancel();
                        try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); }
                        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException) { }
                    }
                    abandoned = await publisher.AbandonAsync(reservation, fault == "cancel");
                }
                var outcome = abandoned ?? throw new InvalidOperationException("Interrupted reservation was not abandoned.");
                check(outcome.Outcome == (fault == "cancel" ? PublicationOutcome.Cancelled : PublicationOutcome.Failed) &&
                    !File.Exists(reservation.TemporaryPath) && !File.Exists(reservation.Record.OutputPath) &&
                    !Directory.EnumerateFiles(records).Any() && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(originalHash) &&
                    File.GetLastWriteTimeUtc(path) == timestamp,
                    $"Optimize interruption: {family} leaves its original, no published copy, and no active journal: {fault}");
                if (family == "Office")
                    await worker.ProbeOfficePackageAsync(new(Guid.NewGuid(), path), default);
                else await worker.ProbeLosslessImageAsync(new(Guid.NewGuid(), path), default);
                check(worker.ProcessId is { } recovered && recovered != processId,
                    $"Optimize interruption: {family} valid probe recovers in a fresh worker after {fault}");
            }
        }
    }

    private static void WriteJpeg(string path)
    {
        const int dimension = 4096;
        var pixels = new byte[dimension * dimension * 3];
        new Random(7429).NextBytes(pixels);
        var source = BitmapSource.Create(dimension, dimension, 96, 96, PixelFormats.Bgr24, null, pixels, dimension * 3);
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void WritePackage(string path)
    {
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"bin\" ContentType=\"application/octet-stream\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"main\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Interruption fixture</w:t></w:r></w:p></w:body></w:document>");
            var entry = zip.CreateEntry("word/media/opaque.bin", CompressionLevel.NoCompression);
            using var stream = entry.Open();
            var random = new Random(28731);
            var block = new byte[65536];
            for (var index = 0; index < 128; index++) { random.NextBytes(block); stream.Write(block); }

            void Add(string name, string content)
            {
                using var part = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                part.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        // Stored entries default to ZIP 1.0. Author ZIP 2.0 declarations so
        // the recipe may choose Deflate without changing extraction metadata.
        using var package = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var end = new byte[22];
        package.Position = package.Length - end.Length;
        package.ReadExactly(end);
        var directory = BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(16));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(end.AsSpan(10));
        var header = new byte[46];
        Span<byte> version = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(version, 20);
        for (var index = 0; index < count; index++)
        {
            package.Position = directory;
            package.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50) throw new InvalidDataException("Authored ZIP directory is invalid.");
            var local = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(42));
            package.Position = local + 4;
            package.Write(version);
            package.Position = directory + 6;
            package.Write(version);
            directory += (uint)(46 + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28)) +
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32)));
        }
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Interruption fixtures must never recycle originals.");
    }
}
