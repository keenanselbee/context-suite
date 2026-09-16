using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Analysis;

internal static class ExcelStoredDateEvaluation
{
    internal static async Task<int> RunAsync(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!directory.Contains("\\.codex-temp\\office-engine\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored Excel date evidence.");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "independent-dates.json")));
        var expected = new Dictionary<string, int> { ["Excel dates 1900-default.xlsx"] = 3,
            ["Excel dates 1900-explicit.xlsx"] = 3, ["Excel dates 1904.xlsx"] = 0 };
        var results = new List<object>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var entry in report.RootElement.GetProperty("Results").EnumerateArray())
        {
            var name = entry.GetProperty("Source").GetString()!;
            if (!expected.Remove(name, out var count)) throw new InvalidDataException("Unexpected date fixture.");
            var path = Path.Combine(directory, "fixtures", name);
            var written = File.GetLastWriteTimeUtc(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token));
            if (hash != entry.GetProperty("SourceSha256").GetString()) throw new InvalidDataException("Changed date fixture.");
            input.Position = 0;
            var observed = await ExcelStoredDateInspection.ReadAsync(input, deadline.Token);
            input.Position = 0;
            if (hash != Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token)) || File.GetLastWriteTimeUtc(path) != written)
                throw new InvalidDataException("Date source changed during inspection.");
            var passed = observed.Complete && observed.EarlyDateCells == count && observed.DateFormulaCells == 0;
            results.Add(new { Source = name, SourceSha256 = hash, ExpectedEarlyDateCells = count, Passed = passed, Observation = observed });
            if (!passed) throw new InvalidDataException("Stored date observation disagrees with authored fixture: " + JsonSerializer.Serialize(observed));
        }
        if (expected.Count != 0) throw new InvalidDataException("Incomplete date fixture set.");
        var output = Path.Combine(directory, "stored-date-inspection-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, Results = results,
            Scope = "Stored numeric values and supported direct number formats only; no rendering, calculation, source rewriting or launch-policy acceptance." },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Three retained Excel fixtures match stored-date inspection: " + output);
        return 0;
    }
}
