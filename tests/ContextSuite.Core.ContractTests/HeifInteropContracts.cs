using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

namespace ContextSuite.Core.ContractTests;

internal static class HeifInteropContracts
{
    public static async Task RunAsync(string executable, string fixtures, string manifest, string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root))
            throw new ArgumentException("Use a new absolute evidence directory.");
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var checks = new List<string>();
        var reports = new List<object>();
        var gaps = new List<string>();
        var originals = new Dictionary<string, (string Hash, DateTime Written)>();
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        await using var worker = new WorkerClient(executable, Path.Combine(root, "workers"));
        using var corpus = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var recovery = Path.Combine(fixtures, "libavif/tests/data/white_1x1.avif");
        foreach (var row in corpus.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = row.GetProperty("path").GetString()!;
            var path = Path.Combine(fixtures, name);
            var expectation = row.GetProperty("expectation").GetString();
            Check(expectation is "convert" or "refuse" or "pending-color", "Declared corpus expectation");
            Check(Hash(path) == row.GetProperty("sha256").GetString(), "Pinned input " + name);
            originals.Add(path, (Hash(path), File.GetLastWriteTimeUtc(path)));
            if (expectation != "convert")
            {
                // A recorded refusal can pass this regression without closing a
                // compatibility gap. Pending color cases remain explicit gates.
                if (expectation == "pending-color") gaps.Add(name);
                try
                {
                    await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
                    throw new InvalidOperationException("Expected typed refusal: " + name);
                }
                catch (MediaWorkerException error)
                {
                    Check(error.Failure == ImageFailure.UnsupportedInput, "Unsupported feature is distinguished from damage " + name);
                }
                var process = worker.ProcessId;
                var recovered = await worker.ProbeAsync(new(Guid.NewGuid(), recovery), default);
                Check(worker.ProcessId == process && recovered.Width == 1 && recovered.Height == 1,
                    "Same worker recovers after " + name);
                continue;
            }
            var facts = await worker.ProbeAsync(new(Guid.NewGuid(), path), default);
            Check(facts.UnsupportedReason is null && facts.Width == row.GetProperty("width").GetUInt32() &&
                facts.Height == row.GetProperty("height").GetUInt32() && facts.Orientation == 1 && facts.BitDepth == 8,
                "Expected transformed geometry and precision " + name);
            Check(facts.HasTransparency == row.GetProperty("alpha").GetBoolean(), "Expected alpha presence " + name);
            if (facts.HasTransparency)
                Check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Jpeg), new("convert", new())).HasExecutableItems,
                    "Transparent upstream input still requires explicit JPEG matte");
            foreach (var target in new[] { ImageFormat.Png, ImageFormat.Jpeg })
            {
                facts = facts with { ItemId = Guid.NewGuid() };
                var options = new ImageConversionOptions(target, Metadata: ImageMetadataMode.Automatic,
                    MatteRgb: target == ImageFormat.Jpeg ? 0xffffffu : null);
                var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new(OutputDirectory: output)));
                var confirmed = plan.Confirm(true, false, false);
                var reservation = await publisher.ReserveAsync(new(facts.ItemId, path, options.Extension, plan.Settings));
                var result = await worker.ConvertAsync(new(confirmed.Plan.Items[0], options, reservation.TemporaryPath), default);
                Check(!File.Exists(reservation.Record.OutputPath) && result.Validation.MatchesPlan, "Validate before publication " + name);
                var published = await publisher.PublishAsync(reservation, result.Validation);
                var reopened = await worker.ProbeAsync(new(Guid.NewGuid(), published.OutputPath!), default);
                Check(published.Outcome == PublicationOutcome.CopyCreated && reopened.Format == target && reopened.Width == facts.Width &&
                    reopened.Height == facts.Height && reopened.BitDepth == 8 && reopened.ProfileNames.Contains("icc"),
                    "Named output copy reopens with expected facts " + name);
                Check(reopened.HasTransparency == (target == ImageFormat.Png && facts.HasTransparency), "Output alpha policy " + name);
                if (Path.GetFileName(path) == "white_1x1.avif")
                {
                    var sourcePreview = await worker.PreviewAsync(new(facts), default);
                    var preview = await worker.PreviewAsync(new(reopened), default);
                    // This upstream default-quality AVIF decodes near white,
                    // not the uncompressed source's ideal 255 sample.
                    Check(sourcePreview.Width == 1 && sourcePreview.Height == 1 &&
                        sourcePreview.BgraPixels.SequenceEqual(new byte[] { 253, 253, 253, 255 }), "Pinned lossy near-white input");
                    Check(preview.Width == 1 && preview.Height == 1 && preview.BgraPixels.SequenceEqual(sourcePreview.BgraPixels),
                        "Near-white sample survives conversion " + target);
                }
                reports.Add(new { Source = name, Target = target.ToString(), published.OutputPath, result.OutputBytes });
            }
        }
        Check(originals.All(pair => Hash(pair.Key) == pair.Value.Hash && File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Written),
            "All upstream inputs retain bytes and write times");
        Check(!Directory.EnumerateFiles(root, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(), "No temporary outputs remain");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = true, CompatibilityComplete = gaps.Count == 0, Checks = checks.Count,
            PendingColorCases = gaps, Outputs = reports, Details = checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} HEIF interoperability checks; {reports.Count} outputs; {gaps.Count} color compatibility gaps remain.");

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks.Add(message);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Interop tests must preserve originals.");
    }
}
