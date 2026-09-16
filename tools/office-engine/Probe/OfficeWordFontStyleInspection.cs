using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class OfficeWordFontStyleInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium)
    {
        reportPath = Path.GetFullPath(reportPath);
        if (!reportPath.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored application exports.");
        var root = Path.GetDirectoryName(reportPath)!;
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var data = report.RootElement;
        if (data.GetProperty("Mode").GetString() != "WordFontStyles" ||
            data.GetProperty("Results").GetArrayLength() != WordFontStyleFixtures.Cases.Length ||
            data.GetProperty("Profiles").GetArrayLength() != WordFontStyleFixtures.Cases.Length ||
            data.GetProperty("Profiles").EnumerateArray().Any(item => !item.GetProperty("Removed").GetBoolean() || !item.GetProperty("ContextRetired").GetBoolean()))
            throw new IOException("Complete authored publication and cleanup evidence is required.");
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "style-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Console.WriteLine("Word font-style PDF inspection: " + folder);
        var fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
        WordFontStyleFixtures.Create(fixtures);
        var expected = WordFontStyleFixtures.Cases.ToDictionary(item => WordFontStyleFixtures.Name(item.Key));
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var renders = new Dictionary<string, (string Folder, JsonElement Render, string[] Text, string[] Fonts)>();
        var observations = new List<object>();
        var textMatches = true;
        var positiveFaces = true;
        foreach (var entry in data.GetProperty("Results").EnumerateArray())
        {
            var source = entry.GetProperty("Path").GetString()!;
            var name = Path.GetFileName(source);
            var publication = entry.GetProperty("Publication");
            var output = publication.GetProperty("OutputPath").GetString()!;
            if (!expected.Remove(name, out var item) || Path.GetDirectoryName(source) != Path.Combine(root, "originals") ||
                Path.GetDirectoryName(output) != Path.Combine(root, "output") || source == output ||
                entry.GetProperty("State").GetInt32() != 5 || !publication.GetProperty("IsCommitted").GetBoolean() ||
                publication.GetProperty("Outcome").GetInt32() != 0 || publication.GetProperty("SourcePath").GetString() != source ||
                new FileInfo(source).Length != publication.GetProperty("SourceBytes").GetInt64() ||
                new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
                throw new IOException("Unexpected Word font publication identity.");
            var original = data.GetProperty("Originals").GetProperty(source);
            var sourceHash = Hash(source); var outputHash = Hash(output);
            var sourceTime = File.GetLastWriteTimeUtc(source); var outputTime = File.GetLastWriteTimeUtc(output);
            if (sourceHash != original.GetProperty("Sha256").GetString() || sourceTime != original.GetProperty("Written").GetDateTime())
                throw new IOException("Authored Word source changed.");
            VerifyParts(source, Path.Combine(fixtures, name));
            var target = Path.Combine(folder, item.Key); Directory.CreateDirectory(target);
            var structure = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", output], target);
            File.WriteAllText(Path.Combine(target, "qpdf.txt"), structure.Output + structure.Error);
            var fontResult = await OfficeEvaluationProcess.RunAsync(qpdf, ["--json", "--json-key=qpdf", output], target);
            File.WriteAllText(Path.Combine(target, "fonts.json"), fontResult.Output);
            var fontNames = OfficeFontFixtures.Observe(fontResult.Output)
                .Select(font => Regex.Replace(font, "^/[A-Z]{6}\\+", "/")).Distinct().Order(StringComparer.Ordinal).ToArray();
            var rendered = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", output, Path.Combine(target, "page"), "96", "opaque", "no-widgets"], target);
            File.WriteAllText(Path.Combine(target, "render.json"), rendered.Output);
            using var parsed = JsonDocument.Parse(rendered.Output);
            var render = parsed.RootElement.Clone();
            var pages = render.GetProperty("pages").EnumerateArray().ToArray();
            if (pages.Length != 1 || pages[0].GetProperty("widthPoints").GetDouble() != 612 || pages[0].GetProperty("heightPoints").GetDouble() != 792)
                throw new IOException("Unexpected font-style page geometry.");
            var text = PdfTextReader.Read(output);
            File.WriteAllText(Path.Combine(target, "text.json"), JsonSerializer.Serialize(text));
            var matches = text.Length == 1 && Regex.Replace(text[0], @"\s+", " ").Trim() == WordFontStyleFixtures.Text;
            textMatches &= matches;
            // Source-family labels are corroborated for the three explicit controls.
            // Names alone do not establish glyph or embedded-program fidelity.
            var faceMatches = item.Key is not ("arial-control" or "courier-control" or "times-control") ||
                fontNames.Length == 1 && fontNames[0].StartsWith("/" + item.Family.Replace(" ", ""), StringComparison.Ordinal);
            positiveFaces &= faceMatches;
            if (Hash(source) != sourceHash || Hash(output) != outputHash ||
                sourceTime != File.GetLastWriteTimeUtc(source) || outputTime != File.GetLastWriteTimeUtc(output))
                throw new IOException("Source or PDF changed during independent inspection.");
            renders.Add(item.Key, (target, render, text, fontNames));
            observations.Add(new { Name = name, item.Family, item.Control, SourceSha256 = sourceHash, OutputSha256 = outputHash,
                TextMatches = matches, ControlFaceMatches = faceMatches, Fonts = fontNames, Render = render });
        }
        if (expected.Count != 0) throw new IOException("Incomplete font-style export matrix.");
        var comparisons = new List<object>();
        var exact = true;
        foreach (var item in WordFontStyleFixtures.Cases.Where(item => item.Key != item.Control))
        {
            var before = renders[item.Control]; var after = renders[item.Key];
            var comparison = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
            var matches = comparison.TextEqual && comparison.Pages.All(page => page.ExactPixels) && before.Fonts.SequenceEqual(after.Fonts);
            exact &= matches;
            comparisons.Add(new { Case = item.Key, Control = item.Control, Passed = matches, Comparison = comparison });
        }
        var controlsDistinct = true;
        foreach (var other in new[] { "courier-control", "times-control", "missing-control" })
        {
            var before = renders["arial-control"]; var after = renders[other];
            var comparison = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
            var different = comparison.TextEqual && comparison.Pages.Any(page => !page.ExactPixels);
            controlsDistinct &= different;
            comparisons.Add(new { Case = "negative-" + other, Control = "arial-control", Passed = different, Comparison = comparison });
        }
        var passed = data.GetProperty("Passed").GetBoolean() && data.GetProperty("ReviewMatches").GetBoolean() &&
            textMatches && positiveFaces && exact && controlsDistinct;
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = passed, SourceReport = reportPath, SourceReportSha256 = Hash(reportPath), TextMatches = textMatches,
            PositiveControlFaces = positiveFaces, ExactStyleControls = exact, ControlsDistinct = controlsDistinct,
            Observations = observations, Comparisons = comparisons
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Fifteen Word font-style PDFs: text={textMatches}, control faces={positiveFaces}, exact style controls={exact}, distinct controls={controlsDistinct}.");
        return passed ? 0 : 1;
    }

    private static void VerifyParts(string source, string expected)
    {
        using var actual = ZipFile.OpenRead(source); using var authored = ZipFile.OpenRead(expected);
        if (authored.Entries.Count is not (7 or 8) || actual.Entries.Count != authored.Entries.Count)
            throw new IOException("Changed authored font-style package parts.");
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
