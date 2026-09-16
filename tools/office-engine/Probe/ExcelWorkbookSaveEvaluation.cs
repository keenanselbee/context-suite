using System.Security.Cryptography;
using System.Text.Json;

// Authored passive workbook diagnostics. Jobs bound resources and lifetime;
// this ordinary-process comparison does not provide AppContainer isolation.
internal static class ExcelWorkbookSaveEvaluation
{
    internal static async Task<int> RunAsync(string prepared, string fixtures, string native, bool workbookFirst = false, int processMemoryMiB = 512)
    {
        prepared = Path.GetFullPath(prepared); fixtures = Path.GetFullPath(fixtures); native = Path.GetFullPath(native);
        if (!prepared.Contains("\\.codex-temp\\office-engine\\", StringComparison.OrdinalIgnoreCase) ||
            !fixtures.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            !native.Contains("\\.codex-temp\\office-date-native\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use prepared runtimes and authored repository fixtures.");
        var source = Path.Combine(fixtures, "Excel \u00fc.xlsx");
        var hash = Hash(source); var written = File.GetLastWriteTimeUtc(source);
        var root = Path.Combine(prepared, "workbook-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var profile = Path.Combine(root, "profile"); var user = Path.Combine(profile, "user"); Directory.CreateDirectory(user);
        var output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
        var settings = Path.Combine(user, "registrymodifications.xcu");
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
        { var path = Path.Combine(profile, name); Directory.CreateDirectory(path); environment[name] = path; }
        environment["SAL_DISABLE_OPENCL"] = "1"; environment["SAL_LOG"] = "+WARN";
        var program = Path.Combine(prepared, "unpacked", "program");
        OfficeProfileSettings.Apply(settings);
        Console.WriteLine("Workbook save evaluation: " + root);
        var initialization = await OfficeEvaluationProcess.RunAsync(Path.Combine(program, "soffice.com"),
            ["-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
                "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all", "--terminate_after_init"], root, environment);
        File.WriteAllText(Path.Combine(root, "initialization.json"), JsonSerializer.Serialize(initialization));
        OfficeProfileSettings.Apply(settings, 1);
        environment["SAL_LOK_OPTIONS"] = "unipoll";
        string? failure = null;
        OfficeEvaluationProcess.Result? conversion = null;
        try { conversion = await OfficeEvaluationProcess.RunAsync(native, [program, source, profile, output, workbookFirst ? "--workbook-first" : "--workbook-save"], root, environment, processMemoryMiB: processMemoryMiB); }
        catch (Exception error) { failure = error.ToString(); }
        if (Hash(source) != hash || File.GetLastWriteTimeUtc(source) != written) throw new IOException("Original fixture changed.");
        OfficeProfileSettings.Verify(settings, 1);
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Completed = true, ExportSucceeded = conversion is not null, WorkbookFirst = workbookFirst, ProcessMemoryMiB = processMemoryMiB,
            Source = source, SourceSha256 = hash, NativeSha256 = Hash(native), Failure = failure, Conversion = conversion,
            Scope = "Ordinary-process library diagnostic with bounded job; no filesystem/network isolation acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(failure ?? "Workbook save control completed.");
        return 0;
    }

    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
}
