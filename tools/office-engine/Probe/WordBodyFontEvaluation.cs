using ContextSuite.Core.Analysis;
using System.Security.Cryptography;
using System.Text.Json;

// Read-only inspection of the retained, independently authored native controls.
internal static class WordBodyFontEvaluation
{
    internal static async Task<int> RunAsync(string directory)
    {
        var folder = Path.GetFullPath(directory);
        if (!folder.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(folder) != "office-fixtures")
            throw new IOException("Use the owned Office isolation fixture directory.");
        var results = new List<object>();
        foreach (var (variant, expected) in new[] { ("control", "Arial"), ("missing", OfficeFontFixtures.MissingFont) })
        {
            var name = "Word font " + variant + ".docx";
            var path = Path.Combine(folder, name);
            var written = File.GetLastWriteTimeUtc(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var before = Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token));
            input.Position = 0;
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync(path, input, deadline.Token);
            var fonts = result.WordFonts;
            if (result.FormatId != "docx" || result.Refusal is not null || fonts is not { Available: true, TextRuns: 5, ResolvedRuns: 1 } ||
                !fonts.Families.SequenceEqual([expected]) || !fonts.CoverageIssues.Contains("additional-content") ||
                !fonts.CoverageIssues.Contains("font-selection-unavailable") || !fonts.CoverageIssues.Contains("unsupported-text-context"))
                throw new InvalidDataException("Unexpected retained Word font evidence: " + JsonSerializer.Serialize(fonts));
            input.Position = 0;
            if (before != Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token)) || written != File.GetLastWriteTimeUtc(path))
                throw new IOException("Source changed during inspection.");
            results.Add(new { Name = name, Sha256 = before, Unchanged = true, ExpectedFamily = expected, Observation = fonts });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, Results = results,
            Scope = "One explicit Latin-font text run per retained Word control. Defaults, tables and other stories remain unresolved. No native export." },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
