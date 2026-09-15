using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Office;

internal static class OfficeWorkerInspection
{
    internal static async Task<int> RunAsync(string reportPath, string qpdf, string pdfium, string control)
    {
        reportPath = Path.GetFullPath(reportPath); control = Path.GetFullPath(control);
        if (!reportPath.Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase) ||
            !control.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use completed worker evidence and retained isolated controls.");
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        if (!report.RootElement.GetProperty("Passed").GetBoolean() || report.RootElement.GetProperty("Reports").GetArrayLength() != 3)
            throw new IOException("Worker export and cleanup must pass before PDF inspection.");
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(reportPath))!, "inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); Console.WriteLine("Office worker PDF inspection: " + folder);
        var remaining = new HashSet<string>(["docx", "xlsx", "pptx"], StringComparer.Ordinal);
        var results = new List<object>();
        foreach (var entry in report.RootElement.GetProperty("Reports").EnumerateArray())
        {
            var work = entry.GetProperty("Work").Deserialize<OfficeExportWork>()!;
            var candidate = entry.GetProperty("Candidate").Deserialize<OfficeExportCandidate>()!;
            candidate.Validate(work, candidate.Completion.RequestId);
            if (!remaining.Remove(work.Format) || Path.GetDirectoryName(work.DirectoryPath) != Path.GetDirectoryName(reportPath) ||
                entry.GetProperty("Output").GetString() != work.CandidatePath || Hash(work.SourcePath) != work.SourceSha256 ||
                Hash(work.CandidatePath) != candidate.Completion.OutputSha256)
                throw new IOException("Worker report does not match its owned source and candidate.");
            OfficeProfileSettings.Verify(Path.Combine(work.EngineProfilePath, "user", "registrymodifications.xcu"), work.Format == "xlsx" ? 1 : null);
            var family = work.Format == "docx" ? "Word" : work.Format == "xlsx" ? "Excel" : "PowerPoint";
            var baseline = Path.Combine(control, "writable", family + "-control", family + " ü.pdf");
            var before = await Inspect(baseline, Path.Combine(folder, family + "-control"));
            var after = await Inspect(work.CandidatePath, Path.Combine(folder, family + "-worker"));
            var pages = work.Format == "xlsx" ? 1 : 2;
            var width = work.Format == "pptx" ? 720 : 612; var height = work.Format == "pptx" ? 405 : 792;
            if (after.Render.GetProperty("pages").GetArrayLength() != pages || after.Render.GetProperty("pages").EnumerateArray().Any(page =>
                Math.Abs(page.GetProperty("widthPoints").GetDouble() - width) > 0.1 || Math.Abs(page.GetProperty("heightPoints").GetDouble() - height) > 0.1))
                throw new IOException("Unexpected exported page geometry.");
            var text = after.Text;
            var expected = work.Format switch
            {
                "docx" => text.Length == 2 && text[0].Contains("page one") && text[1].Contains("page two") &&
                    text[0].Contains("café ü") && text[0].Contains("Value 42") && text[0].Contains("FIRST PAGE HEADER") &&
                    text[1].Contains("RUNNING HEADER") && text[0].Contains("Page 1 of 2") && text[1].Contains("Page 2 of 2"),
                "xlsx" => text.Length == 1 && text[0].Contains("Excel café") && text[0].Contains("Formula result") &&
                    text[0].Contains('5') && !text[0].Contains("HIDDEN") && !text[0].Contains("OUTSIDE"),
                _ => text.Length == 2 && text[0].Contains("slide 1 café") && text[1].Contains("slide 3 café") &&
                    !text.Any(page => page.Contains("slide 2"))
            };
            var comparison = LegacyPdfComparison.Compare(before.Folder, before.Render, before.Text, after.Folder, after.Render, after.Text);
            if (!expected || !comparison.TextEqual || comparison.Pages.Any(page => !page.ExactPixels))
                throw new IOException("Worker PDF differs from authored expectations or retained control pixels/text.");
            results.Add(new { work.Format, SourceSha256 = work.SourceSha256, OutputSha256 = Hash(work.CandidatePath),
                ControlSha256 = Hash(baseline), Text = text, Comparison = comparison });
        }
        File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Passed = remaining.Count == 0, Results = results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Three Office worker PDFs pass independent structure, authored text, geometry and exact control-pixel checks.");
        return remaining.Count == 0 ? 0 : 1;

        async Task<(string Folder, JsonElement Render, string[] Text)> Inspect(string pdf, string directory)
        {
            Directory.CreateDirectory(directory);
            var result = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", pdf], directory);
            File.WriteAllText(Path.Combine(directory, "qpdf.txt"), result.Output + result.Error);
            var render = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", pdf, Path.Combine(directory, "page"), "96", "opaque", "no-widgets"], directory);
            File.WriteAllText(Path.Combine(directory, "render.json"), render.Output);
            using var parsed = JsonDocument.Parse(render.Output);
            var text = PdfTextReader.Read(pdf);
            File.WriteAllText(Path.Combine(directory, "text.json"), JsonSerializer.Serialize(text));
            return (directory, parsed.RootElement.Clone(), text);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
