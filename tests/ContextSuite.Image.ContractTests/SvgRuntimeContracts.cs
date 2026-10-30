using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Core.Svg;
using ContextSuite.Private.Svg;
using ImageMagick;

internal static class SvgRuntimeContracts
{
    internal static async Task LoaderAsync(string root, string scenario)
    {
        if (scenario is not ("valid" or "missing" or "tampered" or "extra-exe" or "extra-dll"))
            throw new ArgumentException("Unknown SVG loader scenario.");
        Prepare(root);
        var source = Path.Combine(root, "source.svg");
        await File.WriteAllTextAsync(source, "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='4' height='4' fill='red'/></svg>");
        var original = Hash(source);
        var adapter = new SvgAdapter(Path.Combine(root, "scratch"));
        var checks = new List<string>();
        if (scenario == "valid")
        {
            var facts = await adapter.ProbeAsync(new(Guid.NewGuid(), source), default);
            Check(facts.IntrinsicWidth == 4 && facts.IntrinsicHeight == 4 && facts.Sha256 == original, "Verified adjacent renderer returns bound source facts");
        }
        else
        {
            try { await adapter.ProbeAsync(new(Guid.NewGuid(), source), default); throw new InvalidOperationException("Unsafe SVG runtime accepted"); }
            catch (ImageFailureException error) { Check(error.Failure == ImageFailure.EngineUnavailable, "Typed unavailable-engine refusal: " + scenario); }
        }
        Check(Hash(source) == original, "Source bytes remain unchanged");
        Check(!Directory.EnumerateFiles(root, "*.png").Any(), "Probing or engine refusal publishes no output");
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Scenario = scenario, Checks = checks }, JsonOptions));
        Console.WriteLine($"Passed {checks.Count} SVG loader checks: {scenario}.");
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks.Add(message); }
    }

    internal static async Task BoundsAsync(string root)
    {
        Prepare(root);
        var adapter = new SvgAdapter(Path.Combine(root, "scratch"));
        var results = new List<object>();
        foreach (var filtered in new[] { false, true })
        {
            var source = Path.Combine(root, filtered ? "filtered.svg" : "ordinary.svg");
            var body = filtered ? "<defs><filter id='f'><feGaussianBlur stdDeviation='1'/></filter></defs><rect x='250000' y='250000' width='500000' height='500000' fill='red' filter='url(#f)'/>" :
                "<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='1000000' height='1000000' fill='url(#g)'/>";
            await File.WriteAllTextAsync(source, "<svg xmlns='http://www.w3.org/2000/svg' width='1000000' height='1000000'>" + body + "</svg>");
            var hash = Hash(source);
            var watch = Stopwatch.StartNew();
            var facts = await adapter.ProbeAsync(new(Guid.NewGuid(), source), default);
            var options = new SvgConversionOptions();
            var size = options.GetSize(facts);
            var expected = filtered ? 1000U : 4000U;
            if (size.Width != expected || size.Height != expected) throw new InvalidDataException("Automatic SVG pixel cap mismatch");
            var id = Guid.NewGuid(); var output = Path.Combine(root, $".context-suite-{id:N}.tmp"); using (File.Create(output)) { }
            var result = await adapter.ConvertAsync(new(facts, options, id, output), default);
            watch.Stop();
            using var decoded = new MagickImage(output);
            var rgba = decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            var center = checked((int)((expected / 2 * expected + expected / 2) * 4));
            if (decoded.Width != expected || decoded.Height != expected || rgba[center + 3] != 255 ||
                filtered && (rgba[center] != 255 || rgba[center + 1] != 0 || rgba[3] != 0) ||
                !filtered && (rgba[0] <= rgba[2] || rgba[center] < 100 || rgba[center + 2] < 100) ||
                Hash(source) != hash || result.Validation.Sha256 != Hash(output))
                throw new InvalidDataException("SVG maximum-size semantic validation failed");
            using var process = Process.GetCurrentProcess();
            results.Add(new { Filtered = filtered, Width = expected, Height = expected, ElapsedMilliseconds = watch.ElapsedMilliseconds,
                OutputBytes = result.OutputBytes, AdapterProcessPeakWorkingSetBytes = process.PeakWorkingSet64, SourceUnchanged = true,
                PixelSemanticsPassed = true });
            Console.WriteLine($"PASS: SVG {(filtered ? "filtered" : "ordinary")} maximum canvas {expected}x{expected}; {watch.ElapsedMilliseconds} ms; {result.OutputBytes} bytes.");
        }
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Results = results }, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Prepare(string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a fresh absolute SVG evidence directory.");
        Directory.CreateDirectory(root);
    }
}
