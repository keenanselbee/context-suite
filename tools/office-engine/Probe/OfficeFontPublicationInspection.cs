using System.Security.Cryptography;
using System.Text.Json;

internal static class OfficeFontPublicationInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium, string controlReport)
    {
        reportPath = Path.GetFullPath(reportPath); controlReport = Path.GetFullPath(controlReport);
        if (!reportPath.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) ||
            !controlReport.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use completed font-review execution and independent callback evidence.");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        using var control = JsonDocument.Parse(File.ReadAllText(controlReport));
        var root = Path.GetDirectoryName(reportPath)!;
        var controlRoot = Path.GetDirectoryName(Path.GetDirectoryName(controlReport))!;
        if (!report.RootElement.GetProperty("Passed").GetBoolean() || report.RootElement.GetProperty("Reports").GetArrayLength() != 8 ||
            report.RootElement.GetProperty("Profiles").GetArrayLength() != 13 ||
            !report.RootElement.GetProperty("Profiles").EnumerateArray().All(profile => profile.GetProperty("Removed").GetBoolean() && profile.GetProperty("ContextRetired").GetBoolean()) ||
            !control.RootElement.GetProperty("passed").GetBoolean() || !control.RootElement.GetProperty("fontComparison").GetBoolean())
            throw new IOException("Complete execution, native cleanup and independent control checks are required.");
        var baselines = control.RootElement.GetProperty("results").EnumerateArray().Where(item =>
            item.GetProperty("name").GetString()!.EndsWith("-control", StringComparison.Ordinal)).ToDictionary(
                item => item.GetProperty("name").GetString()!, item => item.Clone(), StringComparer.Ordinal);
        if (baselines.Count != 6) throw new IOException("Expected all six independent font controls.");
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var folder = Path.Combine(Path.GetDirectoryName(root)!, "font-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); Console.WriteLine("Office font PDF inspection: " + folder);
        var results = new List<object>(); var remaining = new HashSet<string>(baselines.Keys);
        var reportHash = Hash(reportPath); var controlHash = Hash(controlReport);
        foreach (var run in report.RootElement.GetProperty("Reports").EnumerateArray())
        {
            var scenario = run.GetProperty("Scenario").GetString()!;
            if (scenario is not ("quiet" or "skip-continue" or "accept-retry" or "no-handler" or "cancel" or "review-error" or "validation-failure" or "viewmodel"))
                throw new IOException("Unreviewed execution scenario.");
            foreach (var entry in run.GetProperty("Results").EnumerateArray().Where(item => item.GetProperty("State").GetInt32() == 5))
            {
                var source = entry.GetProperty("Path").GetString()!;
                var name = Path.GetFileNameWithoutExtension(source);
                var key = name.Replace(' ', '-') + "-control";
                if (!baselines.TryGetValue(key, out var baseline) || !baseline.GetProperty("passed").GetBoolean())
                    throw new IOException("Missing independently accepted source control.");
                var publication = entry.GetProperty("Publication");
                var output = publication.GetProperty("OutputPath").GetString()!;
                if (Path.GetDirectoryName(source) != Path.Combine(root, "originals") || Path.GetDirectoryName(output) != Path.Combine(root, scenario) ||
                    publication.GetProperty("SourcePath").GetString() != source || publication.GetProperty("Outcome").GetInt32() != 0 ||
                    !publication.GetProperty("IsCommitted").GetBoolean() ||
                    new FileInfo(source).Length != publication.GetProperty("SourceBytes").GetInt64() ||
                    new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
                    throw new IOException("Expected an owned, committed PDF copy.");
                var original = Path.Combine(controlRoot, "office-fixtures", Path.GetFileName(source));
                var baselinePdf = baseline.GetProperty("pdf").GetString()!;
                if (Path.GetDirectoryName(baselinePdf) != Path.Combine(controlRoot, "case", "writable", key) ||
                    Hash(source) != Hash(original) || Hash(baselinePdf) != baseline.GetProperty("sha256").GetString())
                    throw new IOException("Source or retained control identity changed.");
                var sourceHash = Hash(source); var outputHash = Hash(output);
                var before = await Inspect(baselinePdf, Path.Combine(folder, results.Count + "-control"));
                var after = await Inspect(output, Path.Combine(folder, results.Count + "-published"));
                var comparison = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
                if (!before.Text.SequenceEqual(after.Text, StringComparer.Ordinal) || !comparison.TextEqual ||
                    comparison.Pages.Any(page => !page.ExactPixels || page.ModernWidthPoints != page.LegacyWidthPoints || page.ModernHeightPoints != page.LegacyHeightPoints) ||
                    Hash(source) != sourceHash || Hash(output) != outputHash ||
                    Hash(baselinePdf) != baseline.GetProperty("sha256").GetString())
                    throw new IOException("Published PDF changed control text, geometry, pixels or inspection inputs.");
                remaining.Remove(key);
                results.Add(new { Scenario = scenario, Source = source, Pdf = output, SourceSha256 = sourceHash, OutputSha256 = outputHash,
                    ControlSha256 = Hash(baselinePdf), Comparison = comparison });
            }
        }
        if (results.Count != 7 || remaining.Count != 0 || Hash(reportPath) != reportHash || Hash(controlReport) != controlHash)
            throw new IOException("Missing complete unchanged publication/control coverage.");
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Passed = true, Results = results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Seven published PDFs match independent control text, geometry and exact pixels across all six fixtures.");
        return 0;

        async Task<(string Folder, JsonElement Render, string[] Text)> Inspect(string pdf, string directory)
        {
            Directory.CreateDirectory(directory);
            var structure = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", pdf], directory);
            File.WriteAllText(Path.Combine(directory, "qpdf.txt"), structure.Output + structure.Error);
            var render = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", pdf, Path.Combine(directory, "page"), "96", "opaque", "no-widgets"], directory);
            File.WriteAllText(Path.Combine(directory, "render.json"), render.Output);
            using var parsed = JsonDocument.Parse(render.Output);
            var text = PdfTextReader.Read(pdf);
            File.WriteAllText(Path.Combine(directory, "text.json"), JsonSerializer.Serialize(text));
            return (directory, parsed.RootElement.Clone(), text);
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
}
