using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

// Diagnostic only: ordinary processes in bounded jobs, exclusively on freshly
// authored passive workbooks. This is not a change to shipping security policy.
internal static class ExcelChartPolicyEvaluation
{
    internal static async Task<int> RunAsync(string report, string qpdf, string pdfium)
    {
        var stage = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(report)))!;
        if (!stage.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored execution evidence.");
        var program = Path.Combine(stage, "worker", "office-engine", "runtime", "program");
        var root = Path.Combine(stage, "chart-policy-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fixtures = Path.Combine(root, "fixtures");
        var source = Path.Combine(fixtures, ExcelLayoutFixtures.Create(fixtures)[0]);
        var originalHash = Hash(source); var originalTime = File.GetLastWriteTimeUtc(source);
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        var observations = new List<object>();
        var matches = new List<bool>();
        foreach (var disabled in new[] { true, false })
        {
            var folder = Path.Combine(root, disabled ? "blocked" : "enabled-control"); Directory.CreateDirectory(folder);
            var profile = Path.Combine(folder, "profile"); var user = Path.Combine(profile, "user"); Directory.CreateDirectory(user);
            var settings = Path.Combine(user, "registrymodifications.xcu");
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
            { var path = Path.Combine(profile, name); Directory.CreateDirectory(path); environment[name] = path; }
            environment["SAL_DISABLE_OPENCL"] = "1"; environment["SAL_LOG"] = "+WARN";
            OfficeProfileSettings.Apply(settings, 0);
            var common = new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
                "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all" };
            var initialize = await OfficeEvaluationProcess.RunAsync(Path.Combine(program, "soffice.com"),
                [.. common, "--terminate_after_init"], folder, environment);
            OfficeProfileSettings.Apply(settings, 0);
            if (!disabled)
            {
                var xml = XDocument.Load(settings);
                XNamespace ns = "http://openoffice.org/2001/registry";
                xml.Descendants("prop").Single(node => (string?)node.Attribute(ns + "name") == "DisableActiveContent").Element("value")!.Value = "false";
                xml.Save(settings);
            }
            File.Copy(settings, Path.Combine(folder, "settings-before.xcu"));
            var converted = await OfficeEvaluationProcess.RunAsync(Path.Combine(program, "soffice.com"),
                [.. common, "--convert-to", "pdf:calc_pdf_Export", "--outdir", folder, source], folder, environment);
            File.Copy(settings, Path.Combine(folder, "settings-after.xcu"));
            var output = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + ".pdf");
            var structure = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", output], folder);
            var rendered = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", output, Path.Combine(folder, "page"), "96", "opaque", "no-widgets"], folder);
            using var render = JsonDocument.Parse(rendered.Output);
            var pages = render.RootElement.GetProperty("pages").EnumerateArray().ToArray();
            var glyphs = new List<ExcelLayoutOracle.Glyph[]>();
            var text = PdfTextReader.Read(output, (_, page) => glyphs.Add(ExcelLayoutOracle.ReadGlyphs(page)));
            if (pages.Length != 1 || text.Length != 1 || glyphs.Count != 1) throw new IOException("Unexpected diagnostic page count.");
            var pageInfo = pages[0];
            var layout = ExcelLayoutOracle.Inspect(text[0], glyphs[0], File.ReadAllBytes(Path.Combine(folder, "page-1.bgra")),
                pageInfo.GetProperty("width").GetInt32(), pageInfo.GetProperty("height").GetInt32(), pageInfo.GetProperty("stride").GetInt32(),
                pageInfo.GetProperty("widthPoints").GetDouble(), pageInfo.GetProperty("heightPoints").GetDouble());
            matches.Add(layout.Passed);
            observations.Add(new { DisableActiveContent = disabled, Text = text, Layout = layout,
                OutputSha256 = Hash(output), Initialize = initialize, Conversion = converted, Structure = structure, Render = render.RootElement.Clone() });
            Console.WriteLine($"Diagnostic DisableActiveContent={disabled}: complete authored layout={layout.Passed}, bars={layout.Bars.Length}.");
        }
        if (Hash(source) != originalHash || File.GetLastWriteTimeUtc(source) != originalTime)
            throw new IOException("Diagnostic source changed.");
        var expectedDifference = matches.SequenceEqual(new[] { false, true });
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = expectedDifference, SourceSha256 = originalHash, Observations = observations,
            Scope = "Passive authored chart policy comparison only. Ordinary bounded jobs; no AppContainer or production fidelity/security acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return expectedDifference ? 0 : 1;
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
