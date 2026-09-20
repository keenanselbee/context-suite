using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OfficeWordLayoutInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium)
    {
        reportPath = Path.GetFullPath(reportPath);
        if (!reportPath.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored application exports.");
        var root = Path.GetDirectoryName(reportPath)!;
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var data = report.RootElement;
        if (!data.GetProperty("Passed").GetBoolean() || data.GetProperty("Mode").GetString() != "WordLayout" ||
            data.GetProperty("Results").GetArrayLength() != 1 || data.GetProperty("Profiles").GetArrayLength() != 1 ||
            data.GetProperty("Profiles").EnumerateArray().Any(item => !item.GetProperty("Removed").GetBoolean() || !item.GetProperty("ContextRetired").GetBoolean()))
            throw new IOException("Complete authored Word publication and cleanup evidence is required.");
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "word-layout-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
        var expected = WordLayoutFixtures.Create(fixtures).ToHashSet(StringComparer.Ordinal);
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var observations = new List<object>();
        var passed = true;
        var controls = false;
        foreach (var entry in data.GetProperty("Results").EnumerateArray())
        {
            var source = entry.GetProperty("Path").GetString()!;
            var name = Path.GetFileName(source);
            var publication = entry.GetProperty("Publication");
            var output = publication.GetProperty("OutputPath").GetString()!;
            if (!expected.Remove(name) || Path.GetDirectoryName(source) != Path.Combine(root, "originals") ||
                Path.GetDirectoryName(output) != Path.Combine(root, "output") ||
                entry.GetProperty("State").GetInt32() != 5 || !publication.GetProperty("IsCommitted").GetBoolean() ||
                publication.GetProperty("Outcome").GetInt32() != 0 || publication.GetProperty("SourcePath").GetString() != source ||
                new FileInfo(source).Length != publication.GetProperty("SourceBytes").GetInt64() ||
                new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
                throw new IOException("Unexpected Word publication identity.");
            var original = data.GetProperty("Originals").GetProperty(source);
            var sourceHash = Hash(source); var outputHash = Hash(output);
            var sourceTime = File.GetLastWriteTimeUtc(source); var outputTime = File.GetLastWriteTimeUtc(output);
            if (sourceHash != original.GetProperty("Sha256").GetString() || sourceTime != original.GetProperty("Written").GetDateTime())
                throw new IOException("Authored Word source changed.");
            VerifyParts(source, Path.Combine(fixtures, name));
            var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(name)); Directory.CreateDirectory(target);
            var structure = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", output], target);
            File.WriteAllText(Path.Combine(target, "qpdf.txt"), structure.Output + structure.Error);
            var rendered = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", output, Path.Combine(target, "page"), "96", "opaque", "no-widgets"], target);
            File.WriteAllText(Path.Combine(target, "render.json"), rendered.Output);
            using var parsed = JsonDocument.Parse(rendered.Output);
            var render = parsed.RootElement.Clone();
            var pages = render.GetProperty("pages").EnumerateArray().ToArray();
            var glyphs = new List<OfficeLayoutGlyphs.Glyph[]>();
            var text = PdfTextReader.Read(output, (pdfPage, page) => glyphs.Add(OfficeLayoutGlyphs.Read(pdfPage, page)));
            File.WriteAllText(Path.Combine(target, "text.json"), JsonSerializer.Serialize(text));
            var geometry = pages.Length == 1 && text.Length == 1 && glyphs.Count == 1 &&
                Math.Abs(pages[0].GetProperty("widthPoints").GetDouble() - 612) < .1 &&
                Math.Abs(pages[0].GetProperty("heightPoints").GetDouble() - 792) < .1;
            WordLayoutOracle.Observation? layout = null;
            if (geometry)
            {
                var page = pages[0];
                layout = WordLayoutOracle.Inspect(text[0], glyphs[0], File.ReadAllBytes(Path.Combine(target, "page-1.bgra")),
                    page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), page.GetProperty("stride").GetInt32(),
                    page.GetProperty("widthPoints").GetDouble(), page.GetProperty("heightPoints").GetDouble());
                controls = WordLayoutOracle.NegativeControls(text[0], glyphs[0], File.ReadAllBytes(Path.Combine(target, "page-1.bgra")),
                    page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(), page.GetProperty("stride").GetInt32(),
                    page.GetProperty("widthPoints").GetDouble(), page.GetProperty("heightPoints").GetDouble());
            }
            passed &= geometry && layout is { Passed: true };
            if (Hash(source) != sourceHash || Hash(output) != outputHash ||
                sourceTime != File.GetLastWriteTimeUtc(source) || outputTime != File.GetLastWriteTimeUtc(output))
                throw new IOException("Source or PDF changed during inspection.");
            observations.Add(new { Name = name, SourceSha256 = sourceHash, OutputSha256 = outputHash,
                Text = text, Layout = layout, GeometryMatches = geometry, Render = render });
        }
        if (expected.Count != 0) throw new IOException("Incomplete Word matrix.");
        passed &= controls;
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, SourceReport = reportPath, SourceReportSha256 = Hash(reportPath),
            Observations = observations, NegativeControls = controls
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Word combining accents, CJK/RTL placement, font programs, anchored image and negative controls passed={passed}.");
        return passed ? 0 : 1;
    }

    private static void VerifyParts(string source, string expected)
    {
        using var actual = ZipFile.OpenRead(source); using var authored = ZipFile.OpenRead(expected);
        if (actual.Entries.Count != authored.Entries.Count) throw new IOException("Changed authored package parts.");
        foreach (var entry in authored.Entries)
        {
            var found = actual.Entries.SingleOrDefault(item => item.FullName == entry.FullName);
            if (found is null || found.Length != entry.Length || found.Length > 65536) throw new IOException("Changed authored part.");
            using var before = entry.Open(); using var after = found.Open();
            if (!SHA256.HashData(before).SequenceEqual(SHA256.HashData(after))) throw new IOException("Changed authored XML.");
        }
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
