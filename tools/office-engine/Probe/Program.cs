using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Analysis;

// Runs only the passive fixtures authored here, never arbitrary customer documents.
if (args.Length is < 3 or > 4 || args.Length == 4 && args[3] is not ("Word" or "Excel" or "PowerPoint" or "ProfileMatrix" or "ProfileLengths" or "LegacyAnalysis")) return 2;
var profileMatrix = args.Length == 4 && args[3] is "ProfileMatrix" or "ProfileLengths";
var profileStyles = !profileMatrix ? new[] { "default" } : args[3] == "ProfileLengths"
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
Console.WriteLine("Office evaluation evidence: " + root);
var version = await RunOffice("version", ["--version"]); Console.WriteLine(version.Output.Trim());
if (args.Length == 4 && args[3] == "LegacyAnalysis")
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
    return 0;
}
foreach (var (name, filter, expectedPages) in new[] { ("Word ü.docx", "writer_pdf_Export", 2), ("Excel ü.xlsx", "calc_pdf_Export", 1), ("PowerPoint ü.pptx", "impress_pdf_Export", 2) })
{
    if (args.Length == 4 && !name.StartsWith(profileMatrix ? "PowerPoint" : args[3], StringComparison.Ordinal)) continue;
    foreach (var profileStyle in profileStyles)
    {
        var source = Path.Combine(fixtures, name); var hash = Hash(source);
        var folder = Path.Combine(root, profileMatrix ? profileStyle : Path.GetFileNameWithoutExtension(name)); Directory.CreateDirectory(folder);
        // Keep input and output paths identical across profile variants, then retain each result separately.
        var outputFolder = profileMatrix ? Path.Combine(root, "conversion") : folder; Directory.CreateDirectory(outputFolder);
        var options = new Dictionary<string, object>();
        foreach (var (key, value) in new[] { ("UseLosslessCompression", true), ("ReduceImageResolution", false), ("UseTaggedPDF", true),
            ("ExportBookmarks", true), ("ExportNotes", false), ("ExportNotesPages", false), ("ExportOnlyNotesPages", false),
            ("ExportHiddenSlides", false), ("SinglePageSheets", false), ("ExportFormFields", false), ("IsAddStream", false), ("EncryptFile", false) })
            options[key] = new { type = "boolean", value = value ? "true" : "false" };
        options["SelectPdfVersion"] = new { type = "long", value = "17" };
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
        if (pages != expectedPages) throw new InvalidDataException($"{name}: expected {expectedPages} pages, got {pages}.");
        var render = await Run(pdfium, ["render", pdf, Path.Combine(folder, "page"), "96", "opaque", "no-widgets"], root);
        using var rendered = JsonDocument.Parse(render.Output);
        var text = PdfTextReader.Read(pdf);
        File.WriteAllText(Path.Combine(folder, "extracted-text.json"), JsonSerializer.Serialize(text, new JsonSerializerOptions { WriteIndented = true }));
        var expectedWidth = filter == "impress_pdf_Export" ? 720 : 612;
        var expectedHeight = filter == "impress_pdf_Export" ? 405 : 792;
        if (rendered.RootElement.GetProperty("pages").EnumerateArray().Any(page => Math.Abs(page.GetProperty("widthPoints").GetDouble() - expectedWidth) > 0.1 ||
            Math.Abs(page.GetProperty("heightPoints").GetDouble() - expectedHeight) > 0.1)) throw new InvalidDataException("Authored page geometry changed.");
        var textMatches = filter switch
        {
            "writer_pdf_Export" => text.Length == 2 && text[0].Contains("page one") && text[0].Contains("café ü") && text[0].Contains("Value 42") && text[1].Contains("page two"),
            "calc_pdf_Export" => text.Length == 1 && text[0].Contains("Excel café") && text[0].Contains("Formula result") && text[0].Contains('5') && !text[0].Contains("HIDDEN") && !text[0].Contains("OUTSIDE"),
            _ => text.Length == 2 && text[0].Contains("slide 1 café") && text[1].Contains("slide 3 café") && text.All(page => !page.Contains("HIDDEN"))
        };
        if (!textMatches) throw new InvalidDataException("Expected visible text or hidden/print-area policy did not match: " + name);
        if (Hash(source) != hash) throw new InvalidDataException("Generated original changed.");
        results.Add(new { Source = name, SourceSha256 = hash, ProfileStyle = profileStyle, Completed = true, conversion.Profile,
            PdfSha256 = Hash(pdf), Pages = pages, conversion.Milliseconds,
            ConversionOutput = conversion.Output, Diagnostics = conversion.Error, Text = text, Render = rendered.RootElement.Clone() });
        if (profileMatrix) File.Move(pdf, Path.Combine(folder, Path.GetFileName(pdf)));
        SaveResults();
        Console.WriteLine($"PASS: {name} ({profileStyle}, {conversion.Profile!.Length} profile characters): {pages} independently parsed/rendered pages, expected text/geometry, original unchanged, {conversion.Milliseconds} ms.");
    }
}
return 0;

void SaveResults()
{
    File.WriteAllText(Path.Combine(root, "office-evaluation.json"), JsonSerializer.Serialize(new { Version = version.Output.Trim(), ProfileMatrix = profileMatrix,
        Mode = profileMatrix ? args[3] : "Conversion", Results = results,
        Scope = "Generated passive modern Office fixtures only; bounded text/geometry checks, not broad font/layout fidelity, arbitrary-document isolation or launch acceptance. Profile-matrix observations include failed conversions." }, new JsonSerializerOptions { WriteIndented = true }));
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
    var user = Path.Combine(profile, "user"); Directory.CreateDirectory(user);
    File.AppendAllText(Path.Combine(root, "profiles.txt"), name + "=" + profile + Environment.NewLine);
    File.WriteAllText(Path.Combine(user, "registrymodifications.xcu"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry">
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop><prop oor:name="DisableActiveContent" oor:op="fuse"><value>true</value></prop><prop oor:name="DisablePythonRuntime" oor:op="fuse"><value>true</value></prop><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
        <item oor:path="/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments"><prop oor:name="AutoCheckEnabled" oor:op="fuse"><value>false</value></prop><prop oor:name="AutoDownloadEnabled" oor:op="fuse"><value>false</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Misc"><prop oor:name="UseOpenCL" oor:op="fuse"><value>false</value></prop></item>
        </oor:items>
        """, new UTF8Encoding(false));
    return await Run(Path.Combine(payload, "program", "soffice.com"),
        new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
            "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all" }.Concat(arguments), root, profile);
}

static async Task<RunResult> Run(string executable, IEnumerable<string> arguments, string directory, string? profile = null)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    if (profile is not null)
    {
        foreach (var variable in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
        { var path = Path.Combine(profile, variable); Directory.CreateDirectory(path); start.Environment[variable] = path; }
        start.Environment["SAL_DISABLE_OPENCL"] = "1";
        start.Environment["SAL_LOG"] = "+WARN";
    }
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var timer = Stopwatch.StartNew();
    using var process = Process.Start(start) ?? throw new IOException("Cannot start evaluation child.");
    void Stop() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    using var cancel = deadline.Token.Register(Stop);
    try
    {
        var stdout = Read(process.StandardOutput, deadline.Token); var stderr = Read(process.StandardError, deadline.Token);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
        if (process.ExitCode != 0) throw new IOException($"Evaluation child exited {process.ExitCode}: {await stderr}");
        return new(await stdout, await stderr, timer.ElapsedMilliseconds, profile);
    }
    catch { Stop(); throw; }
}
static async Task<string> Read(StreamReader reader, CancellationToken token)
{
    var text = new StringBuilder(); var buffer = new char[4096];
    while (true)
    {
        var size = await reader.ReadAsync(buffer.AsMemory(), token); if (size == 0) return text.ToString();
        if (text.Length + size > 65536) throw new InvalidDataException("Evaluation diagnostics exceed the budget.");
        text.Append(buffer, 0, size);
    }
}
static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
internal sealed record RunResult(string Output, string Error, long Milliseconds, string? Profile);
