using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class QuantizationEvaluation
{
    private sealed record Result(string Fixture, string Candidate, int SourceBytes, int LosslessBytes,
        int OutputBytes, double SavingsPercent, double Milliseconds, QuantizationMetrics Metrics,
        bool MetadataPreserved, bool BalancedScreen, bool SmallestScreen, int PaletteEntries, int EncodingMaximumError,
        string OutputSha256, bool AutoScreen, GradientProtection.Diagnostic? Gradient, GradientRejection? Plateau);

    internal static async Task RunAsync(string scratch, string? corpus = null, bool presetResearch = false,
        bool previews = true, string? fixtureFilter = null, string? candidateFilter = null)
    {
        var elapsed = Stopwatch.StartNew();
        if (!Path.IsPathFullyQualified(scratch)) throw new ArgumentException("Absolute scratch path required.");
        var root = Path.Combine(scratch, "evaluation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ImageEngineRuntime.Initialize(Path.Combine(root, "scratch"));
        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContextSuite.Engine.json")));
        var native = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName == "Magick.Native-Q16-x64.dll");
        Check(Hash(File.ReadAllBytes(native.FileName)) == pin.RootElement.GetProperty("nativeSha256").GetString(), "curated native identity");
        SelfTest();
        if (presetResearch) { GradientProtection.Verify(); GradientRejection.Verify(); }
        var results = new List<Result>();
        var sources = new List<object>();
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>Context Suite PNG quantization evaluation</title>
            <style>body{font:16px system-ui;margin:24px;background:#202124;color:#eee}a{color:#9bd5ff}
            table{border-collapse:collapse}td,th{padding:7px;border:1px solid #666;text-align:left}h2{margin-top:40px}
            .pair{display:flex;gap:12px;flex-wrap:wrap}figure{margin:8px 0}img{max-width:100%;border:1px solid #888}
            .zoom img{max-width:none;image-rendering:pixelated}.zoom{overflow:auto;max-width:100%}
            summary{cursor:pointer;padding:10px;background:#34363a}details{margin:10px 0}small{color:#ccc}</style>
            <h1>PNG quantization: research, not shipping presets</h1>
            <p>Corpus: <!--corpus-->. All outputs, including failed screens,
            are retained ONLY here for comparison. No customer files, trial state or installed app are changed.</p>
            <p>Measurements use sRGB byte values; RMSE is worst of black/white composites on a 0–255 scale.
            Worst tile is the maximum 8×8 RGB RMSE. SSIM is the lower mean of black/white 8×8 luminance SSIM.
            Alpha error is separate. Screening thresholds are provisional, not visual acceptance.</p>
            <p>Balanced screen: RMSE ≤2, worst tile ≤6, SSIM ≥0.98. Smallest screen: ≤4, ≤12, ≥0.95.
            Both additionally require encoding error ≤1, exact alpha, retained fixture metadata and a smaller result than BOTH
            the source and its lossless baseline. These are research filters, not approved guarantees.</p>
            <p>Expanded matrix: baseline palette methods, RGB 7/6-bit precision, partial diffusion,
            RGB-only quantization with exact alpha, and fixed-seed k-means (128 clusters, at most 8 iterations).
            No quality threshold has been relaxed. All precision/alpha-restoration variants preserve hidden RGB.</p>
            <!--shortlist-->
            """);
        var fixtures = corpus is null ? QuantizationFixtures.Create(presetResearch).ToList() : ReadCorpus(corpus, scratch, fixtureFilter);
        if (corpus is null)
        {
            var indexed = fixtures.Single(f => f.Name == "indexed-icons");
            fixtures.Add(indexed with { Name = "already-optimized-icons", Png = await PngOptimizer.EncodeAsync(indexed.Png, CancellationToken.None) });
        }
        var candidates = QuantizationCandidate.All().Where(c => corpus is null || c.Name is "256-No" or "rgb7-exact-alpha" or "rgb6-exact-alpha").ToArray();
        if (presetResearch)
        {
            candidates = [new("rgb7-baseline", RgbBits:7, PreserveAlpha:true),
                new("sRGB-512-fs10", 512, DitherMethod.FloydSteinberg, 10, PreserveAlpha:true),
                new("sRGB-512-fs25", 512, DitherMethod.FloydSteinberg, 25, PreserveAlpha:true),
                new("sRGB-1024-fs10", 1024, DitherMethod.FloydSteinberg, 10, PreserveAlpha:true)];
            html.Append("<p><strong>Focused preset research:</strong> exact encoding, alpha and hidden RGB required. Auto uses the Balanced screen plus at least 5% additional savings. All research screens now include a provisional plateau veto; lack of rejection is NOT visual acceptance. No shipping policy changed.</p>");
        }
        if (fixtureFilter is not null) fixtures = fixtures.Where(f => f.Name == fixtureFilter).ToList();
        if (candidateFilter is not null) candidates = candidates.Where(c => c.Name == candidateFilter).ToArray();
        Check(fixtures.Count > 0 && candidates.Length > 0, "filters must select at least one fixture and candidate");
        if (!previews) html.Append("<p>Preview generation disabled. Full original and candidate PNG links remain available.</p>");
        Console.WriteLine($"Starting {fixtures.Count} fixture(s) x {candidates.Length} candidate(s); previews={previews}. Report directory: {root}");
        Checkpoint(false);
        html.Replace("<!--corpus-->", corpus is null ? "authored synthetic patterns, not real photos" :
            "user-authorized screenshot COPIES, full resolution. Unknown fdEC metadata removed from research inputs only; copied originals retained unchanged. Not redistributable fixtures or production metadata acceptance");
        foreach (var fixture in fixtures)
        {
            var path = Path.Combine(root, fixture.Name + ".png");
            File.WriteAllBytes(path, fixture.Png);
            using (var decoded = new MagickImage(fixture.Png))
                Check(Pixels(decoded).SequenceEqual(fixture.Pixels), fixture.Name + " independent fixture decode");
            var lossless = await PngOptimizer.EncodeAsync(fixture.Png, CancellationToken.None);
            using (var decoded = new MagickImage(lossless))
                Check(Pixels(decoded).SequenceEqual(fixture.Pixels), fixture.Name + " lossless baseline pixels");
            Check(MetadataEqual(fixture.Png, lossless), fixture.Name + " lossless metadata");
            File.WriteAllBytes(Path.Combine(root, fixture.Name + "-lossless.png"), lossless);
            if (previews) Preview(root, fixture.Name, fixture.Pixels, fixture.Width, fixture.Height);
            sources.Add(new { fixture.Name, fixture.Width, fixture.Height, Sha256 = Hash(fixture.Png), LosslessSha256 = Hash(lossless),
                fixture.Provenance, Bytes = fixture.Png.Length, LosslessBytes = lossless.Length });
            html.Append($"<h2 id='{fixture.Name}'>{fixture.Name}</h2><p>Source: {fixture.Png.Length:N0} bytes; lossless: {lossless.Length:N0} bytes. <a href='{fixture.Name}-lossless.png'>Lossless baseline</a></p>");
            foreach (var candidate in candidates)
            {
                var name = candidate.Name;
                var stem = fixture.Name + "-" + name;
                var timer = Stopwatch.StartNew();
                var (encoded, quantizedPixels) = candidate.Encode(fixture);
                // Only four known invariant fixture chunks: never restore PLTE, tRNS or arbitrary chunks.
                encoded = QuantizationFixtures.RestoreFixtureMetadata(fixture.Png, encoded);
                var optimized = await PngOptimizer.EncodeAsync(encoded, CancellationToken.None);
                timer.Stop();
                using var reopened = new MagickImage(optimized);
                Check(reopened.Width == fixture.Width && reopened.Height == fixture.Height && reopened.Format == MagickFormat.Png,
                    stem + " output structure");
                var pixels = Pixels(reopened);
                int encodingMaximumError = pixels.Zip(quantizedPixels).Max(pair => Math.Abs(pair.First - pair.Second));
                // Q16 palette centroids can sit between 8-bit levels. Anything beyond one rounding
                // unit is a rejected experimental candidate, retained for diagnosis, NOT a pass.
                var chunks = QuantizationFixtures.Parse(optimized);
                int paletteEntries = (chunks.SingleOrDefault(c => c.Type == "PLTE")?.Data.Length ?? 0) / 3;
                Check(paletteEntries <= 256 && (candidate.PreserveAlpha || paletteEntries <= candidate.Colors), stem + " legal palette budget");
                var metrics = QuantizationMetrics.Measure(fixture.Pixels, pixels, fixture.Width, fixture.Height);
                if (candidate.PreserveAlpha)
                    Check(metrics.AlphaMaximumError == 0 && metrics.HiddenRgbChangedPixels == 0, stem + " exact alpha and hidden RGB");
                bool metadata = MetadataEqual(fixture.Png, optimized);
                Check(metadata, stem + " supported metadata preserved");
                if (presetResearch) Check(encodingMaximumError == 0, stem + " exact encoded samples");
                var plateau = presetResearch ? GradientRejection.Measure(fixture.Pixels, pixels, fixture.Width, fixture.Height) : null;
                if (presetResearch && fixture.Name == "dark-color-gradient" && name == "sRGB-512-fs10")
                    Check(plateau!.Rejected, "known visually blocky gradient must be rejected");
                bool eligible = metadata && encodingMaximumError <= (presetResearch ? 0 : 1) && metrics.AlphaMaximumError == 0 &&
                    (!presetResearch || metrics.HiddenRgbChangedPixels == 0) && plateau?.Rejected != true && optimized.Length < Math.Min(fixture.Png.Length, lossless.Length);
                var result = new Result(fixture.Name, name, fixture.Png.Length, lossless.Length, optimized.Length,
                    100.0 * (fixture.Png.Length - optimized.Length) / fixture.Png.Length, timer.Elapsed.TotalMilliseconds, metrics, metadata,
                    eligible && metrics.CompositeRmse <= 2 && metrics.WorstTileRmse <= 6 && metrics.MinimumMeanSsim >= .98,
                    eligible && metrics.CompositeRmse <= 4 && metrics.WorstTileRmse <= 12 && metrics.MinimumMeanSsim >= .95, paletteEntries, encodingMaximumError, Hash(optimized),
                    eligible && metrics.CompositeRmse <= 2 && metrics.WorstTileRmse <= 6 && metrics.MinimumMeanSsim >= .98 &&
                        optimized.Length <= .95 * Math.Min(fixture.Png.Length, lossless.Length),
                    presetResearch ? GradientProtection.Measure(fixture.Pixels, pixels, fixture.Width, fixture.Height) : null, plateau);
                File.WriteAllBytes(Path.Combine(root, stem + ".png"), optimized);
                if (previews) Preview(root, stem, pixels, fixture.Width, fixture.Height);
                // Check cleanup did not introduce additional quantization or transparency changes.
                using (var beforeCleanup = new MagickImage(encoded))
                    Check(Pixels(beforeCleanup).SequenceEqual(pixels), stem + " oxipng adds no sample loss");
                results.Add(result);
                Checkpoint(false);
                Console.WriteLine($"Completed {stem}: {optimized.Length:N0} bytes; plateau rejected={plateau?.Rejected}; total {elapsed.Elapsed.TotalSeconds:F1}s");
                html.Append(CultureInfo.InvariantCulture, $"<details><summary>{name}: {optimized.Length:N0} bytes, {result.SavingsPercent:F1}% saved; RMSE {metrics.CompositeRmse:F2}; worst tile {metrics.WorstTileRmse:F2}; SSIM {metrics.MinimumMeanSsim:F4}; alpha max {metrics.AlphaMaximumError}; encoding error {encodingMaximumError}; screens B={result.BalancedScreen}, S={result.SmallestScreen}</summary>");
                if (plateau is not null) html.Append(CultureInfo.InvariantCulture, $"<p>Plateau veto: {plateau.Rejected}; {plateau.FlattenedRuns}/{plateau.EligibleRuns} eligible channel/background runs flattened ({plateau.FlattenedPercent:F2}%). Zero eligible runs is no evidence of smoothness.</p>");
                if (previews)
                {
                    html.Append("<p>Original left, candidate right. Click an image for native size. Light and dark backgrounds expose transparency errors.</p>");
                    foreach (var background in new[] { "white", "black" })
                        html.Append($"<div class='pair'><figure><figcaption>Original — {background}</figcaption><a href='{fixture.Name}-{background}.png'><img loading='lazy' alt='Original {fixture.Name} over {background}' src='{fixture.Name}-{background}.png'></a></figure><figure><figcaption>{name} — {background}</figcaption><a href='{stem}-{background}.png'><img loading='lazy' alt='Candidate {name} over {background}' src='{stem}-{background}.png'></a></figure></div>");
                    html.Append($"<details><summary>2× inspection: original then candidate</summary><div class='zoom'><img style='width:{fixture.Width * 2}px' loading='lazy' alt='Enlarged original' src='{fixture.Name}-white.png'><br><img style='width:{fixture.Width * 2}px' loading='lazy' alt='Enlarged candidate' src='{stem}-white.png'></div></details>");
                    if (corpus is not null)
                    {
                        CropPreview(root, stem, pixels, fixture.Width, fixture.Height);
                        html.Append($"<p>Native-scale center crops: original left, candidate right.</p><div class='pair'><img alt='Original center crop' src='{fixture.Name}-crop.png'><img alt='Candidate center crop' src='{stem}-crop.png'></div>");
                    }
                }
                else html.Append($"<p><a href='{fixture.Name}.png'>Original</a> · <a href='{stem}.png'>Candidate</a></p>");
                html.Append("</details>");
            }
            Check(Hash(File.ReadAllBytes(path)) == Hash(fixture.Png), fixture.Name + " source unchanged");
            Console.WriteLine("Evaluated " + fixture.Name);
            if (previews && corpus is not null) CropPreview(root, fixture.Name, fixture.Pixels, fixture.Width, fixture.Height);
        }
        var shortlist = new StringBuilder("<h2>Smallest candidates passing the current research screens</h2><p>Research selections only, NOT automatic visual approval. Original pixel-error limits remain unchanged; focused preset research adds the plateau veto. Savings below compare against lossless, not the larger original. No passing candidate means use lossless if it saves space, otherwise No smaller result.</p><table><tr><th>Fixture</th><th>Auto screen</th><th>Balanced screen</th><th>Smallest screen</th></tr>");
        foreach (var group in results.GroupBy(r => r.Fixture))
        {
            shortlist.Append($"<tr><td><a href='#{group.Key}'>{group.Key}</a></td>");
            foreach (var preset in new[] { "Auto", "Balanced", "Smallest" })
            {
                var best = group.Where(r => preset == "Auto" ? r.AutoScreen : preset == "Balanced" ? r.BalancedScreen : r.SmallestScreen).OrderBy(r => r.OutputBytes).ThenBy(r => r.Candidate, StringComparer.Ordinal).FirstOrDefault();
                if (best is null) shortlist.Append("<td>No acceptable smaller candidate</td>");
                else shortlist.Append(CultureInfo.InvariantCulture, $"<td><a href='{best.Fixture}-{best.Candidate}.png'>{best.Candidate}</a>: {best.OutputBytes:N0} bytes ({100.0 * (best.LosslessBytes - best.OutputBytes) / best.LosslessBytes:F1}% below lossless)</td>");
            }
            shortlist.Append("</tr>");
        }
        shortlist.Append("</table>");
        html.Replace("<!--shortlist-->", shortlist.ToString());
        var process = Process.GetCurrentProcess(); process.Refresh();
        var report = new { Schema = "png-quantization-evaluation-v4", Complete=true, Previews=previews, FixtureFilter=fixtureFilter,
            CandidateFilter=candidateFilter, ElapsedSeconds=elapsed.Elapsed.TotalSeconds, PresetResearch=presetResearch, GeneratedUtc = DateTimeOffset.UtcNow,
            Engine = ImageEngineRuntime.GetIdentity(), NativeSha256 = Hash(File.ReadAllBytes(native.FileName)),
            OxipngSha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "oxipng.exe"))),
            HarnessSha256 = Hash(File.ReadAllBytes(typeof(QuantizationEvaluation).Assembly.Location)),
            Os = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(), PeakProcessWorkingSetBytes = process.PeakWorkingSet64,
            Policy = new { QuantizeColorSpace = "sRGB", TreeDepth = 0, DisableOnlyEntirelyOpaqueAlpha = true,
                PngColorType = "automatic for palette candidates; explicit RGB/RGBA for precision and alpha reconstruction", PngDepth = 8, EncodingMaximumError = presetResearch ? 0 : 1, Oxipng = "existing pinned --opt 2 --nx policy" },
            Candidates = candidates,
            MemoryNote = "Whole evaluation process peak; excludes oxipng child memory, not a per-candidate measurement.",
            Scope = corpus is null ? "Synthetic 8-bit static RGBA/indexed fixtures only. No photo, ICC/EXIF, 16-bit, APNG, arbitrary input, production admission or UI validation." :
                "User-authorized screenshot copies at full resolution; fdEC removed from research input, untagged RGB interpreted as sRGB for measurement. Original copies untouched. No general metadata or production acceptance.",
            Screens = new { AlphaMaximumError = 0, RequireSmallerThanSourceAndLossless = true,
                Plateau = new { Enabled=presetResearch, RunLength=32, GridStride=8, MinimumEligibleRuns=32, RejectedPercent=5, MaximumRetainedProgress=.25 },
                Auto = new { Rmse = 2, WorstTile = 6, Ssim = .98, MinimumAdditionalSavingsPercent = 5 },
                Balanced = new { Rmse = 2, WorstTile = 6, Ssim = .98 }, Smallest = new { Rmse = 4, WorstTile = 12, Ssim = .95 } },
            Sources = sources, Results = results };
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        html.Append("<h2>Candidate screening totals</h2><table><tr><th>Candidate</th><th>Balanced</th><th>Smallest</th></tr>");
        foreach (var group in results.GroupBy(r => r.Candidate))
        {
            html.Append($"<tr><td>{WebUtility.HtmlEncode(group.Key)}</td><td>{group.Count(r => r.BalancedScreen)}/{group.Count()}</td><td>{group.Count(r => r.SmallestScreen)}/{group.Count()}</td></tr>");
            Console.WriteLine($"{group.Key}: Balanced screen {group.Count(r => r.BalancedScreen)}/{group.Count()}, Smallest screen {group.Count(r => r.SmallestScreen)}/{group.Count()}");
        }
        html.Append("</table><p>Inspect results.json for exact metrics, fixture hashes, engine identity and timings. Human review remains required.</p></html>");
        File.WriteAllText(Path.Combine(root, "index.html"), html.ToString());
        Checkpoint(true);
        Console.WriteLine($"PASS: harness completed {results.Count} experiments; fixture, metric, metadata, cleanup and source-integrity assertions passed.");
        Console.WriteLine($"Rejected encoding-fidelity candidates: {results.Count(r => r.EncodingMaximumError > 1)}. These are NOT successful media results.");
        Console.WriteLine("Report: " + Path.Combine(root, "index.html"));

        void Checkpoint(bool complete)
        {
            var checkpoint = new { Schema="png-quantization-progress-v1", Complete=complete, ExpectedResults=fixtures.Count * candidates.Length,
                CompletedResults=results.Count, ElapsedSeconds=elapsed.Elapsed.TotalSeconds, Previews=previews,
                HarnessSha256=Hash(File.ReadAllBytes(typeof(QuantizationEvaluation).Assembly.Location)), Sources=sources, Candidates=candidates, Results=results };
            var path = Path.Combine(root, "progress.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented=true }));
            File.Move(path + ".tmp", path, true);
        }
    }

    private static byte[] Pixels(MagickImage image) => image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static List<QuantizationFixtures.Fixture> ReadCorpus(string corpus, string scratch, string? fixtureFilter = null)
    {
        string allowed = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(corpus);
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Corpus must be an ordinary copied directory inside the evaluation scratch root.");
        var files = Directory.GetFiles(full, "*.png").Order(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > 3) throw new ArgumentException("One to three screenshot copies required.");
        var fixtures = new List<QuantizationFixtures.Fixture>();
        for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            var file = files[fileIndex];
            var fixtureName = "screenshot-" + (fileIndex + 1);
            if (fixtureFilter is not null && fixtureFilter != fixtureName) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Corpus requires ordinary PNG copies <=16 MiB.");
            var original = File.ReadAllBytes(file);
            var chunks = QuantizationFixtures.Parse(original);
            var header = chunks[0].Data;
            if (header.Length != 13) throw new InvalidDataException("Invalid screenshot IHDR length.");
            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
            if (width is < 1 or > 4096 || height is < 1 or > 2160 || header[8] != 8 || header[9] != 2 || header[12] != 0 ||
                chunks.Any(c => c.Type is not ("IHDR" or "fdEC" or "IDAT" or "IEND")))
                throw new InvalidDataException("This research corpus supports only bounded, static, untagged 8-bit RGB screenshots.");
            // fdEC is unknown/unsafe-to-copy. Do not pretend to preserve it after pixel changes.
            // Strip only on the in-memory research copy; the original and copied source remain intact.
            var normalized = QuantizationFixtures.Pack(chunks.Where(c => c.Type != "fdEC"));
            using var image = new MagickImage(original); using var normalizedImage = new MagickImage(normalized);
            var pixels = Pixels(image);
            Check(Pixels(normalizedImage).SequenceEqual(pixels), "research metadata removal leaves screenshot pixels exact");
            fixtures.Add(new(fixtureName, width, height, pixels, normalized,
                "User-authorized local copy: " + Path.GetFileName(file) + "; original SHA256=" + Hash(original) + "; fdEC removed for research only; no redistribution permission inferred."));
            Check(Hash(File.ReadAllBytes(file)) == Hash(original), "copied screenshot retained unchanged");
        }
        return fixtures;
    }

    private static bool MetadataEqual(byte[] source, byte[] output)
    {
        var a = QuantizationFixtures.Parse(source).Where(c => QuantizationFixtures.MetadataTypes.Contains(c.Type)).OrderBy(c => c.Type).ToArray();
        var b = QuantizationFixtures.Parse(output).Where(c => QuantizationFixtures.MetadataTypes.Contains(c.Type)).OrderBy(c => c.Type).ToArray();
        return a.Length == b.Length && a.Zip(b).All(pair => pair.First.Type == pair.Second.Type && pair.First.Data.SequenceEqual(pair.Second.Data));
    }

    private static void Preview(string root, string stem, byte[] pixels, int width, int height)
    {
        foreach (var background in new[] { 0, 255 })
            File.WriteAllBytes(Path.Combine(root, stem + (background == 0 ? "-black.png" : "-white.png")),
                QuantizationFixtures.Encode(width, height, QuantizationMetrics.CompositePreview(pixels, background)));
    }

    private static void CropPreview(string root, string stem, byte[] pixels, int width, int height)
    {
        int cropWidth = Math.Min(width, 512), cropHeight = Math.Min(height, 256);
        int x = (width - cropWidth) / 2, y = (height - cropHeight) / 2;
        var crop = new byte[cropWidth * cropHeight * 4];
        for (int row = 0; row < cropHeight; row++)
            pixels.AsSpan(((y + row) * width + x) * 4, cropWidth * 4).CopyTo(crop.AsSpan(row * cropWidth * 4));
        File.WriteAllBytes(Path.Combine(root, stem + "-crop.png"), QuantizationFixtures.Encode(cropWidth, cropHeight, crop));
    }

    private static void SelfTest()
    {
        foreach (int bits in new[] { 6, 7 })
        {
            var ramp = Enumerable.Range(0, 256).SelectMany(i => new byte[] { (byte)i, (byte)i, (byte)i, 255 }).ToArray();
            var reduced = QuantizationCandidate.ReducePrecision(ramp, bits);
            Check(reduced[0] == 0 && reduced[^4] == 255, "precision endpoints");
            Check(reduced.Where((_, i) => i % 4 == 0).Distinct().Count() == 1 << bits, "precision level count");
            Check(ramp.Zip(reduced).Max(p => Math.Abs(p.First - p.Second)) <= (bits == 7 ? 1 : 2), "precision error bound");
            Check(QuantizationCandidate.ReducePrecision(reduced, bits).SequenceEqual(reduced), "precision idempotence");
            byte[] transparent = [33, 144, 255, 0, 99, 100, 101, 17];
            var alphaKept = QuantizationCandidate.ReducePrecision(transparent, bits);
            Check(alphaKept.AsSpan(0, 4).SequenceEqual(transparent.AsSpan(0, 4)) && alphaKept[7] == 17, "precision alpha/hidden preservation");
        }
        byte[] black = [0, 0, 0, 255], white = [255, 255, 255, 255];
        var equal = QuantizationMetrics.Measure(black, black, 1, 1);
        Check(equal.CompositeRmse == 0 && equal.MinimumMeanSsim == 1 && equal.ChangedPixels == 0, "identity metrics");
        var extreme = QuantizationMetrics.Measure(black, white, 1, 1);
        Check(extreme.CompositeRmse == 255 && extreme.WorstTileRmse == 255 && extreme.MinimumMeanSsim < .001, "extreme metrics");
        var hidden = QuantizationMetrics.Measure([0, 0, 0, 0], [255, 255, 255, 0], 1, 1);
        Check(hidden.CompositeRmse == 0 && hidden.HiddenRgbChangedPixels == 1, "hidden RGB is reported separately");
        var alpha = QuantizationMetrics.Measure([0, 0, 0, 0], black, 1, 1);
        Check(alpha.CompositeRmse == 255 && alpha.AlphaMaximumError == 255, "white background detects alpha damage");
        var png = QuantizationFixtures.Encode(1, 1, black);
        Check(MetadataEqual(png, png), "metadata identity");
        var missing = QuantizationFixtures.Pack(QuantizationFixtures.Parse(png).Where(c => c.Type != "tEXt"));
        Check(!MetadataEqual(png, missing), "metadata removal detected");
        Check(MetadataEqual(png, QuantizationFixtures.RestoreFixtureMetadata(png, missing)), "known metadata restoration");
        foreach (var broken in new[] { png[..20], new byte[20], png.Select((b, i) => i == 29 ? (byte)(b ^ 1) : b).ToArray() })
        {
            bool rejected = false;
            try { QuantizationFixtures.Parse(broken); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "malformed fixture rejection");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Evaluation assertion failed: " + message);
    }
}
