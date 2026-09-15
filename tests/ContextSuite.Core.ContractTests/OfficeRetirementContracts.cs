using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;

internal static class OfficeRetirementContracts
{
    internal static async Task HoldAsync(string stage, bool native)
    {
        RequireOwned(stage);
        using var prepared = await OfficeContextPreparation.CreateAsync(Path.Combine(stage, "contexts"), Path.Combine(stage, "runtime"),
            Path.Combine(stage, "original.docx"), "docx", "none", default);
        var work = prepared.Work;
        if (native)
        {
            await using var owner = OfficeSandboxOwner.Create(prepared.Journal);
            owner.GrantDirectory(Path.Combine(stage, "runtime"), false);
            owner.GrantDirectory(Path.Combine(work.DirectoryPath, "input"), false);
            foreach (var name in new[] { "output", "profile", "temp" }) owner.GrantDirectory(Path.Combine(work.DirectoryPath, name), true);
        }
        else
        {
            // Authored profile lifecycle only; the foundation suite creates no
            // Windows profile. The explicit native mode uses the owner above.
            prepared.Journal.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(work.ProfileName),
                OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName), new string('A', 48)));
            prepared.Journal.Record(new(OfficeOwnershipStep.CleanupIntent));
            prepared.Journal.Record(new(OfficeOwnershipStep.DeleteIntent));
            prepared.Journal.Record(new(OfficeOwnershipStep.ProfileDeleted));
        }
        using var locked = new FileStream(Path.Combine(work.DirectoryPath, "temp", "locked.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try { prepared.Retire(); throw new InvalidOperationException("Expected obstructed retirement."); }
        catch (Win32Exception error) when (error.NativeErrorCode is 32 or 33) { }
        if (prepared.Journal.Changes[^1].Step != OfficeOwnershipStep.RetirementIntent) throw new InvalidDataException("Missing durable retirement intent.");
        var ready = Path.Combine(stage, "ready.json");
        File.WriteAllText(ready + ".tmp", JsonSerializer.Serialize(new { Work = work, Native = native }));
        File.Move(ready + ".tmp", ready);
        await Task.Delay(Timeout.Infinite);
    }

    internal static async Task RunAsync(string scratch, byte[] document, Action<bool, string> check, bool native = false)
    {
        RequireOwned(Path.GetFullPath(scratch));
        var receipts = new List<object>();
        var scenarios = new[] { "complete", "partial", "replaced", "root-replaced", "linked", "record-only" }.AsEnumerable();
        if (native) scenarios = scenarios.Append("profile-present");
        foreach (var scenario in scenarios)
        {
            var stage = Path.GetFullPath(Path.Combine(scratch, "or-" + Guid.NewGuid().ToString("N")));
            var root = Path.Combine(stage, "contexts"); var runtime = Path.Combine(stage, "runtime");
            Directory.CreateDirectory(root); Directory.CreateDirectory(runtime);
            var original = Path.Combine(stage, "original.docx"); File.WriteAllBytes(original, document);
            var written = File.GetLastWriteTimeUtc(original);
            var sentinel = Path.Combine(runtime, "preserve.txt"); File.WriteAllText(sentinel, "preserve runtime");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var value in new[] { "--office-retirement-hold", stage, native ? "native" : "authored" }) start.ArgumentList.Add(value);
            using var child = Process.Start(start) ?? throw new IOException("Could not start retirement fixture.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var readyPath = Path.Combine(stage, "ready.json");
                while (!File.Exists(readyPath))
                {
                    if (child.HasExited) throw new IOException("Retirement fixture exited before readiness: " + await stderr);
                    await Task.Delay(20, deadline.Token);
                }
                using var ready = JsonDocument.Parse(File.ReadAllText(readyPath));
                var work = ready.RootElement.GetProperty("Work").Deserialize<OfficeExportWork>()!; work.Validate();
                check(Path.GetDirectoryName(work.DirectoryPath) == root, "Office restart fixture owns its exact context: " + scenario);
                var record = Path.Combine(root, work.ItemId.ToString("N") + ".ownership");
                var before = File.ReadAllBytes(record);
                var live = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                check(live.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.ReadAllBytes(record).SequenceEqual(before) &&
                    File.Exists(work.SourcePath), "Office restart retirement refuses a live owner: " + scenario);
                child.Kill(); await child.WaitForExitAsync(deadline.Token);
                using (var journal = OfficeOwnershipJournal.Open(record, root, runtime))
                    check(journal.Version == 4 && journal.Owner.ContextDirectories is not null &&
                        journal.Changes[^1].Step == OfficeOwnershipStep.RetirementIntent,
                        "Office restart retains measured directory bindings and retirement intent: " + scenario);
                if (scenario == "partial")
                {
                    foreach (var name in new[] { "output", "profile" })
                    {
                        var path = Path.GetFullPath(Path.Combine(work.DirectoryPath, name));
                        if (Path.GetDirectoryName(path) != work.DirectoryPath) throw new IOException("Unexpected fixture deletion path.");
                        Directory.Delete(path); // Empty owned child: simulate deletion before owner loss.
                    }
                }
                if (scenario is "replaced" or "root-replaced")
                {
                    var target = scenario == "replaced" ? Path.Combine(work.DirectoryPath, "input") : work.DirectoryPath;
                    var held = Path.Combine(stage, "held");
                    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetDirectoryName(held) != stage) throw new IOException("Unexpected fixture move paths.");
                    Directory.Move(target, held); Directory.CreateDirectory(target);
                    try
                    {
                        var refused = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                        check(refused.Entries.Single().State == OfficeRecoveryState.ReviewRequired &&
                            File.ReadAllBytes(record).SequenceEqual(before) && Directory.Exists(held),
                            "Office restart refuses substituted directory identity without deleting evidence: " + scenario);
                    }
                    finally { Directory.Delete(target); Directory.Move(held, target); }
                }
                if (scenario == "linked")
                {
                    var alias = Path.Combine(work.DirectoryPath, "temp", "alias.txt");
                    if (!CreateHardLink(alias, sentinel, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    try
                    {
                        var refused = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                        check(refused.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.Exists(work.SourcePath) &&
                            File.ReadAllBytes(record).SequenceEqual(before) && File.ReadAllText(sentinel) == "preserve runtime",
                            "Office restart refuses a link outside its owned context without deleting evidence");
                    }
                    finally { File.Delete(alias); }
                }
                if (scenario == "profile-present")
                {
                    using var replacement = OfficeSandboxOwner.Create(work.ProfileName);
                    var refused = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                    check(refused.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.Exists(work.SourcePath) &&
                        File.ReadAllBytes(record).SequenceEqual(before) && Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)),
                        "Office restart leaves a newly present native profile and context untouched");
                }
                var recovered = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                check(!recovered.NeedsAttention && recovered.Entries.Single().State == OfficeRecoveryState.AlreadyClean &&
                    !Directory.EnumerateFileSystemEntries(root).Any(), "Office restart completes bounded retirement quietly: " + scenario);
                if (scenario == "record-only")
                {
                    // Replay the exact record with the tree already absent, as if
                    // the app exited between directory and journal deletion.
                    File.WriteAllBytes(record, before);
                    var recordOnly = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                    check(!recordOnly.NeedsAttention && !File.Exists(record), "Office restart completes retirement when only the journal remains");
                }
                var repeat = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
                OfficeSandboxOwner.RequireProfileAbsent(work.ProfileName);
                check(!repeat.NeedsAttention && repeat.Entries.Count == 0 && !Directory.Exists(work.DirectoryPath) &&
                    File.ReadAllBytes(original).SequenceEqual(document) && File.GetLastWriteTimeUtc(original) == written &&
                    File.ReadAllText(sentinel) == "preserve runtime", "Office repeated restart preserves originals/runtime with no profile or context left: " + scenario);
                receipts.Add(new { Scenario = scenario, work.ProfileName, Removed = true, ContextRetired = true, Stage = stage, Native = native });
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync();
                File.WriteAllText(Path.Combine(stage, "stdout.log"), await stdout);
                File.WriteAllText(Path.Combine(stage, "stderr.log"), await stderr);
                // On failure retain files, but attempt recorded native cleanup only
                // after this exact fixture owner has exited.
                await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
            }
        }
        File.WriteAllText(Path.Combine(scratch, "office-retirement-recovery.json"), JsonSerializer.Serialize(new { Passed = true, Profiles = receipts },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RequireOwned(string path)
    {
        if (Path.GetFullPath(path) != path || !path.Contains("\\.codex-temp\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an isolated repository retirement fixture.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string name, string existing, IntPtr security);
}
