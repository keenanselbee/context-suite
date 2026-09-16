using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Analysis;

// Fixed authored isolation inputs only. The PowerShell owner retains read leases
// through the native experiment so the checked sources cannot be replaced.
internal static class OfficeFixturePreflight
{
    internal static async Task<int> RunAsync(string directory, bool fonts = false)
    {
        var folder = Path.GetFullPath(directory);
        if (!folder.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(folder) != "office-fixtures")
            throw new IOException("Use the owned Office isolation fixture directory.");
        var results = new List<object>();
        var passed = true;
        foreach (var (family, format) in new[] { ("Word", "docx"), ("Excel", "xlsx"), ("PowerPoint", "pptx") })
        foreach (var suffix in fonts ? new[] { " font control.", " font missing." } : new[] { " \u00fc." })
        {
            var path = Path.Combine(folder, family + suffix + format);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync(path, input, deadline.Token);
            var matches = result.FormatId == format && result.Refusal is null;
            passed &= matches;
            input.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token));
            results.Add(new { Name = Path.GetFileName(path), ExpectedFormat = format,
                result.FormatId, result.Refusal, Matches = matches, Sha256 = hash, Bytes = input.Length });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = passed, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
