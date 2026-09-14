using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Analysis;

// Runs only the passive fixtures authored here, never arbitrary customer documents.
if (args is ["--export-file-release", var releaseRoot])
{
    await OfficeEngineLifetimeContracts.FileReleaseContractsAsync(releaseRoot);
    return 0;
}
if (args is ["--export-fixtures", var fixtureRoot, var fixtureFamily])
{
    if (Directory.Exists(fixtureRoot)) throw new IOException("Use a fresh fixture directory.");
    Directory.CreateDirectory(fixtureRoot); OfficeFixtures.Create(fixtureRoot);
    Console.WriteLine(OfficeExportFixture.Create(fixtureRoot, fixtureFamily));
    return 0;
}
if (args is ["--engine-export-lifetime", var familyPrepared, var familyQpdf, var familyRoot, var exportFamily])
{
    await OfficeEngineLifetimeContracts.ExportAsync(familyPrepared, familyQpdf, familyRoot, exportFamily);
    return 0;
}
if (args is ["--engine-export-lifetime", var exportPrepared, var exportQpdf, var exportRoot])
{
    await OfficeEngineLifetimeContracts.ExportAsync(exportPrepared, exportQpdf, exportRoot);
    return 0;
}
if (args is ["--engine-export-owner", var exportExecutable, var exportProfile, var exportFolder, var exportDocument])
{
    await OfficeEngineLifetimeContracts.ExportOwnerAsync(exportExecutable, exportProfile, exportFolder, exportDocument);
    return 0;
}
if (args is ["--engine-lifetime", var enginePrepared, var engineRoot])
{
    await OfficeEngineLifetimeContracts.RunAsync(enginePrepared, engineRoot);
    return 0;
}
if (args is ["--engine-owner", var engineExecutable, var engineProfile, var engineFolder])
{
    await OfficeEngineLifetimeContracts.OwnerAsync(engineExecutable, engineProfile, engineFolder);
    return 0;
}
if (args is ["--process-contracts", var processRoot])
{
    await OfficeProcessContracts.RunAsync(processRoot);
    return 0;
}
if (args.Length >= 3 && args[0] == "--process-child") return await OfficeProcessContracts.Child(args[1], args[2], args[3..]);
if (args is ["--process-owner", var ownerRoot])
{
    await OfficeEvaluationProcess.RunAsync(Environment.ProcessPath!, ["--process-child", "orphan", ownerRoot], ownerRoot);
    return 0;
}
if (args.Length is < 3 or > 4 || args.Length == 4 && args[3] is not ("Word" or "Excel" or "PowerPoint" or "ProfileMatrix" or "ProfileLengths" or "EnvironmentPaths" or "LegacyAnalysis" or "LegacyPdf" or "ExcelCalculation" or "FontSubstitution" or "ExcelDates" or "ExcelPrint" or "WordRevisions" or "WordFinalText" or "WordRevisionStructures" or "PowerPointSlides" or "EmbeddedImages")) return 2;
var profileMatrix = args.Length == 4 && args[3] is "ProfileMatrix" or "ProfileLengths" or "EnvironmentPaths";
var embeddedImages = args.Length == 4 && args[3] == "EmbeddedImages";
var fontSubstitution = args.Length == 4 && args[3] == "FontSubstitution";
var powerPointSlides = args.Length == 4 && args[3] == "PowerPointSlides";
var wordStructures = args.Length == 4 && args[3] == "WordRevisionStructures";
var wordFinalText = args.Length == 4 && args[3] == "WordFinalText";
var wordRevisions = args.Length == 4 && args[3] is "WordRevisions" or "WordFinalText" or "WordRevisionStructures";
var excelPrint = args.Length == 4 && args[3] == "ExcelPrint";
var excelDates = args.Length == 4 && args[3] == "ExcelDates";
var excelCalculation = args.Length == 4 && args[3] == "ExcelCalculation";
var legacyPdf = args.Length == 4 && args[3] == "LegacyPdf";
var profileStyles = wordFinalText || wordStructures ? new[] { "final-text", "show-changes-control" } : excelCalculation ? new[] { "calc-default", "calc-always", "calc-never" } : legacyPdf ? new[] { "modern", "legacy" } : !profileMatrix ? new[] { "default" } : args[3] == "EnvironmentPaths"
    ? new[] { "env-control", "env-all", "env-profile", "env-TEMP", "env-TMP", "env-APPDATA", "env-LOCALAPPDATA" } : args[3] == "ProfileLengths"
    ? new[] { "length-90", "length-110", "length-130", "length-150", "length-170" }
    : new[] { "short-ascii", "short-unicode", "long-ascii", "long-unicode" };
var prepared = Path.GetFullPath(args[0]); var qpdf = Path.GetFullPath(args[1]); var pdfium = Path.GetFullPath(args[2]);
if (!prepared.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar + "office-engine" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Use prepared office-engine scratch.");
var payload = Path.Combine(prepared, "unpacked");
PdfTextReader.Initialize(Path.Combine(Path.GetDirectoryName(pdfium)!, "pdfium.dll"));
var root = Path.Combine(prepared, "evaluation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var fixtures = Path.Combine(root, "fixtures"); Directory.CreateDirectory(fixtures); OfficeFixtures.Create(fixtures);
var results = new List<object>();
var structuresMatch = true;
Console.WriteLine("Office evaluation evidence: " + root);
OfficeProfileContracts.Run(Path.Combine(root, "profile-contracts"));
var version = await RunOffice("version", ["--version"]); Console.WriteLine(version.Output.Trim());
if (args.Length == 4 && args[3] is "LegacyAnalysis" or "LegacyPdf")
{
    foreach (var (name, extension, filter) in new[] { ("Word ü.docx", "doc", "MS Word 97"),
        ("Excel ü.xlsx", "xls", "MS Excel 97"), ("PowerPoint ü.pptx", "ppt", "MS PowerPoint 97") })
    {
        var source = Path.Combine(fixtures, name); var before = Hash(source);
        var folder = Path.Combine(root, "legacy-" + extension); Directory.CreateDirectory(folder);
        var conversion = await RunOffice("legacy-" + extension, ["--convert-to", extension + ":" + filter, "--outdir", folder, source]);
        File.WriteAllText(Path.Combine(folder, "conversion.json"), JsonSerializer.Serialize(conversion, new JsonSerializerOptions { WriteIndented = true }));
        var legacy = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + "." + extension);
        if (!File.Exists(legacy) || new FileInfo(legacy).Length is <= 0 or > 16 * 1024 * 1024 || Hash(source) != before)
            throw new InvalidDataException("Expected generated legacy copy with unchanged authored original.");
        var legacyHash = Hash(legacy);
        using var input = new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[(int)Math.Min(input.Length, HeaderAnalyzer.MaximumBytes)]; await input.ReadExactlyAsync(bytes);
        var header = HeaderAnalyzer.Analyze(legacy, bytes, input.Length);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var analysis = await LegacyDocumentAnalysis.AddCompoundAsync(header, input, deadline.Token);
        File.WriteAllText(Path.Combine(folder, "analysis.json"), JsonSerializer.Serialize(new
        {
            analysis.Path, analysis.FileBytes, analysis.Identity, analysis.Facts, analysis.Warnings, analysis.InspectedBytes,
            FilenameHints = analysis.FilenameHints.Select(hint => new { hint.Id, hint.Name })
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (analysis.Identity.FormatId != extension || analysis.Identity.Basis != IdentificationBasis.Content ||
            analysis.Identity.Confidence != IdentificationConfidence.Likely || Hash(legacy) != legacyHash)
            throw new InvalidDataException("Generated legacy Office analysis failed: " + extension);
        results.Add(new { Source = name, SourceSha256 = before, LegacySha256 = legacyHash, analysis.FileBytes, analysis.InspectedBytes,
            Format = extension, conversion.Milliseconds });
        File.WriteAllText(Path.Combine(root, "legacy-analysis.json"), JsonSerializer.Serialize(new { Version = version.Output.Trim(), Results = results,
            Scope = "LibreOffice-produced copies of authored passive fixtures; legacy parser interoperability, not Microsoft Office layout fidelity or customer legacy export." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: generated {extension}: likely content identity, {analysis.FileBytes} bytes, bounded analysis and unchanged originals/copy, {conversion.Milliseconds} ms.");
    }
    if (!legacyPdf) return 0;
    results.Clear(); // Preserve generation evidence in legacy-analysis.json; keep PDF results separate.
}
var baselines = new Dictionary<string, (string Folder, JsonElement Render, string[] Text)>();
var comparisons = new List<object>();
var conversionCases = wordStructures ? WordRevisionStructureFixtures.Create(fixtures) : embeddedImages ? OfficeImageFixtures.Create(fixtures) : powerPointSlides ? PowerPointSlideFixtures.Create(fixtures) : wordRevisions ? WordRevisionFixtures.Create(fixtures) : excelPrint ? ExcelPrintFixtures.Create(fixtures) : excelDates ? ExcelDateFixtures.Create(fixtures) : fontSubstitution ? OfficeFontFixtures.Create(fixtures) : excelCalculation ? ExcelCalculationFixtures.Create(fixtures) : new[] { ("Word ü.docx", "writer_pdf_Export", 2), ("Excel ü.xlsx", "calc_pdf_Export", 1), ("PowerPoint ü.pptx", "impress_pdf_Export", 2) };
foreach (var (name, filter, expectedPages) in conversionCases)
{
    if (args.Length == 4 && !legacyPdf && !excelCalculation && !excelDates && !excelPrint && !wordRevisions && !powerPointSlides && !fontSubstitution && !embeddedImages && !name.StartsWith(profileMatrix ? "PowerPoint" : args[3], StringComparison.Ordinal)) continue;
    foreach (var profileStyle in profileStyles)
    {
        if (wordStructures && profileStyle == "show-changes-control" && !name.EndsWith(" tracked.docx", StringComparison.Ordinal)) continue;
        var extension = filter switch { "writer_pdf_Export" => "doc", "calc_pdf_Export" => "xls", _ => "ppt" };
        var source = legacyPdf && profileStyle == "legacy"
            ? Path.Combine(root, "legacy-" + extension, Path.ChangeExtension(name, extension)) : Path.Combine(fixtures, name);
        var hash = Hash(source);
        var sourceWriteTime = File.GetLastWriteTimeUtc(source);
        var folder = Path.Combine(root, profileMatrix ? profileStyle : Path.GetFileNameWithoutExtension(name) + (legacyPdf || excelCalculation || wordFinalText || wordStructures ? "-" + profileStyle : "")); Directory.CreateDirectory(folder);
        // Keep input and output paths identical across profile variants, then retain each result separately.
        var outputFolder = profileMatrix ? Path.Combine(root, "conversion") : folder; Directory.CreateDirectory(outputFolder);
        var options = new Dictionary<string, object>();
        foreach (var (key, value) in new[] { ("UseLosslessCompression", true), ("ReduceImageResolution", false), ("UseTaggedPDF", true),
            ("ExportBookmarks", true), ("ExportNotes", false), ("ExportNotesPages", false), ("ExportOnlyNotesPages", false),
            ("ExportHiddenSlides", false), ("SinglePageSheets", false), ("ExportFormFields", false), ("IsAddStream", false), ("EncryptFile", false) })
            options[key] = new { type = "boolean", value = value ? "true" : "false" };
        var notesControl = powerPointSlides && name == "PowerPoint slides notes-control.pptx";
        if (notesControl)
        {
            options["ExportNotesPages"] = new { type = "boolean", value = "true" };
            options["ExportOnlyNotesPages"] = new { type = "boolean", value = "true" };
        }
        if (embeddedImages && name.Contains("reduced", StringComparison.Ordinal))
        {
            options["ReduceImageResolution"] = new { type = "boolean", value = "true" };
            options["MaxImageResolution"] = new { type = "long", value = "150" };
        }
        options["SelectPdfVersion"] = new { type = "long", value = "17" };
        // Owner-selected Word default. Preserve the original observation mode;
        // the explicit true variant is only a positive control for this option.
        if (filter == "writer_pdf_Export" && (!wordRevisions || wordFinalText || wordStructures))
            options["ExportTrackedChanges"] = new { type = "boolean", value = profileStyle == "show-changes-control" ? "true" : "false" };
        var conversion = await RunOffice(Path.GetFileNameWithoutExtension(name), ["--convert-to", "pdf:" + filter + ":" + JsonSerializer.Serialize(options), "--outdir", outputFolder, source], profileStyle);
        File.WriteAllText(Path.Combine(folder, "conversion.json"), JsonSerializer.Serialize(conversion, new JsonSerializerOptions { WriteIndented = true }));
        var pdf = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(name) + ".pdf");
        if (Hash(source) != hash) throw new InvalidDataException("Generated original changed.");
        if (!File.Exists(pdf) && profileMatrix)
        {
            results.Add(new { Source = name, SourceSha256 = hash, ProfileStyle = profileStyle, Completed = false, Reason = "Exit zero but no PDF", conversion });
            SaveResults();
            Console.WriteLine($"OBSERVED: {profileStyle}, {conversion.Profile!.Length} profile characters: exit zero without PDF.");
            continue;
        }
        if (!File.Exists(pdf) || new FileInfo(pdf).Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("Expected one bounded PDF: " + name);
        await Run(qpdf, ["--check", pdf], root);
        var inspect = await Run(pdfium, ["inspect", pdf], root);
        using var facts = JsonDocument.Parse(inspect.Output);
        var pages = facts.RootElement.GetProperty("pages").GetInt32();
        if (pages is < 1 or > 16) throw new InvalidDataException("Evaluation page budget exceeded.");
        if (!excelPrint && pages != expectedPages) throw new InvalidDataException($"{name}: expected {expectedPages} pages, got {pages}.");
        var render = await Run(pdfium, ["render", pdf, Path.Combine(folder, "page"), "96", "opaque", "no-widgets"], root);
        using var rendered = JsonDocument.Parse(render.Output);
        var text = PdfTextReader.Read(pdf);
        File.WriteAllText(Path.Combine(folder, "extracted-text.json"), JsonSerializer.Serialize(text, new JsonSerializerOptions { WriteIndented = true }));
        var expectedWidth = notesControl ? 540 : filter == "impress_pdf_Export" ? 720 : 612;
        var expectedHeight = notesControl ? 720 : filter == "impress_pdf_Export" ? 405 : 792;
        if (rendered.RootElement.GetProperty("pages").EnumerateArray().Any(page => Math.Abs(page.GetProperty("widthPoints").GetDouble() - expectedWidth) > 0.1 ||
            Math.Abs(page.GetProperty("heightPoints").GetDouble() - expectedHeight) > 0.1)) throw new InvalidDataException("Authored page geometry changed.");
        var textMatches = filter switch
        {
            "writer_pdf_Export" => text.Length == 2 && text[0].Contains("page one") && text[0].Contains("café ü") && text[0].Contains("Value 42") && text[1].Contains("page two"),
            "calc_pdf_Export" => text.Length == 1 && text[0].Contains("Excel café") && text[0].Contains("Formula result") && text[0].Contains('5') && !text[0].Contains("HIDDEN") && !text[0].Contains("OUTSIDE"),
            _ => text.Length == 2 && text[0].Contains("slide 1 café") && text[1].Contains("slide 3 café") && text.All(page => !page.Contains("HIDDEN"))
        };
        string[]? pdfFonts = null;
        if (fontSubstitution)
        {
            var fontJson = await Run(qpdf, ["--json", "--json-key=qpdf", pdf], root);
            File.WriteAllText(Path.Combine(folder, "pdf-font-objects.json"), fontJson.Output);
            pdfFonts = OfficeFontFixtures.Observe(fontJson.Output);
            Console.WriteLine("OBSERVED: " + name + " PDF fonts: " + string.Join(", ", pdfFonts));
        }
        if (embeddedImages)
        {
            await Run(qpdf, ["--json-output=2", "--decode-level=all", pdf, Path.Combine(folder, "pdf-image-objects.json")], root);
            if (new FileInfo(Path.Combine(folder, "pdf-image-objects.json")).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("Image inspection JSON exceeds the passive fixture budget.");
        }
        var calculationObservation = excelCalculation ? ExcelCalculationFixtures.Observe(name, text) : null;
        var slideObservation = powerPointSlides ? PowerPointSlideFixtures.Observe(name, text) : null;
        var revisionObservation = wordRevisions && !wordStructures ? WordRevisionFixtures.Observe(name, text) : null;
        if (wordFinalText && (revisionObservation is null || !revisionObservation.InsertedTextPresent ||
            revisionObservation.DeletedTextPresent != (profileStyle == "show-changes-control" && revisionObservation.Case != "clean")))
            throw new InvalidDataException("Explicit Word revision export policy did not match authored final/control text.");
        var structureObservation = wordStructures ? WordRevisionStructureFixtures.Observe(name, profileStyle, text) : null;
        if (structureObservation is not null) structuresMatch &= structureObservation.Matches;
        var printObservation = excelPrint ? ExcelPrintFixtures.Observe(name, text) : null;
        var dateObservations = excelDates ? ExcelDateFixtures.Observe(name, text) : null;
        if (!excelCalculation && !excelDates && !excelPrint && !wordRevisions && !powerPointSlides && !textMatches) throw new InvalidDataException("Expected visible text or hidden/print-area policy did not match: " + name);
        if (!wordRevisions && filter == "writer_pdf_Export" &&
            (!text[0].Contains("FIRST PAGE HEADER") || text[0].Contains("RUNNING HEADER") ||
             !text[1].Contains("RUNNING HEADER") || text[1].Contains("FIRST PAGE HEADER") ||
             !text[0].Contains("Page 1 of 2") || !text[1].Contains("Page 2 of 2") || text.Any(page => page.Contains("99"))))
            throw new InvalidDataException("Word first/default headers or PAGE/NUMPAGES field rendering did not match authored expectations.");
        if (Hash(source) != hash) throw new InvalidDataException("Generated original changed.");
        if ((wordFinalText || wordStructures) && File.GetLastWriteTimeUtc(source) != sourceWriteTime)
            throw new InvalidDataException("Word revision source write time changed.");
        results.Add(new { Source = Path.GetFileName(source), SourceSha256 = hash, ProfileStyle = profileStyle, Completed = true, conversion.Profile, conversion.EnvironmentPaths,
            PdfSha256 = Hash(pdf), Pages = pages, conversion.Milliseconds, SourceWriteTimeUtc = wordFinalText || wordStructures ? (DateTime?)sourceWriteTime : null, CalculationObservation = calculationObservation, DateObservations = dateObservations, PrintObservation = printObservation, RevisionObservation = revisionObservation, StructureObservation = structureObservation, SlideObservation = slideObservation, ExportOptions = powerPointSlides || embeddedImages || filter == "writer_pdf_Export" ? options : null, PdfFontNames = pdfFonts,
            RequestedFont = fontSubstitution ? (name.Contains("missing", StringComparison.Ordinal) ? OfficeFontFixtures.MissingFont : "Arial") : null,
            ConversionOutput = conversion.Output, Diagnostics = conversion.Error, Text = text, Render = rendered.RootElement.Clone() });
        if (structureObservation is not null) Console.WriteLine("OBSERVED: " + name + ": structural revision text matches=" + structureObservation.Matches);
        if (slideObservation is not null) Console.WriteLine("OBSERVED: " + name + ": slide/notes policy matches=" + slideObservation.Matches);
        if (revisionObservation is not null) Console.WriteLine("OBSERVED: " + name + ": inserted=" + revisionObservation.InsertedTextPresent + ", deleted=" + revisionObservation.DeletedTextPresent);
        if (printObservation is not null) Console.WriteLine("OBSERVED: " + name + ": saved print policy matches=" + printObservation.Matches);
        if (dateObservations is not null) Console.WriteLine("OBSERVED: " + name + ": " + dateObservations.Count(item => item.Matches) + "/7 dates/durations match Excel display expectations.");
        if (calculationObservation is not null) Console.WriteLine("OBSERVED: " + name + ": " + calculationObservation);
        if (profileMatrix) File.Move(pdf, Path.Combine(folder, Path.GetFileName(pdf)));
        SaveResults();
        var textScope = wordStructures ? "structural revision observations retained (inspect text/pixel controls)" : wordFinalText ? "explicit final-text/control policy matches" : powerPointSlides ? "slide/notes observations retained (not a fidelity pass)" : wordRevisions ? "revision observations retained (not a fidelity pass)" : excelPrint ? "print observations retained (not a fidelity pass)" : excelDates ? "date observations retained (not a fidelity pass)" : "expected text";
        Console.WriteLine($"PASS: {Path.GetFileName(source)} ({profileStyle}, {conversion.Profile!.Length} profile characters): {pages} independently parsed/rendered pages, {textScope}, expected geometry, original unchanged, {conversion.Milliseconds} ms.");
        if (legacyPdf)
        {
            if (profileStyle == "modern") baselines.Add(name, (folder, rendered.RootElement.Clone(), text));
            else
            {
                var baseline = baselines[name];
                var comparison = LegacyPdfComparison.Compare(baseline.Folder, baseline.Render, baseline.Text, folder, rendered.RootElement, text);
                comparisons.Add(new { Family = extension, Comparison = comparison });
                File.WriteAllText(Path.Combine(root, "legacy-pdf-comparison.json"), JsonSerializer.Serialize(new
                {
                    Version = version.Output.Trim(), Results = comparisons,
                    Scope = "Same-engine modern versus generated legacy roundtrip at 96 DPI. Differences are observations, not accepted fidelity tolerances or independent Microsoft Office baselines."
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"OBSERVED: {extension} legacy roundtrip: normalized text equal={comparison.TextEqual}, exact rendered pages={comparison.Pages.Count(page => page.ExactPixels)}/{comparison.Pages.Length}.");
            }
        }
    }
}
if (!structuresMatch) throw new InvalidDataException("Structural revision text did not match; all completed observations are retained.");
return 0;

void SaveResults()
{
    File.WriteAllText(Path.Combine(root, "office-evaluation.json"), JsonSerializer.Serialize(new { Version = version.Output.Trim(), ProfileMatrix = profileMatrix,
        Mode = profileMatrix || legacyPdf || excelCalculation || excelDates || fontSubstitution || excelPrint || wordRevisions || powerPointSlides || embeddedImages ? args[3] : "Conversion", Results = results,
        Scope = "Authored passive Office fixtures and generated legacy copies only; bounded text/geometry checks, not broad font/layout fidelity, arbitrary-document isolation or launch acceptance. Profile-matrix observations include failed conversions." }, new JsonSerializerOptions { WriteIndented = true }));
}

async Task<RunResult> RunOffice(string name, string[] arguments, string profileStyle = "default")
{
    // Keep the disposable profile short while inputs/outputs still exercise Unicode paths.
    var suffix = profileStyle.EndsWith("-unicode", StringComparison.Ordinal) ? "ü" : "u";
    var profile = profileStyle.StartsWith("long-", StringComparison.Ordinal)
        ? Path.Combine(root, "profiles", "PowerPoint " + suffix)
        : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(prepared)!)!, "office-profile-" + Guid.NewGuid().ToString("N") + suffix);
    if (profileStyle.StartsWith("length-", StringComparison.Ordinal))
    {
        var length = int.Parse(profileStyle[7..], System.Globalization.CultureInfo.InvariantCulture);
        if (length < profile.Length || length > 170) throw new InvalidDataException("Profile length experiment cannot fit this repository location.");
        profile = profile.PadRight(length, 'p'); // Same ASCII parent and depth; change only leaf length.
    }
    var environment = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var variable in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
        environment[variable] = Path.Combine(profile, variable);
    if (profileStyle.StartsWith("env-", StringComparison.Ordinal))
    {
        if (profile.Length > 90) throw new InvalidDataException("Environment path experiment cannot fit this repository location.");
        var shortRoot = profile.PadRight(90, 'p');
        var longRoot = shortRoot.PadRight(170, 'p');
        profile = profileStyle is "env-all" or "env-profile" ? longRoot : shortRoot;
        foreach (var variable in environment.Keys.ToArray())
            environment[variable] = Path.Combine(profileStyle == "env-all" || profileStyle == "env-" + variable ? longRoot : shortRoot, variable);
    }
    var user = Path.Combine(profile, "user"); Directory.CreateDirectory(user);
    File.AppendAllText(Path.Combine(root, "profiles.txt"), name + "=" + profile + Environment.NewLine);
    var settingsPath = Path.Combine(user, "registrymodifications.xcu");
    OfficeProfileSettings.Apply(settingsPath);
    var officeArguments = new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
        "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all" };
    var initializeProfile = !profileMatrix && arguments is not ["--version"];
    if (initializeProfile)
    {
        var initialization = await Run(Path.Combine(payload, "program", "soffice.com"),
            officeArguments.Concat(new[] { "--terminate_after_init" }), root, profile, environment);
        File.WriteAllText(Path.Combine(profile, "initialization.json"), JsonSerializer.Serialize(initialization));
    }
    int? calculationMode = profileStyle == "calc-always" ? 0 : profileStyle == "calc-never" ? 1 : null;
    OfficeProfileSettings.Apply(settingsPath, calculationMode);
    File.Copy(settingsPath, Path.Combine(profile, "requested-settings.xcu"), false);
    var result = await Run(Path.Combine(payload, "program", "soffice.com"),
        officeArguments.Concat(arguments), root, profile, environment);
    if (initializeProfile)
    {
        OfficeProfileSettings.Verify(settingsPath, calculationMode);
        File.WriteAllText(Path.Combine(profile, "settings-verification.json"), JsonSerializer.Serialize(new
        {
            Verified = true, CalculationMode = calculationMode,
            Scope = "Requested declarations retained after engine exit; not observed enforcement or network isolation."
        }));
    }
    return result;
}

static async Task<RunResult> Run(string executable, IEnumerable<string> arguments, string directory, string? profile = null,
    IReadOnlyDictionary<string, string>? environment = null)
{
    var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (profile is not null)
    {
        foreach (var (variable, path) in environment ?? throw new InvalidDataException("An Office profile requires explicit local environment paths."))
        { Directory.CreateDirectory(path); overrides[variable] = path; }
        overrides["SAL_DISABLE_OPENCL"] = "1";
        overrides["SAL_LOG"] = "+WARN";
    }
    var result = await OfficeEvaluationProcess.RunAsync(executable, arguments, directory, overrides);
    return new(result.Output, result.Error, result.Milliseconds, profile, environment, result.ProcessId, result.TotalProcesses, result.ActiveProcessesAfterCleanup);
}
static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
internal sealed record RunResult(string Output, string Error, long Milliseconds, string? Profile, IReadOnlyDictionary<string, string>? EnvironmentPaths,
    int ProcessId, uint JobTotalProcesses, uint JobActiveProcessesAfterCleanup);
