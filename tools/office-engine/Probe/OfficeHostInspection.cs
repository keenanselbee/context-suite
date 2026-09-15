using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Office;

internal static class OfficeHostInspection
{
    internal static async Task<int> RunAsync(string stagingDirectory, string caseName)
    {
        var stage = Path.GetFullPath(stagingDirectory);
        if (!stage.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            !caseName.StartsWith("cs", StringComparison.Ordinal) || caseName.Length is < 3 or > 12 || !caseName[2..].All(char.IsAsciiDigit))
            throw new IOException("Use owned Office host evaluation evidence.");
        var folder = Path.Combine(stage, caseName);
        var results = new List<object>();
        var index = 0;
        foreach (var (family, format, profile) in new[] { ("Word", "docx", "wi"), ("Excel", "xlsx", "xi"), ("PowerPoint", "pptx", "pi") })
        {
            index++;
            var name = family + "-isolated";
            var source = Path.Combine(folder, "allowed", name, family + " ü." + format);
            var output = Path.Combine(folder, "writable", name, family + " ü.pdf");
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(input));
            using var candidate = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
            var outputHash = Convert.ToHexString(await SHA256.HashDataAsync(candidate));
            var replyPath = Path.Combine(folder, name + ".log");
            if (new FileInfo(replyPath).Length > OfficeHostProtocol.MaximumReplyBytes) throw new InvalidDataException("Host reply exceeds its bound.");
            using var process = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, name + "-export.json")));
            var completed = OfficeHostProtocol.ReadCompletion(await File.ReadAllBytesAsync(replyPath), process.RootElement.GetProperty("exitCode").GetInt32(),
                Guid.ParseExact(index.ToString("x32"), "N"), format, format == "xlsx" ? "cached" : "none", input.Length, sourceHash);
            if (completed.OutputBytes != candidate.Length || completed.OutputSha256 != outputHash)
                throw new InvalidDataException("Host candidate does not match its completion reply.");
            OfficeProfileSettings.Verify(Path.Combine(folder, "writable", profile, "user", "registrymodifications.xcu"), format == "xlsx" ? 1 : null);
            results.Add(new { Family = family, Completion = completed, ProfileSettingsMatch = true });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, Results = results,
            Scope = "Host completion binding and actual profile settings; independent PDF validation remains required." }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
