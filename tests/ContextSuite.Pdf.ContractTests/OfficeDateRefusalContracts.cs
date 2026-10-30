using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using ContextSuite.Private.Office;

internal static class OfficeDateRefusalContracts
{
    internal static async Task RunAsync(string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-date-refusal-", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Use a new repository scratch directory for date refusal evidence.");
        Directory.CreateDirectory(root);
        var checks = new List<string>(); var observations = new List<object>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var (name, refused) in new[] { ("Excel dates 1900-default.xlsx", true),
            ("Excel dates 1900-explicit.xlsx", true), ("Excel dates 1904.xlsx", false) })
        {
            var original = Path.GetFullPath(Path.Combine(fixtures, name));
            var bytes = File.ReadAllBytes(original); var written = File.GetLastWriteTimeUtc(original);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            using var input = new MemoryStream(bytes, false);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync(original, input, deadline.Token);
            Check(preflight.FormatId == "xlsx" && (preflight.Refusal is not null) == refused &&
                preflight.StoredDates?.EarlyDateCells == (refused ? 3 : 0), name + ": retained authored source has expected date admission");
            foreach (var calculation in new[] { "cached", "recalculate" })
            {
                var id = Guid.NewGuid(); var directory = Path.Combine(root, "office-" + id.ToString("N"));
                var work = new OfficeExportWork(id, directory, "ContextSuite.Office." + id.ToString("N"), "xlsx", calculation, bytes.Length, hash);
                foreach (var child in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(directory, child));
                File.WriteAllBytes(work.SourcePath, bytes);
                File.SetAttributes(work.SourcePath, FileAttributes.ReadOnly);
                try
                {
                    try
                    {
                        // No native host is supplied. A refused date must fail in
                        // source preflight; the 1904 control reaches runtime lookup.
                        await OfficeFileAdapter.ExportAsync(Path.Combine(root, "absent-engine"), work, Guid.NewGuid(), deadline.Token);
                        throw new InvalidOperationException("Unexpected Office export.");
                    }
                    catch (InvalidDataException error) when (refused && error.Message == preflight.Refusal)
                    { Check(true, name + "/" + calculation + ": private export returns the date reason before runtime acquisition"); }
                    catch (IOException error) when (!refused && error is FileNotFoundException or DirectoryNotFoundException)
                    { Check(true, name + "/" + calculation + ": unaffected date control reaches missing runtime"); }
                    catch (Win32Exception error) when (!refused && error.NativeErrorCode is 2 or 3)
                    { Check(true, name + "/" + calculation + ": unaffected date control reaches missing runtime directory"); }
                    Check(File.ReadAllBytes(work.SourcePath).SequenceEqual(bytes) &&
                        !Directory.EnumerateFileSystemEntries(Path.Combine(directory, "output")).Any() &&
                        !Directory.EnumerateFileSystemEntries(Path.Combine(directory, "profile")).Any() &&
                        !Directory.EnumerateFileSystemEntries(Path.Combine(directory, "temp")).Any(),
                        name + "/" + calculation + ": no candidate or engine working files created");
                    using var exclusive = new FileStream(work.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    Check(exclusive.Length == bytes.Length, name + "/" + calculation + ": rejected export releases source handles");
                }
                finally { File.SetAttributes(work.SourcePath, FileAttributes.Normal); }
            }
            Check(File.ReadAllBytes(original).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(original) == written,
                name + ": original bytes and timestamp unchanged");
            observations.Add(new { Source = name, SourceSha256 = hash, Refused = refused, preflight.Refusal, preflight.StoredDates });
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Sources = observations,
            Scope = "Private adapter preflight only. No engine, native profile, PDF export or corrected date rendering." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office date refusal checks: {root}");

        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidDataException(description);
            checks.Add(description);
        }
    }
}
