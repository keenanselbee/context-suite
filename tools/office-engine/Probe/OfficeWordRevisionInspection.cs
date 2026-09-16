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
        var mode = data.GetProperty("Mode").GetString();
        var paragraphMarks = mode == "WordParagraphRevisions";
        var expectedCount = paragraphMarks ? 6 : 13;
        if (!data.GetProperty("Passed").GetBoolean() || mode is not ("WordRevisions" or "WordParagraphRevisions") ||
            data.GetProperty("Results").GetArrayLength() != expectedCount || data.GetProperty("Profiles").GetArrayLength() != expectedCount ||
            data.GetProperty("Profiles").EnumerateArray().Any(item => !item.GetProperty("Removed").GetBoolean() || !item.GetProperty("ContextRetired").GetBoolean()))
            throw new IOException("The complete selected export matrix and native cleanup are required.");
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "revision-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); Console.WriteLine("Word revision PDF inspection: " + folder);
        var fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
        var names = (paragraphMarks ? WordRevisionStructureFixtures.Create(fixtures, true) :
            WordRevisionFixtures.Create(fixtures).Concat(WordRevisionStructureFixtures.Create(fixtures)))
            .Select(item => item.Name).ToHashSet();
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var renders = new Dictionary<string, (string Folder, JsonElement Render, string[] Text)>();
        var observations = new List<object>();
        var moveObservations = new Dictionary<string, WordMoveSpacing.Observation>();
        var allText = true;
        var allLayout = true;
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
            bool? lineLayout = null;
            if (paragraphMarks)
            {
                var parts = Path.GetFileNameWithoutExtension(name).Split(' ');
                var split = parts[2] == "paragraph-insert" ? parts[3] != "before" : parts[3] == "before";
                string[] expectedLines = split ? ["CONTROL_MARKER", "PARAGRAPH_LEFT", "PARAGRAPH_RIGHT", "END_MARKER"] :
                    ["CONTROL_MARKER", "PARAGRAPH_LEFT PARAGRAPH_RIGHT", "END_MARKER"];
                var actualLines = text.Single().Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => Regex.Replace(line, @"\s+", " ").Trim()).ToArray();
                lineLayout = actualLines.SequenceEqual(expectedLines, StringComparer.Ordinal);
                allLayout &= lineLayout.Value;
            }
            renders.Add(name, (target, render, text));
            if (name is "Word structures move clean.docx" or "Word structures move tracked.docx")
            {
                var move = WordMoveSpacing.Read(output);
                moveObservations.Add(name, move);
                File.WriteAllText(Path.Combine(target, "spacing.json"), JsonSerializer.Serialize(move));
            }
            if (Hash(source) != sourceHash || Hash(output) != outputHash || File.GetLastWriteTimeUtc(source) != sourceTime || File.GetLastWriteTimeUtc(output) != outputTime)
                throw new IOException("Inspection inputs changed.");
            observations.Add(new { Name = name, SourceSha256 = sourceHash, OutputSha256 = outputHash, Text = text,
                TextMatches = matches, LineLayoutMatches = lineLayout });
        }
        var comparisons = new List<object>();
        var exact = true;
        var fidelity = true;
        var spacingGuards = paragraphMarks ? 0 : WordMoveSpacing.VerifyGuards(moveObservations["Word structures move clean.docx"]);
        if (!paragraphMarks)
            foreach (var variant in new[] { "shown", "hidden", "unspecified" })
                Compare("inline-" + variant, "Word revisions clean.docx", "Word revisions " + variant + ".docx", expectSame: true);
        foreach (var kind in paragraphMarks ? new[] { "paragraph-delete", "paragraph-insert" } : new[] { "format", "table", "move" })
        {
            Compare(kind + "-final", $"Word structures {kind} clean.docx", $"Word structures {kind} tracked.docx", expectSame: true);
            Compare(kind + "-before", $"Word structures {kind} clean.docx", $"Word structures {kind} before.docx", expectSame: false);
        }
        var passed = names.Count == 0 && allText && allLayout && fidelity;
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Passed = passed,
            TextPassed = allText, LineLayoutPassed = paragraphMarks ? allLayout : (bool?)null,
            PixelComparisonsPassed = exact, FidelityComparisonsPassed = fidelity,
            MoveHorizontalLimitPoints = WordMoveSpacing.MaximumHorizontalPoints,
            SpacingGuardsPassed = spacingGuards,
            Observations = observations, Comparisons = comparisons },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{expectedCount} Word PDFs: text={allText}, paragraph layout={(paragraphMarks ? allLayout.ToString() : "not separately tested")}, exact final/control pixel matrix={exact}, accepted fidelity={fidelity}.");
        return passed ? 0 : 1;

        void Compare(string label, string baseline, string candidate, bool expectSame)
        {
            var before = renders[baseline]; var after = renders[candidate];
            var result = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
            var matches = result.Pages.All(page => page.ExactPixels) == expectSame;
            exact &= matches;
            WordMoveSpacing.Comparison? spacing = null;
            if (label == "move-final")
                spacing = WordMoveSpacing.Compare(moveObservations[baseline], moveObservations[candidate]);
            var accepted = spacing is null ? matches : spacing.Passed && result.TextEqual;
            fidelity &= accepted;
            comparisons.Add(new { Case = label, ExpectedSamePixels = expectSame, ExactComparisonPassed = matches,
                Passed = accepted, Spacing = spacing, Comparison = result });
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
