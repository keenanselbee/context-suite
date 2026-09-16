using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class OfficeWordRevisionInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium)
    {
        reportPath = Path.GetFullPath(reportPath);
        if (!reportPath.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained isolated application exports.");
        var root = Path.GetDirectoryName(reportPath)!;
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var data = report.RootElement;
        if (!data.GetProperty("Passed").GetBoolean() || data.GetProperty("Mode").GetString() != "WordRevisions" ||
            data.GetProperty("Results").GetArrayLength() != 13 || data.GetProperty("Profiles").GetArrayLength() != 13 ||
            data.GetProperty("Profiles").EnumerateArray().Any(item => !item.GetProperty("Removed").GetBoolean() || !item.GetProperty("ContextRetired").GetBoolean()))
            throw new IOException("Thirteen completed exports and native cleanup are required.");
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "revision-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); Console.WriteLine("Word revision PDF inspection: " + folder);
        var fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
        var names = WordRevisionFixtures.Create(fixtures).Concat(WordRevisionStructureFixtures.Create(fixtures)).Select(item => item.Name).ToHashSet();
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var renders = new Dictionary<string, (string Folder, JsonElement Render, string[] Text)>();
        var observations = new List<object>();
        var allText = true;
        foreach (var entry in data.GetProperty("Results").EnumerateArray())
        {
            var source = entry.GetProperty("Path").GetString()!;
            var name = Path.GetFileName(source);
            var publication = entry.GetProperty("Publication");
            var output = publication.GetProperty("OutputPath").GetString()!;
            if (!names.Remove(name) || Path.GetDirectoryName(source) != Path.Combine(root, "originals") ||
                Path.GetDirectoryName(output) != Path.Combine(root, "output") || source == output ||
                entry.GetProperty("State").GetInt32() != 5 || !publication.GetProperty("IsCommitted").GetBoolean() ||
                publication.GetProperty("Outcome").GetInt32() != 0 || publication.GetProperty("SourcePath").GetString() != source ||
                new FileInfo(source).Length != publication.GetProperty("SourceBytes").GetInt64() ||
                new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
                throw new IOException("Unexpected revision publication identity.");
            var original = data.GetProperty("Originals").GetProperty(source);
            var sourceHash = Hash(source); var outputHash = Hash(output);
            var sourceTime = File.GetLastWriteTimeUtc(source); var outputTime = File.GetLastWriteTimeUtc(output);
            if (sourceHash != original.GetProperty("Sha256").GetString() || sourceTime != original.GetProperty("Written").GetDateTime())
                throw new IOException("Revision original changed.");
            VerifyParts(source, Path.Combine(fixtures, name));
            var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(name)); Directory.CreateDirectory(target);
            var structure = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", output], target);
            File.WriteAllText(Path.Combine(target, "qpdf.txt"), structure.Output + structure.Error);
            var rendered = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", output, Path.Combine(target, "page"), "96", "opaque", "no-widgets"], target);
            File.WriteAllText(Path.Combine(target, "render.json"), rendered.Output);
            using var parsed = JsonDocument.Parse(rendered.Output);
            var render = parsed.RootElement.Clone();
            var pages = render.GetProperty("pages").EnumerateArray().ToArray();
            if (pages.Length != 1 || pages[0].GetProperty("widthPoints").GetDouble() != 612 || pages[0].GetProperty("heightPoints").GetDouble() != 792)
                throw new IOException("Unexpected Word page geometry.");
            var text = PdfTextReader.Read(output);
            File.WriteAllText(Path.Combine(target, "text.json"), JsonSerializer.Serialize(text));
            var matches = name.StartsWith("Word structures ", StringComparison.Ordinal)
                ? WordRevisionStructureFixtures.Observe(name, "final-text", text).Matches
                : text.Length == 1 && Regex.Replace(text[0], @"\s+", " ").Trim() == "CONTROL_MARKER INSERTED_MARKER END_MARKER";
            allText &= matches;
            renders.Add(name, (target, render, text));
            if (Hash(source) != sourceHash || Hash(output) != outputHash || File.GetLastWriteTimeUtc(source) != sourceTime || File.GetLastWriteTimeUtc(output) != outputTime)
                throw new IOException("Inspection inputs changed.");
            observations.Add(new { Name = name, SourceSha256 = sourceHash, OutputSha256 = outputHash, Text = text, TextMatches = matches });
        }
        var comparisons = new List<object>();
        var exact = true;
        foreach (var variant in new[] { "shown", "hidden", "unspecified" })
            Compare("inline-" + variant, "Word revisions clean.docx", "Word revisions " + variant + ".docx", expectSame: true);
        foreach (var kind in new[] { "format", "table", "move" })
        {
            Compare(kind + "-final", $"Word structures {kind} clean.docx", $"Word structures {kind} tracked.docx", expectSame: true);
            Compare(kind + "-before", $"Word structures {kind} clean.docx", $"Word structures {kind} before.docx", expectSame: false);
        }
        var passed = names.Count == 0 && allText && exact;
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Passed = passed,
            TextPassed = allText, PixelComparisonsPassed = exact, Observations = observations, Comparisons = comparisons },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Thirteen Word PDFs: text={allText}, exact final/control pixel matrix={exact}. Retain individual comparisons.");
        return passed ? 0 : 1;

        void Compare(string label, string baseline, string candidate, bool expectSame)
        {
            var before = renders[baseline]; var after = renders[candidate];
            var result = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
            var matches = result.Pages.All(page => page.ExactPixels) == expectSame;
            exact &= matches;
            comparisons.Add(new { Case = label, ExpectedSamePixels = expectSame, Passed = matches, Comparison = result });
        }
    }

    private static void VerifyParts(string source, string expected)
    {
        using var actual = ZipFile.OpenRead(source); using var authored = ZipFile.OpenRead(expected);
        if (actual.Entries.Count != 5 || authored.Entries.Count != 5) throw new IOException("Changed authored package parts.");
        foreach (var entry in authored.Entries)
        {
            var found = actual.Entries.SingleOrDefault(item => item.FullName == entry.FullName);
            if (found is null || found.Length != entry.Length || found.Length > 65536) throw new IOException("Changed authored part.");
            using var before = entry.Open(); using var after = found.Open();
            if (!SHA256.HashData(before).SequenceEqual(SHA256.HashData(after))) throw new IOException("Changed authored XML declarations.");
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
