using System.Runtime.InteropServices;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;

namespace ContextSuite.Core.ContractTests;

internal static class OfficeRecoveryCoordinatorContracts
{
    internal static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-recovery-scan-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(stage);
        var missing = Path.Combine(stage, "absent");
        var runtime = Path.Combine(stage, "runtime"); Directory.CreateDirectory(runtime);
        var empty = await OfficeRecoveryCoordinator.RecoverAsync(missing, runtime, default);
        check(!empty.NeedsAttention && !Directory.Exists(missing), "Office startup leaves missing context storage absent and quiet");
        var root = Path.Combine(stage, "contexts"); Directory.CreateDirectory(root);
        var completed = Record(root, runtime, "completed");
        var pending = Record(root, runtime, "intent");
        var live = Record(root, runtime, "live");
        var corrupt = Path.Combine(root, Guid.NewGuid().ToString("N") + ".ownership");
        File.WriteAllBytes(corrupt, [4, 0, 0, 0, 123]);
        var orphan = Path.Combine(root, "office-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "retained.txt"), "Partial preparation evidence");
        var snapshots = new[] { completed, pending, live, corrupt }.ToDictionary(path => path, File.ReadAllBytes);
        var report = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, default);
        check(report.Entries.Count == 5 && !report.Incomplete && report.NeedsAttention, "Office startup accounts for all bounded records and orphan preparation");
        check(report.Entries.Single(entry => entry.Path == completed).State == OfficeRecoveryState.AlreadyClean,
            "Office startup skips a completed cleanup record without native mutations");
        foreach (var path in new[] { pending, live, corrupt, orphan })
            check(report.Entries.Single(entry => entry.Path == path).State == OfficeRecoveryState.ReviewRequired,
                "Office startup retains uncertain or live ownership for review: " + Path.GetFileName(path));
        check(snapshots.All(entry => File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)) &&
            File.ReadAllText(Path.Combine(orphan, "retained.txt")) == "Partial preparation evidence",
            "Office startup refusals preserve all journal and partial-file bytes");
        check(report.Notice.Contains(root) && report.Notice.Contains("needs review") && !report.Notice.Contains("Cleaned up"),
            "Office startup notice distinguishes review from successful cleanup and gives its retained location");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = await OfficeRecoveryCoordinator.RecoverAsync(root, runtime, cancellation.Token);
        check(canceled.Cancelled && canceled.Entries.Count == 0 && snapshots.All(entry => File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)),
            "Office startup cancellation before scanning preserves every pending record");
        var foreign = await OfficeRecoveryCoordinator.RecoverAsync(root, Path.Combine(stage, "other-runtime"), default);
        check(foreign.Entries.All(entry => entry.State == OfficeRecoveryState.ReviewRequired) && snapshots.All(entry => File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)),
            "Office startup refuses records from a different runtime root without mutations");
        var aliasRoot = Path.Combine(stage, "aliases"); Directory.CreateDirectory(aliasRoot);
        var alias = Path.Combine(aliasRoot, Path.GetFileName(completed));
        if (!CreateHardLink(alias, completed, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var aliased = await OfficeRecoveryCoordinator.RecoverAsync(aliasRoot, runtime, default);
        check(aliased.Entries.Single().State == OfficeRecoveryState.ReviewRequired && File.ReadAllBytes(completed).SequenceEqual(snapshots[completed]),
            "Office startup refuses a multiply linked journal before accepting its contents");
        var crowded = Path.Combine(stage, "crowded"); Directory.CreateDirectory(crowded);
        for (var index = 0; index <= OfficeRecoveryCoordinator.MaximumDirectoryEntries; index++)
            File.WriteAllText(Path.Combine(crowded, index + ".txt"), "retained");
        var capped = await OfficeRecoveryCoordinator.RecoverAsync(crowded, runtime, default);
        check(capped.Incomplete && capped.NeedsAttention && capped.Entries.Count == 0 &&
            Directory.GetFiles(crowded).Length == OfficeRecoveryCoordinator.MaximumDirectoryEntries + 1,
            "Office startup reports the directory entry limit without an unbounded scan or cleanup");
        var many = Path.Combine(stage, "many-records"); Directory.CreateDirectory(many);
        for (var index = 0; index <= OfficeRecoveryCoordinator.MaximumRecords; index++)
            File.WriteAllBytes(Path.Combine(many, Guid.NewGuid().ToString("N") + ".ownership"), [42]);
        var bounded = await OfficeRecoveryCoordinator.RecoverAsync(many, runtime, default);
        check(bounded.Incomplete && bounded.Entries.Count == OfficeRecoveryCoordinator.MaximumRecords &&
            Directory.GetFiles(many).All(path => File.ReadAllBytes(path).SequenceEqual(new byte[] { 42 })),
            "Office startup respects the journal budget and preserves malformed records");
        var worker = new WorkerClient(Path.Combine(stage, "not-launched.exe"));
        await using var model = new MainViewModel(worker);
        var changed = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.RecoveryNotice)) changed++; };
        model.SetOfficeRecoveryReport(report);
        check(model.RecoveryNotice == report.Notice && changed == 1 && worker.ProcessId is null,
            "Office startup report refreshes the existing status without launching a worker");
        model.SetOfficeRecoveryReport(empty);
        check(model.RecoveryNotice.Length == 0 && changed == 2, "Office empty recovery report leaves ordinary startup quiet");
        var recoveredNotice = new OfficeRecoveryReport(root, [new(completed, OfficeRecoveryState.Recovered)]);
        check(recoveredNotice.NeedsAttention && recoveredNotice.Notice.Contains("Run Convert again") && !recoveredNotice.Notice.Contains("needs review"),
            "Office recovered interruption offers retry without claiming completed conversion");
    }

    private static string Record(string root, string runtime, string state)
    {
        var id = Guid.NewGuid(); var directory = Path.Combine(root, "office-" + id.ToString("N")); Directory.CreateDirectory(directory);
        var work = new OfficeExportWork(id, directory, "ContextSuite.Office.Evaluation." + id.ToString("N"), "docx", "none", 1, new string('0', 64));
        var path = Path.Combine(root, id.ToString("N") + ".ownership");
        using var journal = OfficeOwnershipJournal.Create(path, work, runtime);
        if (state != "intent") journal.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(work.ProfileName),
            OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName), new string('A', 48)));
        if (state == "completed")
        {
            journal.Record(new(OfficeOwnershipStep.CleanupIntent));
            journal.Record(new(OfficeOwnershipStep.DeleteIntent)); journal.Record(new(OfficeOwnershipStep.ProfileDeleted));
        }
        // Authored schema fixtures only: never create native profiles in this suite.
        return path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);
}
