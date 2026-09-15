using System.Security.Cryptography;
using System.Text.Json;

internal static class OfficeIsolatedExportInspection
{
    internal static async Task<int> RunAsync(string stage, string qpdf, string pdfium, string caseName = "case")
    {
        stage = Path.GetFullPath(stage);
        if (!stage.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained isolated export evidence.");
        if (caseName != "case" && (caseName.Length is < 3 or > 10 || !caseName.StartsWith("cs", StringComparison.Ordinal) ||
            caseName.AsSpan(2).ContainsAnyExceptInRange('0', '9')))
            throw new IOException("Use a retained named isolation case.");
        var root = Path.Combine(stage, caseName);
        if (!File.Exists(Path.Combine(root, "profile-cleanup.json"))) throw new IOException("Wait for the isolation test to finish cleanup.");
        var output = Path.Combine(stage, "inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Console.WriteLine("Isolated export inspection: " + output);
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(stage, "office-fixtures", "fixtures.json")));
        var expectedNames = new HashSet<string>(StringComparer.Ordinal) { "Word \u00fc.docx", "Excel \u00fc.xlsx", "PowerPoint \u00fc.pptx", "settings.xcu" };
        foreach (var item in fixtures.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("Name").GetString()!;
            if (!expectedNames.Remove(name)) throw new InvalidDataException("Unexpected or duplicate fixture receipt.");
            var path = Path.Combine(stage, "office-fixtures", name);
            if (Hash(path) != item.GetProperty("Sha256").GetString() || File.GetLastWriteTimeUtc(path) != item.GetProperty("WriteTimeUtc").GetDateTime())
                throw new InvalidDataException("Authored source/settings changed.");
        }
        if (expectedNames.Count != 0) throw new InvalidDataException("Incomplete fixture receipt.");
        var results = new List<object>(); var comparisons = new List<object>(); var passed = true;
        foreach (var family in new[] { "Word", "Excel", "PowerPoint" })
        {
            (string Folder, JsonElement Render, string[] Text)? baseline = null;
            foreach (var kind in new[] { "control", "isolated" })
            {
                var name = family + "-" + kind;
                var folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
                try
                {
                    var statePath = Path.Combine(root, name + "-export.json");
                    if (!File.Exists(statePath)) throw new InvalidDataException("No completed export record; inspect retained initialization diagnostic.");
                    using var state = JsonDocument.Parse(File.ReadAllText(statePath));
                    if (!state.RootElement.GetProperty("completed").GetBoolean()) throw new InvalidDataException("Engine did not complete a bounded export.");
                    var profile = (family == "Word" ? "w" : family == "Excel" ? "x" : "p") + (kind == "control" ? "c" : "i");
                    OfficeProfileSettings.Verify(Path.Combine(root, "writable", profile, "user", "registrymodifications.xcu"));
                    var pdf = Path.Combine(root, "writable", name, family + " \u00fc.pdf"); var hash = Hash(pdf);
                    await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", pdf], folder);
                    var inspection = await OfficeEvaluationProcess.RunAsync(pdfium, ["inspect", pdf], folder);
                    using var facts = JsonDocument.Parse(inspection.Output);
                    var count = family == "Excel" ? 1 : 2;
                    if (facts.RootElement.GetProperty("pages").GetInt32() != count) throw new InvalidDataException("Unexpected page count.");
                    var rendered = await OfficeEvaluationProcess.RunAsync(pdfium,
                        ["render", pdf, Path.Combine(folder, "page"), "96", "opaque", "no-widgets"], folder);
                    using var render = JsonDocument.Parse(rendered.Output);
                    var width = family == "PowerPoint" ? 720 : 612; var height = family == "PowerPoint" ? 405 : 792;
                    if (render.RootElement.GetProperty("pages").EnumerateArray().Any(page =>
                        Math.Abs(page.GetProperty("widthPoints").GetDouble() - width) > 0.1 ||
                        Math.Abs(page.GetProperty("heightPoints").GetDouble() - height) > 0.1)) throw new InvalidDataException("Unexpected page geometry.");
                    var text = PdfTextReader.Read(pdf);
                    var matches = family switch
                    {
                        "Word" => text.Length == 2 && text[0].Contains("page one") && text[0].Contains("caf\u00e9 \u00fc") &&
                            text[0].Contains("Value 42") && text[1].Contains("page two") && text[0].Contains("FIRST PAGE HEADER") &&
                            text[1].Contains("RUNNING HEADER") && text[0].Contains("Page 1 of 2") && text[1].Contains("Page 2 of 2"),
                        "Excel" => text.Length == 1 && text[0].Contains("Excel caf\u00e9") && text[0].Contains("Formula result") &&
                            text[0].Contains('5') && !text[0].Contains("HIDDEN") && !text[0].Contains("OUTSIDE"),
                        _ => text.Length == 2 && text[0].Contains("slide 1 caf\u00e9") && text[1].Contains("slide 3 caf\u00e9") && text.All(page => !page.Contains("HIDDEN"))
                    };
                    File.WriteAllText(Path.Combine(folder, "text.json"), JsonSerializer.Serialize(text));
                    File.WriteAllText(Path.Combine(folder, "render.json"), rendered.Output);
                    if (!matches || Hash(pdf) != hash) throw new InvalidDataException("Authored text or PDF preservation differs.");
                    if (kind == "control") baseline = (folder, render.RootElement.Clone(), text);
                    else
                    {
                        if (baseline is not { } reference) throw new InvalidDataException("No passing ordinary export for comparison.");
                        var comparison = LegacyPdfComparison.Compare(reference.Folder, reference.Render, reference.Text, folder, render.RootElement, text);
                        comparisons.Add(new { family, comparison });
                        passed &= comparison.TextEqual && comparison.Pages.All(page => page.ExactPixels);
                    }
                    results.Add(new { name, passed = true, pdf, sha256 = hash, pages = count });
                    Console.WriteLine("PASS: " + name + " structure, authored text, page geometry, settings and unchanged PDF.");
                }
                catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception)
                {
                    results.Add(new { name, passed = false, reason = error.Message }); passed = false;
                    Console.WriteLine("FAIL: " + name + ": " + error.Message);
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed, caseRoot = root, results, comparisons }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
