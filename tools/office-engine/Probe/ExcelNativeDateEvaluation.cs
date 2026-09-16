using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Analysis;

// Passive authored fixtures only. The job bounds lifetime, not filesystem/network access.
internal static class ExcelNativeDateEvaluation
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string prepared, string qpdf, string pdfium, string native)
    {
        prepared = Path.GetFullPath(prepared);
        if (!prepared.Contains("\\.codex-temp\\office-engine\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use prepared repository-local Office scratch.");
        var root = Path.Combine(prepared, "evaluation-" + Guid.NewGuid().ToString("N"));
        var fixtures = Path.Combine(root, "fixtures"); Directory.CreateDirectory(fixtures);
        var program = Path.Combine(prepared, "unpacked", "program");
        var nativeHash = Hash(native);
        PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
        OfficeFixtures.Create(fixtures);
        var cases = ExcelDateFixtures.Create(fixtures, formulas: true);
        var results = new List<object>();
        Console.WriteLine("Office native date evidence: " + root);
        OfficeProfileContracts.Run(Path.Combine(root, "profile-contracts"));
        foreach (var (name, _, _) in cases)
        foreach (var style in new[] { "calc-always", "calc-never" })
        {
            var source = Path.Combine(fixtures, name);
            var sourceHash = Hash(source); var written = File.GetLastWriteTimeUtc(source);
            OfficeSourcePreflight preflight;
            using (var input = File.OpenRead(source))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                preflight = await OfficeSourcePreflight.InspectOpenXmlAsync(source, input, deadline.Token);
            if (preflight.FormatId != "xlsx" || preflight.Refusal is not null ||
                preflight.StoredDates is not { Complete: true, EarlyDateCells: 0, DateFormulaCells: 6 })
                throw new InvalidDataException("Authored date source preflight changed.");
            var folder = Path.Combine(root, Path.GetFileNameWithoutExtension(name) + "-" + style);
            Directory.CreateDirectory(folder);
            var profile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(prepared)!)!, "office-profile-" + Guid.NewGuid().ToString("N") + "u");
            var user = Path.Combine(profile, "user"); Directory.CreateDirectory(user);
            var settings = Path.Combine(user, "registrymodifications.xcu");
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
            {
                var path = Path.Combine(profile, variable); Directory.CreateDirectory(path); environment[variable] = path;
            }
            environment["SAL_DISABLE_OPENCL"] = "1";
            environment["SAL_LOG"] = "+WARN";
            OfficeProfileSettings.Apply(settings);
            var initialization = await OfficeEvaluationProcess.RunAsync(Path.Combine(program, "soffice.com"),
                ["-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
                    "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all", "--terminate_after_init"], root, environment);
            Save(Path.Combine(profile, "initialization.json"), initialization);
            var calculation = style == "calc-always" ? 0 : 1;
            OfficeProfileSettings.Apply(settings, calculation);
            File.Copy(settings, Path.Combine(profile, "requested-settings.xcu"), false);
            if (Hash(native) != nativeHash) throw new InvalidDataException("Native evaluation executable changed.");
            // Supply the option before the engine's runtime is initialized, as
            // in the existing embedded probe, rather than relying on another CRT.
            environment["SAL_LOK_OPTIONS"] = "unipoll";
            var conversion = await OfficeEvaluationProcess.RunAsync(native, [program, source, profile, folder], root, environment);
            Save(Path.Combine(folder, "conversion.json"), new { conversion.Output, conversion.Error, conversion.Milliseconds,
                Profile = profile, EnvironmentPaths = environment, conversion.ProcessId,
                JobTotalProcesses = conversion.TotalProcesses, JobActiveProcessesAfterCleanup = conversion.ActiveProcessesAfterCleanup });
            if (!conversion.Output.Contains("{\"sameDocumentExportsComplete\":true}", StringComparison.Ordinal) || conversion.ActiveProcessesAfterCleanup != 0)
                throw new InvalidDataException("Native exports did not complete and clean up.");
            OfficeProfileSettings.Verify(settings, calculation);
            Save(Path.Combine(profile, "settings-verification.json"), new { Verified = true, CalculationMode = calculation,
                Scope = "Retained declarations only; not active-content enforcement or network isolation." });
            var snapshot = Path.Combine(folder, name);
            RequireOutput(snapshot);
            ExcelStoredDateInspection stored;
            using (var input = File.OpenRead(snapshot))
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                stored = await ExcelStoredDateInspection.ReadAsync(input, deadline.Token);
            var before = await InspectPdfAsync(folder, "before", qpdf, pdfium);
            var after = await InspectPdfAsync(folder, "after", qpdf, pdfium);
            var comparison = LegacyPdfComparison.Compare(Path.Combine(folder, "before"), before.Render, before.Text,
                Path.Combine(folder, "after"), after.Render, after.Text);
            var unchanged = before.Text.SequenceEqual(after.Text, StringComparer.Ordinal) && comparison.TextEqual &&
                comparison.Pages.All(page => page.ExactPixels && page.ModernWidthPoints == page.LegacyWidthPoints && page.ModernHeightPoints == page.LegacyHeightPoints);
            var beforeDates = ExcelDateFixtures.Observe(name, before.Text, style == "calc-never");
            var afterDates = ExcelDateFixtures.Observe(name, after.Text, style == "calc-never");
            var expectedEarly = style == "calc-always" && !name.Contains("1904", StringComparison.Ordinal) ? 3 : 0;
            var storedMatches = stored.Complete && stored.EarlyDateCells == expectedEarly && stored.DateFormulaCells == 6;
            if (Hash(source) != sourceHash || File.GetLastWriteTimeUtc(source) != written || Hash(native) != nativeHash)
                throw new InvalidDataException("Source or native evaluation executable changed.");
            results.Add(new { Source = name, SourceSha256 = sourceHash, SourceWriteTimeUtc = written, ProfileStyle = style,
                Profile = profile, Completed = true, SnapshotSha256 = Hash(snapshot), StoredDates = stored, StoredDatesMatch = storedMatches,
                DatePreflight = new { preflight.FormatId, preflight.Refusal, preflight.StoredDates },
                Before = before, After = after, BeforeDates = beforeDates, AfterDates = afterDates,
                Comparison = comparison, PdfUnchanged = unchanged });
            Save(Path.Combine(root, "office-evaluation.json"), new { Mode = "ExcelNativeDateSnapshots", NativeSha256 = nativeHash, Results = results,
                Scope = "Same loaded workbook, before-PDF/copy-save/after-PDF. Passive fixtures only; not corrected date rendering, production validation or arbitrary-document isolation." });
            Console.WriteLine($"OBSERVED: {name} ({style}): PDF unchanged={unchanged}, stored dates match={storedMatches}, display matches={beforeDates.Count(item => item.Matches)}/7.");
            if (!unchanged || !storedMatches) throw new InvalidDataException("Native copy-save correspondence check failed; retained observations are not acceptance.");
        }
        return 0;
    }

    private static async Task<PdfObservation> InspectPdfAsync(string folder, string name, string qpdf, string pdfium)
    {
        var path = Path.Combine(folder, name + ".pdf"); RequireOutput(path);
        var renderedFolder = Path.Combine(folder, name); Directory.CreateDirectory(renderedFolder);
        var check = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", path], folder);
        Save(Path.Combine(renderedFolder, "qpdf-check.json"), check);
        var inspection = await OfficeEvaluationProcess.RunAsync(pdfium, ["inspect", path], folder);
        using var facts = JsonDocument.Parse(inspection.Output);
        if (facts.RootElement.GetProperty("pages").GetInt32() != 1) throw new InvalidDataException("Expected one authored page.");
        Save(Path.Combine(renderedFolder, "inspection.json"), facts.RootElement);
        var render = await OfficeEvaluationProcess.RunAsync(pdfium, ["render", path, Path.Combine(renderedFolder, "page"), "96", "opaque", "no-widgets"], folder);
        using var rendered = JsonDocument.Parse(render.Output);
        var pages = rendered.RootElement.GetProperty("pages").EnumerateArray().ToArray();
        if (pages.Length != 1 || Math.Abs(pages[0].GetProperty("widthPoints").GetDouble() - 612) > 0.1 ||
            Math.Abs(pages[0].GetProperty("heightPoints").GetDouble() - 792) > 0.1)
            throw new InvalidDataException("Authored Letter page geometry changed.");
        var text = PdfTextReader.Read(path);
        Save(Path.Combine(renderedFolder, "render.json"), rendered.RootElement);
        Save(Path.Combine(renderedFolder, "extracted-text.json"), text);
        return new(Hash(path), text, rendered.RootElement.Clone());
    }

    private static void RequireOutput(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > 16 * 1024 * 1024)
            throw new InvalidDataException("Expected a bounded completed output.");
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private sealed record PdfObservation(string Sha256, string[] Text, JsonElement Render);
}
