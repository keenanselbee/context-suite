using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Private.Images;
using ImageMagick;

// Bounded research on user-provided COPIES. No production policy or source modification.
internal static class PaletteComparison
{
    private static readonly string[] Retained = ["sRGB", "gAMA", "cHRM", "pHYs", "tEXt", "zTXt", "iTXt", "eXIf"];

    internal static async Task RunAsync(string scratch, string corpus, bool refine = false, bool gradients = false)
    {
        GradientProtection.Verify();
        var allowed = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        corpus = Path.GetFullPath(corpus);
        if (!corpus.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(corpus) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Copied scratch corpus required.");
        var sourcePath = Path.Combine(corpus, "source.png");
        var referencePath = Path.Combine(corpus, "reference.png");
        var source = ReadBounded(sourcePath); var reference = ReadBounded(referencePath);
        var chunks = QuantizationFixtures.Parse(source);
        var header = chunks[0].Data;
        int width = BinaryPrimitives.ReadInt32BigEndian(header), height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (width is < 1 or > 4096 || height is < 1 or > 2160 || header[8] != 8 || header[9] is not (2 or 6) || header[12] != 0 ||
            chunks.Any(c => c.Type is not ("IHDR" or "IDAT" or "IEND") && !Retained.Contains(c.Type)))
            throw new InvalidDataException("Only bounded 8-bit RGB/RGBA PNG with understood metadata is supported by this comparison.");
        var root = Path.Combine(scratch, "palette-comparison-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ImageEngineRuntime.Initialize(Path.Combine(root, "scratch"));
        VerifyPaletteColorSpace();
        using var image = new MagickImage(source);
        using var referenceImage = new MagickImage(reference);
        if (referenceImage.Width != width || referenceImage.Height != height) throw new InvalidDataException("Reference dimensions differ.");
        var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
        var fixture = new QuantizationFixtures.Fixture("source", width, height, pixels, source, "User-authorized copy; no redistribution permission.");
        File.WriteAllBytes(Path.Combine(root, "source.png"), source);
        File.WriteAllBytes(Path.Combine(root, "reference.png"), reference);
        var results = new List<object>();
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>PNG palette comparison</title><style>body{font:16px system-ui;background:#202124;color:#eee;margin:24px}
            a{color:#9bd5ff}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #666}
            img{max-width:100%}.pair{display:flex;gap:12px;flex-wrap:wrap}.crop{width:640px;height:250px;overflow:hidden;position:relative}
            .crop img{position:absolute;max-width:none}details{margin:18px 0}summary{cursor:pointer;padding:12px;background:#35363a}</style>
            <h1>Palette comparison — research only</h1><p>Original metadata, alpha and hidden RGB are preserved by every experimental candidate.
            The reference is a comparison, not a preservation-approved output. No shipping settings changed.</p>
            <p>Pixel-error screens do not establish freedom from banding. Compare the crops at native scale, then open full images.</p>
            <p><a href="source.png">Original</a> · <a href="reference.png">Reference</a> · <a href="results.json">Measurements</a></p>
            <table><tr><th>Candidate</th><th>Bytes</th><th>vs reference</th><th>RMSE</th><th>Worst tile</th><th>SSIM</th><th>Alpha max</th><th>Seconds</th></tr>
            <!--rows--></table>
            """);
        var rows = new StringBuilder();
        Record("reference", reference, referenceImage.GetPixels().ToByteArray(PixelMapping.RGBA)!, 0, false);
        var candidates = new List<QuantizationCandidate> {
            new("rgb7", RgbBits:7, PreserveAlpha:true), new("rgb6", RgbBits:6, PreserveAlpha:true) };
        foreach (uint colors in new uint[] { 128, 192, 256 })
        {
            candidates.Add(new($"palette{colors}-none", colors, PreserveAlpha:true));
            candidates.Add(new($"palette{colors}-fs50", colors, DitherMethod.FloydSteinberg, 50, PreserveAlpha:true));
            candidates.Add(new($"palette{colors}-riemersma", colors, DitherMethod.Riemersma, PreserveAlpha:true));
        }
        if (refine)
        {
            candidates.Clear();
            foreach (uint colors in new uint[] { 224, 240, 256 })
                foreach (int diffusion in new[] { 10, 25 })
                    candidates.Add(new($"palette{colors}-fs{diffusion}", colors, DitherMethod.FloydSteinberg, diffusion, PreserveAlpha:true));
        }
        if (gradients)
        {
            candidates.Clear();
            foreach (var space in new[] { ColorSpace.sRGB, ColorSpace.Lab })
            foreach (uint colors in new uint[] { 256, 512 })
                candidates.Add(new($"{space}-{colors}-fs25", colors, DitherMethod.FloydSteinberg, 25, PreserveAlpha:true, PaletteSpace:space));
            foreach (var space in new[] { ColorSpace.sRGB, ColorSpace.Lab })
                candidates.Add(new($"{space}-256-protected", 256, DitherMethod.FloydSteinberg, 25, PreserveAlpha:true, PaletteSpace:space, ProtectGradients:true));
            candidates.Add(new("sRGB-512-none", 512, PreserveAlpha:true));
            candidates.Add(new("sRGB-512-fs10", 512, DitherMethod.FloydSteinberg, 10, PreserveAlpha:true));
            candidates.Add(new("sRGB-1024-fs10", 1024, DitherMethod.FloydSteinberg, 10, PreserveAlpha:true));
        }
        foreach (var candidate in candidates)
        {
            var timer = Stopwatch.StartNew();
            var (encoded, expected) = candidate.Encode(fixture);
            // Exact remapping only: use indexed storage if all RGBA tuples fit, otherwise keep RGBA.
            encoded = TryIndex(encoded, expected, width, height);
            var outputChunks = QuantizationFixtures.Parse(encoded);
            outputChunks.RemoveAll(c => Retained.Contains(c.Type));
            outputChunks.InsertRange(1, chunks.Where(c => Retained.Contains(c.Type)));
            encoded = QuantizationFixtures.Pack(outputChunks);
            var output = await PngOptimizer.EncodeAsync(encoded, default);
            timer.Stop();
            using var reopened = new MagickImage(output);
            var actual = reopened.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            if (!actual.SequenceEqual(expected)) throw new InvalidDataException("Encoding changed quantized samples: " + candidate.Name);
            var metrics = QuantizationMetrics.Measure(pixels, actual, width, height);
            var metadata = QuantizationFixtures.Parse(output).Where(c => Retained.Contains(c.Type)).ToArray();
            var originalMetadata = chunks.Where(c => Retained.Contains(c.Type)).ToArray();
            if (metrics.AlphaMaximumError != 0 || metrics.HiddenRgbChangedPixels != 0 || metadata.Length != originalMetadata.Length ||
                !metadata.Zip(originalMetadata).All(p => p.First.Type == p.Second.Type && p.First.Data.SequenceEqual(p.Second.Data)))
                throw new InvalidDataException("Candidate lost alpha or metadata: " + candidate.Name);
            File.WriteAllBytes(Path.Combine(root, candidate.Name + ".png"), output);
            Record(candidate.Name, output, actual, timer.Elapsed.TotalSeconds, true);
            html.Append($"<details><summary>{candidate.Name}: {output.Length:N0} bytes</summary><a href='{candidate.Name}.png'>Full candidate</a>");
            foreach (var crop in new[] { (Name:"Dark panels", X:800, Y:600), (Name:"Space and glow", X:500, Y:60) })
            {
                html.Append($"<h3>{crop.Name} — original / reference / candidate</h3><div class='pair'>");
                foreach (var stem in new[] { "source", "reference", candidate.Name })
                    html.Append($"<div><p>{stem}</p><div class='crop'><img loading='lazy' alt='{stem}' src='{stem}.png' style='width:{width}px;left:-{crop.X}px;top:-{crop.Y}px'></div></div>");
                html.Append("</div>");
            }
            html.Append("</details>");
            Console.WriteLine($"{candidate.Name}: {output.Length:N0} bytes; {timer.Elapsed.TotalSeconds:F1}s; preservation passed");
        }
        if (!ReadBounded(sourcePath).SequenceEqual(source) || !ReadBounded(referencePath).SequenceEqual(reference))
            throw new InvalidDataException("Copied inputs changed during evaluation.");
        var report = new { Schema="png-palette-comparison-v2", SourceSha256=Hash(source), ReferenceSha256=Hash(reference),
            Engine=ImageEngineRuntime.GetIdentity(), HarnessSha256=Hash(File.ReadAllBytes(typeof(PaletteComparison).Assembly.Location)),
            Candidates=candidates, Scope="One user-provided full-resolution image. Research only; exact alpha, hidden RGB and admitted metadata. No visual acceptance implied.", Results=results };
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented=true }));
        html.Replace("<!--rows-->", rows.ToString()).Append("</html>");
        File.WriteAllText(Path.Combine(root, "index.html"), html.ToString());
        Console.WriteLine("Report: " + Path.Combine(root, "index.html"));

        void Record(string name, byte[] encoded, byte[] decoded, double seconds, bool preserved)
        {
            var metrics = QuantizationMetrics.Measure(pixels, decoded, width, height);
            int entries = (QuantizationFixtures.Parse(encoded).SingleOrDefault(c => c.Type == "PLTE")?.Data.Length ?? 0)/3;
            var gradient = GradientProtection.Measure(pixels, decoded, width, height);
            results.Add(new { Candidate=name, Bytes=encoded.Length, PaletteEntries=entries, Seconds=seconds, Metrics=metrics, Gradient=gradient,
                PreservationVerified=preserved, Sha256=Hash(encoded) });
            rows.Append(FormattableString.Invariant($"<tr><td><a href='{name}.png'>{name}</a></td><td>{encoded.Length:N0}</td><td>{100.0*(encoded.Length-reference.Length)/reference.Length:F1}%</td><td>{metrics.CompositeRmse:F3}</td><td>{metrics.WorstTileRmse:F3}</td><td>{metrics.MinimumMeanSsim:F5}</td><td>{metrics.AlphaMaximumError}</td><td>{seconds:F1}</td></tr>"));
        }
    }

    private static void VerifyPaletteColorSpace()
    {
        var pixels = Enumerable.Range(0, 16 * 16).SelectMany(_ => new byte[] { 32, 48, 64, 255 }).ToArray();
        var fixture = new QuantizationFixtures.Fixture("flat-colour-contract", 16, 16, pixels,
            QuantizationFixtures.Encode(16, 16, pixels), "Authored fixture");
        foreach (var space in new[] { ColorSpace.sRGB, ColorSpace.Lab })
        {
            var candidate = new QuantizationCandidate("colour-space-contract", 16, PreserveAlpha:true, PaletteSpace:space);
            var (encoded, expected) = candidate.Encode(fixture);
            using var reopened = new MagickImage(encoded);
            if (!reopened.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(expected) ||
                expected.Where((v, i) => i % 4 != 3 && Math.Abs(v - pixels[i]) > 2).Any())
                throw new InvalidDataException("Palette working-space roundtrip failed: " + space);
        }
        Console.WriteLine("Palette working-space roundtrip contracts passed.");
    }

    private static byte[] TryIndex(byte[] fallback, byte[] pixels, int width, int height)
    {
        var colors = new Dictionary<uint, byte>(); var indices = new byte[width * height];
        for (int i=0; i<indices.Length; i++)
        {
            uint rgba = BinaryPrimitives.ReadUInt32BigEndian(pixels.AsSpan(i*4));
            if (!colors.TryGetValue(rgba, out var index))
            {
                if (colors.Count == 256) return fallback;
                index=(byte)colors.Count; colors.Add(rgba,index);
            }
            indices[i]=index;
        }
        using var compressed = new MemoryStream();
        using (var zlib=new ZLibStream(compressed, CompressionLevel.Fastest, true))
            for(int y=0;y<height;y++) { zlib.WriteByte(0); zlib.Write(indices.AsSpan(y*width,width)); }
        var chunks = QuantizationFixtures.Parse(fallback); chunks[0].Data[9]=3;
        chunks.RemoveAll(c=>c.Type is "PLTE" or "tRNS" or "IDAT");
        var palette=colors.OrderBy(p=>p.Value).Select(p=>p.Key).ToArray();
        chunks.InsertRange(chunks.Count-1, new[] {
            new QuantizationFixtures.Chunk("PLTE",palette.SelectMany(v=>new[]{(byte)(v>>24),(byte)(v>>16),(byte)(v>>8)}).ToArray()),
            new QuantizationFixtures.Chunk("tRNS",palette.Select(v=>(byte)v).ToArray()),
            new QuantizationFixtures.Chunk("IDAT",compressed.ToArray()) });
        return QuantizationFixtures.Pack(chunks);
    }

    private static byte[] ReadBounded(string path)
    {
        if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0 || new FileInfo(path).Length>16*1024*1024)
            throw new InvalidDataException("Ordinary copied PNG up to 16 MiB required.");
        return File.ReadAllBytes(path);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
