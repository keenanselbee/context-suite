using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class ExcelWorkbookCapacityInspection
{
    internal static async Task<int> RunAsync(string applicationReport, string controlPdf, string evaluation, string qpdf, string pdfium)
    {
        using var application = JsonDocument.Parse(File.ReadAllText(applicationReport));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(evaluation, "results.json")));
        if (!application.RootElement.GetProperty("Passed").GetBoolean() || !native.RootElement.GetProperty("ExportSucceeded").GetBoolean() ||
            native.RootElement.GetProperty("ProcessMemoryMiB").GetInt32() != 1024)
            throw new IOException("Use completed application and capacity-control evidence.");
        var entries = application.RootElement.GetProperty("Reports")[0].GetProperty("Results");
        var entry = entries.EnumerateArray().Single(item => Path.GetExtension(item.GetProperty("Path").GetString()) == ".xlsx");
        var publication = entry.GetProperty("Publication");
        var source = entry.GetProperty("Path").GetString()!;
        var output = publication.GetProperty("OutputPath").GetString()!;
        if (entry.GetProperty("State").GetInt32() != 5 || !publication.GetProperty("IsCommitted").GetBoolean() ||
            publication.GetProperty("Outcome").GetInt32() != 0 || publication.GetProperty("SourcePath").GetString() != source ||
            Hash(source) != native.RootElement.GetProperty("SourceSha256").GetString() ||
            new FileInfo(output).Length != publication.GetProperty("OutputBytes").GetInt64())
            throw new IOException("Application publication does not match the authored workbook.");
        var inputs = new[] { controlPdf, Path.Combine(evaluation, "output", "before.pdf"), Path.Combine(evaluation, "output", "after.pdf"), output };
        if (inputs.Any(path => !Path.GetFullPath(path).Contains("\\.codex-temp\\", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Use retained repository evidence.");
        var hashes = inputs.ToDictionary(path => path, Hash);
        var root = Path.Combine(Path.GetDirectoryName(applicationReport)!, "capacity-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Console.WriteLine("Workbook capacity inspection: " + root);
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var split = new List<string[]>();
        for (var index = 0; index < inputs.Length; index++)
        {
            var folder = Path.Combine(root, "pdf-" + index); Directory.CreateDirectory(folder);
            var check = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", inputs[index]], folder);
            File.WriteAllText(Path.Combine(folder, "qpdf.txt"), check.Output + check.Error);
            await OfficeEvaluationProcess.RunAsync(qpdf, [inputs[index], "--split-pages=1", Path.Combine(folder, "part-%d.pdf")], folder);
            var pages = Directory.GetFiles(folder, "part-*.pdf").Order(StringComparer.Ordinal).ToArray();
            if (pages.Length != 48) throw new IOException("Expected 48 independently parsed workbook pages.");
            split.Add(pages);
        }
        var results = new List<object>();
        for (var page = 0; page < 48; page++)
        {
            var expectedText = "Export interruption sheet " + (page + 1);
            (string Folder, JsonElement Render, string[] Text)? baseline = null;
            for (var variant = 0; variant < split.Count; variant++)
            {
                var pdf = split[variant][page]; var folder = Path.Combine(Path.GetDirectoryName(pdf)!, "render-" + (page + 1)); Directory.CreateDirectory(folder);
                var rendered = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", pdf, Path.Combine(folder, "page"), "96", "opaque", "no-widgets"], folder);
                File.WriteAllText(Path.Combine(folder, "render.json"), rendered.Output);
                using var json = JsonDocument.Parse(rendered.Output);
                var text = PdfTextReader.Read(pdf);
                if (text.Length != 1 || Regex.Replace(text[0], @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Trim() != expectedText)
                    throw new IOException("Authored sheet text or order changed.");
                var geometry = json.RootElement.GetProperty("pages")[0];
                if (Math.Abs(geometry.GetProperty("widthPoints").GetDouble() - 612) > 0.1 || Math.Abs(geometry.GetProperty("heightPoints").GetDouble() - 792) > 0.1)
                    throw new IOException("Workbook page geometry changed.");
                if (baseline is null) baseline = (folder, json.RootElement.Clone(), text);
                else
                {
                    var comparison = LegacyPdfComparison.Compare(baseline.Value.Folder, baseline.Value.Render, baseline.Value.Text, folder, json.RootElement, text);
                    if (!comparison.TextEqual || comparison.Pages.Any(item => !item.ExactPixels)) throw new IOException("Workbook rendering differs from its retained control.");
                    results.Add(new { Page = page + 1, Variant = variant, Text = text, Comparison = comparison });
                }
            }
        }
        if (hashes.Any(item => Hash(item.Key) != item.Value)) throw new IOException("Inspection inputs changed.");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, PdfCount = 4, PagesPerPdf = 48,
            Inputs = hashes, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Four PDFs preserve all 48 authored labels, page geometries and exact control pixels.");
        return 0;
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
}
