using System.Diagnostics;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;

internal static class OfficePreparationCrashContracts
{
    internal static async Task HoldAsync(string stage, string mode)
    {
        RequireOwned(stage);
        using var prepared = await OfficeContextPreparation.CreateAsync(Path.Combine(stage, "contexts"), Path.Combine(stage, "runtime"),
            Path.Combine(stage, "original.docx"), "docx", "none", default, copyProgress: (work, copied) =>
            {
                if (mode == "before" ? copied != 0 : copied == 0) return;
                var ready = Path.Combine(stage, "ready.json");
                File.WriteAllText(ready + ".new", JsonSerializer.Serialize(new { Work = work, Copied = copied }));
                File.Move(ready + ".new", ready);
                using var hold = new ManualResetEvent(false);
                hold.WaitOne(); // The parent kills this exact disposable writer at an actual copy checkpoint.
            });
    }

    internal static async Task RunAsync(string scratch, byte[] original, Action<bool, string> check)
    {
        foreach (var scenario in new[] { "before", "partial", "replaced", "locked" })
        {
            var stage = Path.GetFullPath(Path.Combine(scratch, "crash-" + scenario)); RequireOwned(stage);
            var root = Path.Combine(stage, "contexts"); var runtime = Path.Combine(stage, "runtime");
            Directory.CreateDirectory(root); Directory.CreateDirectory(runtime);
            var source = Path.Combine(stage, "original.docx"); File.WriteAllBytes(source, original);
            var written = File.GetLastWriteTimeUtc(source);
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--office-preparation-hold"); start.ArgumentList.Add(stage); start.ArgumentList.Add(scenario);
            using var child = Process.Start(start) ?? throw new IOException("Could not start disposable copy writer.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try
            {
                var ready = Path.Combine(stage, "ready.json");
                while (!File.Exists(ready))
                {
                    if (child.HasExited) throw new IOException("Copy writer exited before its checkpoint.");
                    await Task.Delay(10, deadline.Token);
                }
                using var evidence = JsonDocument.Parse(File.ReadAllText(ready));
                var work = evidence.RootElement.GetProperty("Work").Deserialize<OfficeExportWork>()!;
                var copied = evidence.RootElement.GetProperty("Copied").GetInt64();
                var record = Path.Combine(root, work.ItemId.ToString("N") + ".ownership");
                var before = ReadRecord(record);
                check(scenario == "before" ? copied == 0 && !File.Exists(work.SourcePath) : copied > 0 && copied < original.Length,
                    "Office crash writer reached the actual source-copy boundary: " + scenario);
                var live = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                check(live.Entries.Single().State == OfficeRecoveryState.ReviewRequired && ReadRecord(record).SequenceEqual(before),
                    "Office preparation recovery refuses the live copy owner: " + scenario);
                child.Kill(); await child.WaitForExitAsync(deadline.Token);
                using (var journal = await OpenAfterExitAsync(record, root, runtime, deadline.Token))
                    check(journal.Version == 4 && journal.Changes.Count == 1 && journal.Owner.ContextDirectories is not null,
                        "Office interrupted copy retains bound directories without claiming profile creation: " + scenario);
                if (scenario == "replaced")
                {
                    var input = Path.Combine(work.DirectoryPath, "input"); var held = Path.Combine(stage, "held");
                    if (!input.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(held) != stage)
                        throw new IOException("Unexpected fixture move paths.");
                    Directory.Move(input, held); Directory.CreateDirectory(input);
                    try
                    {
                        var refusal = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                        check(refusal.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.ReadAllBytes(record).SequenceEqual(before) &&
                            File.Exists(Path.Combine(held, "source.docx")), "Office abandoned preparation refuses substituted directories before recording cleanup");
                    }
                    finally { Directory.Delete(input); Directory.Move(held, input); }
                }
                if (scenario == "locked")
                {
                    using var held = new FileStream(work.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var refusal = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                    check(refusal.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.Exists(work.SourcePath),
                        "Office abandoned preparation keeps a locked partial copy for retry");
                    using var journal = OfficeOwnershipJournal.Open(record, root, runtime);
                    check(journal.Changes[^1].Step == OfficeOwnershipStep.RetirementIntent,
                        "Office interrupted cleanup durably prevents later profile creation");
                }
                var recovered = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                check(recovered.Entries.Single().State == OfficeRecoveryState.Recovered && !Directory.EnumerateFileSystemEntries(root).Any() &&
                    recovered.Notice.Contains("Run Convert again") && !recovered.Notice.Contains("were kept"),
                    "Office restart removes only the recorded unstarted context and reports interrupted work: " + scenario);
                var again = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                check(!again.NeedsAttention && again.Entries.Count == 0 && File.ReadAllBytes(source).SequenceEqual(original) &&
                    File.GetLastWriteTimeUtc(source) == written && !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)),
                    "Office preparation cleanup is repeatable and preserves the original without native profile writes: " + scenario);
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync();
                File.WriteAllText(Path.Combine(stage, "stdout.log"), await stdout);
                File.WriteAllText(Path.Combine(stage, "stderr.log"), await stderr);
                await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
            }
        }
    }
    private static void RequireOwned(string path)
    {
        if (Path.GetFullPath(path) != path || !path.Contains("\\.codex-temp\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an isolated repository preparation fixture.");
    }

    private static async Task<OfficeOwnershipJournal> OpenAfterExitAsync(string path, string root, string runtime, CancellationToken token)
    {
        // Process exit can be signalled before Windows releases every file handle.
        // Match recovery's bounded sharing retry without accepting other failures.
        for (var attempt = 0; ; attempt++)
        {
            try { return OfficeOwnershipJournal.Open(path, root, runtime); }
            catch (System.ComponentModel.Win32Exception error) when (attempt < 49 && error.NativeErrorCode is 32 or 33)
            { await Task.Delay(100, token); }
        }
    }

    internal static byte[] ReadRecord(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (input.Length > 1024 * 1024) throw new IOException("Fixture ownership record exceeds its bound.");
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); return bytes;
    }
}
