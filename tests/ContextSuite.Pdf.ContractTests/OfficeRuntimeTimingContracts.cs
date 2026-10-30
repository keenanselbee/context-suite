using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Private.IO;
using ContextSuite.Private.Office;

internal static class OfficeRuntimeTimingContracts
{
    internal static async Task RunAsync(string package, string evidence)
    {
        package = Path.GetFullPath(package);
        evidence = Path.GetFullPath(evidence);
        if (!package.Contains("\\.codex-temp\\office-execution\\", StringComparison.OrdinalIgnoreCase) ||
            !evidence.Contains("\\.codex-temp\\office-runtime\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(evidence))
            throw new ArgumentException("Use a retained scratch worker and a fresh runtime evidence directory.");
        Directory.CreateDirectory(evidence);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = deadline.Token;
        var started = Stopwatch.GetTimestamp();
        using var runtime = await OfficeRuntimeLease.OpenAsync(Path.Combine(package, "ContextSuite.OfficeHost.exe"),
            Path.Combine(package, "runtime"), Path.Combine(package, "runtime-files.txt"), token);
        var acquisition = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var rows = File.ReadAllLines(Path.Combine(package, "runtime-files.txt"));
        double openSeconds = 0, asyncSeconds = 0, blockSeconds = 0;
        long bytes = 0;
        var buffer = new byte[65536];
        for (var index = 0; index < rows.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var row = rows[index].Split('\t');
            var path = Path.Combine(runtime.DirectoryPath, row[0].Replace('/', '\\'));
            started = Stopwatch.GetTimestamp();
            using var input = MediaFiles.OpenRead(path);
            openSeconds += Stopwatch.GetElapsedTime(started).TotalSeconds;
            bytes += input.Length;
            // Alternate order to limit bias. Both paths read a fully verified,
            // already cached file while the original runtime lease stays held.
            if (index % 2 == 0) { await AsyncHash(); BlockHash(); }
            else { BlockHash(); await AsyncHash(); }

            async Task AsyncHash()
            {
                input.Position = 0;
                var start = Stopwatch.GetTimestamp();
                var hash = await SHA256.HashDataAsync(input, token);
                asyncSeconds += Stopwatch.GetElapsedTime(start).TotalSeconds;
                if (!Convert.ToHexString(hash).Equals(row[2], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Async hash differs from the pinned inventory.");
            }
            void BlockHash()
            {
                input.Position = 0;
                var start = Stopwatch.GetTimestamp();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, count);
                }
                token.ThrowIfCancellationRequested();
                var result = hash.GetHashAndReset();
                blockSeconds += Stopwatch.GetElapsedTime(start).TotalSeconds;
                if (!Convert.ToHexString(result).Equals(row[2], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Block hash differs from the pinned inventory.");
            }
        }
        runtime.VerifyMembership(token);
        var report = new
        {
            Passed = rows.Length == OfficeRuntimeLease.FileCount && bytes == OfficeRuntimeLease.TotalBytes,
            Files = rows.Length, Bytes = bytes, AcquisitionSeconds = acquisition,
            ReopenSeconds = openSeconds, AsyncHashSeconds = asyncSeconds, BlockHashSeconds = blockSeconds,
            Order = "Alternating per file; both algorithms use the already verified runtime cache.",
            Scope = "Read-only runtime timing; no Office execution, profile creation or installed-state changes."
        };
        if (!report.Passed) throw new IOException("Incomplete runtime timing matrix.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
