using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class OfficePowerPointInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium, bool layout = false)
    {
        reportPath = Path.GetFullPath(reportPath);
        if (!reportPath.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored application exports.");
        var root = Path.GetDirectoryName(reportPath)!;
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var data = report.RootElement;
        if (!data.GetProperty("Passed").GetBoolean() || data.GetProperty("Mode").GetString() != (layout ? "PowerPointLayout" : "PowerPointSlides") ||
            data.GetProperty("Results").GetArrayLength() != (layout ? 1 : 3) || data.GetProperty("Profiles").GetArrayLength() != (layout ? 1 : 3) ||
            data.GetProperty("Profiles").EnumerateArray().Any(item => !item.GetProperty("Removed").GetBoolean() || !item.GetProperty("ContextRetired").GetBoolean()))
            throw new IOException("Complete authored PowerPoint publication and cleanup evidence is required.");
        var folder = Path.Combine(Path.GetDirectoryName(root)!, (layout ? "powerpoint-layout" : "slides") + "-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
        OfficeFixtures.Create(fixtures);
        if (layout) PowerPointLayoutFixtures.Create(fixtures); else PowerPointSlideFixtures.Create(fixtures);
        var expected = new Dictionary<string, int[]>
        {
            ["PowerPoint slides reordered.pptx"] = [3, 1, 2],
            ["PowerPoint slides hidden-ends.pptx"] = [2],
            ["PowerPoint slides notes-excluded.pptx"] = [1, 2, 3]
        };
        if (layout) expected = new() { [PowerPointLayoutFixtures.Name] = [1, 2, 3] };
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var observations = new List<object>();
        var passed = true;
        var layoutControls = true;
        var layoutControlPages = 0;
        foreach (var entry in data.GetProperty("Results").EnumerateArray())
        {
            var source = entry.GetProperty("Path").GetString()!;
            var name = Path.GetFileName(source);
            var publication = entry.GetProperty("Publication");
            var output = publication.GetProperty("OutputPath").GetString()!;
            if (!expected.Remove(name, out var order) || Path.GetDirectoryName(source) != Path.Combine(root, "originals") ||
                Path.GetDirectoryName(output) != Path.Combine(root, "output") ||
                entry.GetProperty("State").GetInt32() != 5 || !publication.GetProperty("IsCommitted").GetBoolean() ||
                publication.GetProperty("Outcome").GetInt32() != 0 || publication.GetProperty("SourcePath").GetString() != source ||
                new FileInfo(source).Length != publication.GetProperty("SourceBytes").GetInt64() ||
                new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
                throw new IOException("Unexpected PowerPoint publication identity.");
            var original = data.GetProperty("Originals").GetProperty(source);
            var sourceHash = Hash(source); var outputHash = Hash(output);
            var sourceTime = File.GetLastWriteTimeUtc(source); var outputTime = File.GetLastWriteTimeUtc(output);
            if (sourceHash != original.GetProperty("Sha256").GetString() || sourceTime != original.GetProperty("Written").GetDateTime())
                throw new IOException("Authored PowerPoint source changed.");
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
            var text = PdfTextReader.Read(output, layout ? (pdfPage, page) => glyphs.Add(OfficeLayoutGlyphs.Read(pdfPage, page)) : null);
            File.WriteAllText(Path.Combine(target, "text.json"), JsonSerializer.Serialize(text));
            var geometry = pages.Length == order.Length && pages.All(page =>
                Math.Abs(page.GetProperty("widthPoints").GetDouble() - 720) < .1 &&
                Math.Abs(page.GetProperty("heightPoints").GetDouble() - 405) < .1);
            var layouts = new List<PowerPointLayoutOracle.Observation>();
            var layoutGuardResults = new List<bool>();
            if (layout && geometry && text.Length == 3 && glyphs.Count == 3)
                for (var index = 0; index < 3; index++)
                {
                    var page = pages[index];
                    var pixels = File.ReadAllBytes(Path.Combine(target, $"page-{index + 1}.bgra"));
                    var width = page.GetProperty("width").GetInt32(); var height = page.GetProperty("height").GetInt32();
                    var stride = page.GetProperty("stride").GetInt32();
                    var pageWidth = page.GetProperty("widthPoints").GetDouble(); var pageHeight = page.GetProperty("heightPoints").GetDouble();
                    layouts.Add(PowerPointLayoutOracle.Inspect(index, text[index], glyphs[index], pixels, width, height, stride, pageWidth, pageHeight));
                    var guardResult = PowerPointLayoutOracle.NegativeControls(index, text[index], glyphs[index], pixels, width, height, stride, pageWidth, pageHeight);
                    layoutGuardResults.Add(guardResult);
                    layoutControls &= guardResult;
                    layoutControlPages++;
                }
            var matches = layout ? layouts.Count == 3 && layouts.All(item => item.Passed) : Matches(text, order);
            passed &= geometry && matches;
            if (Hash(source) != sourceHash || Hash(output) != outputHash ||
                sourceTime != File.GetLastWriteTimeUtc(source) || outputTime != File.GetLastWriteTimeUtc(output))
                throw new IOException("Source or PDF changed during inspection.");
            observations.Add(new { Name = name, SourceSha256 = sourceHash, OutputSha256 = outputHash,
                ExpectedOrder = order, Text = text, TextMatches = layout ? layouts.Count == 3 && layouts.All(item => item.TextMatches) : matches,
                Layouts = layouts, LayoutControlResults = layoutGuardResults, GeometryMatches = geometry, Render = render });
        }
        if (expected.Count != 0) throw new IOException("Incomplete PowerPoint matrix.");
        var controls = layout ? layoutControlPages == 3 && layoutControls : !Matches(["SLIDE_1_MARKER", "SLIDE_2_MARKER", "SLIDE_3_MARKER"], [3, 1, 2]) &&
            !Matches(["SLIDE_1_MARKER", "SLIDE_2_MARKER", "SLIDE_3_MARKER"], [2]) &&
            !Matches(["SLIDE_1_MARKER NOTE_1_MARKER", "SLIDE_2_MARKER", "SLIDE_3_MARKER"], [1, 2, 3]) &&
            !Matches(["SLIDE_1_MARKER extra", "SLIDE_2_MARKER", "SLIDE_3_MARKER"], [1, 2, 3]);
        passed &= controls;
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, SourceReport = reportPath, SourceReportSha256 = Hash(reportPath),
            Observations = observations, NegativeTextControls = controls
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(layout ? $"PowerPoint image, diagram/table and typography inspection passed={passed}." :
            $"Three PowerPoint publications: slide order, hidden slides, absent notes, page geometry and four negative text controls passed={passed}.");
        return passed ? 0 : 1;
    }

    private static bool Matches(string[] text, int[] order) => text.Length == order.Length &&
        text.Select((page, index) => Regex.Replace(page, @"\s+", " ").Trim() == $"SLIDE_{order[index]}_MARKER").All(value => value);

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
