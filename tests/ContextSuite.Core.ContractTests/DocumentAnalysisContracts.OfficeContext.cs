using System.ComponentModel;
using System.Runtime.InteropServices;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;

internal static partial class DocumentAnalysisContracts
{
    internal static async Task OfficeContextContractsAsync(string scratch, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-preparation-" + Guid.NewGuid().ToString("N")));
        var root = Path.Combine(stage, "contexts"); var runtime = Path.Combine(stage, "runtime");
        var originals = Path.Combine(stage, "originals");
        foreach (var path in new[] { root, runtime, originals }) Directory.CreateDirectory(path);
        foreach (var format in new[] { "docx", "xlsx", "pptx" })
        {
            var folder = Path.Combine(originals, format); Directory.CreateDirectory(folder);
            var original = Path.Combine(folder, "authored \u00fc." + format); var bytes = OpenXml(format);
            await File.WriteAllBytesAsync(original, bytes);
            var originalTime = File.GetLastWriteTimeUtc(original); var originalAttributes = File.GetAttributes(original);
            var calculation = format == "xlsx" ? "cached" : "none";
            var context = await OfficeContextPreparation.CreateAsync(root, runtime, original, format, calculation, default);
            var work = context.Work;
            check(context.Journal.Version == 4 && context.Journal.Owner.ContextDirectories is not null,
                "Office preparation binds generated directories in its version-four journal: " + format);
            using (context)
            {
                check(File.ReadAllBytes(work.SourcePath).SequenceEqual(bytes) && context.OriginalIdentity.Sha256 == work.SourceSha256 &&
                    context.OriginalIdentity.Length == work.SourceBytes, "Office preparation copies exact source identity: " + format);
                check(File.GetAttributes(work.SourcePath).HasFlag(FileAttributes.ReadOnly) && work.Calculation == calculation &&
                    work.ProfileName == "ContextSuite.Office." + work.ItemId.ToString("N"), "Office preparation retains explicit policy and read-only snapshot: " + format);
                check(context.Journal.Changes.Count == 1 && context.Journal.Changes[0].Step == OfficeOwnershipStep.ProfileIntent &&
                    !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)),
                    "Office preparation records intent without creating a native profile: " + format);
                Refuses(() => { using var writer = new FileStream(original, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); },
                    "Office preparation holds original against writes: " + format);
                Refuses(() => File.Move(original, original + ".moved"), "Office preparation holds original against replacement: " + format);
                Refuses(() => Directory.Move(folder, folder + "-moved"), "Office preparation holds original ancestors: " + format);
                Refuses(() => Directory.Move(work.DirectoryPath, work.DirectoryPath + "-moved"), "Office preparation holds context directory: " + format);
                File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) & ~FileAttributes.ReadOnly);
                try
                {
                    Refuses(() => { using var writer = new FileStream(work.SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); },
                        "Office snapshot write lease remains denied if its read-only attribute is cleared: " + format);
                    await RejectAsync(() => context.VerifyAsync(default), "Office verification refuses cleared snapshot protection: " + format);
                }
                finally { File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) | FileAttributes.ReadOnly); }
                await context.VerifyAsync(default);
                check(true, "Office source and snapshot revalidate after protection restoration: " + format);
            }
            check(File.ReadAllBytes(original).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(original) == originalTime &&
                File.GetAttributes(original) == originalAttributes, "Office preparation leaves original bytes, time and attributes unchanged: " + format);
            foreach (var path in new[] { original, work.SourcePath })
            {
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                check(true, "Office preparation releases its file leases: " + Path.GetFileName(path));
            }
        }
        var good = Path.Combine(originals, "docx", "authored \u00fc.docx");
        var countBefore = Directory.GetFileSystemEntries(root).Length;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, good, "docx", "none", canceled.Token); check(false, "Office canceled preparation refuses"); }
        catch (OperationCanceledException) { check(true, "Office canceled preparation preserves cancellation"); }
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, good, "xlsx", "cached", default); },
            "Office preparation refuses mismatched content despite caller format");
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, good, "xlsx", "none", default); },
            "Office preparation never invents an Excel calculation policy");
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, root, good, "docx", "none", default); },
            "Office preparation refuses overlapping runtime and contexts");
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(originals, runtime, good, "docx", "none", default); },
            "Office preparation refuses originals inside its context root");
        var invalid = Path.Combine(originals, "invalid.docx");
        await File.WriteAllTextAsync(invalid, "This extension is not an Office document.");
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, invalid, "docx", "none", default); },
            "Office preparation refuses extension-only identification before creating files");
        var oversized = Path.Combine(originals, "large.docx");
        using (var file = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write)) file.SetLength(OfficeHostProtocol.MaximumSourceBytes + 1);
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, oversized, "docx", "none", default); },
            "Office preparation checks its source budget before parsing or copying");
        var alias = Path.Combine(originals, "alias.docx");
        if (!CreateHardLink(alias, good, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        await RejectAsync(async () => { using var unused = await OfficeContextPreparation.CreateAsync(root, runtime, alias, "docx", "none", default); },
            "Office preparation refuses multiply linked source files");
        // Keep the authored alias as failed-admission evidence; no source/profile cleanup occurs.
        check(Directory.GetFileSystemEntries(root).Length == countBefore,
            "Office preparation refusals create neither contexts nor ownership records");

        // Authored completed journals exercise file retirement only. No Windows
        // profile is created here; actual profile removal is tested separately.
        foreach (var scenario in new[] { "complete", "locked", "linked", "unexpected", "replaced", "deep" })
        {
            var source = Path.Combine(originals, "retire-" + scenario + ".docx");
            var bytes = OpenXml("docx"); File.WriteAllBytes(source, bytes);
            var written = File.GetLastWriteTimeUtc(source);
            using var prepared = await OfficeContextPreparation.CreateAsync(root, runtime, source, "docx", "none", default);
            var work = prepared.Work;
            var record = Path.Combine(root, work.ItemId.ToString("N") + ".ownership");
            Refuses(prepared.Retire, "Office retirement refuses incomplete profile intent: " + scenario);
            prepared.Journal.Record(new(OfficeOwnershipStep.ProfileCreated, OfficeOwnershipJournal.ProfileSid(work.ProfileName),
                OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName), new string('A', 48)));
            prepared.Journal.Record(new(OfficeOwnershipStep.CleanupIntent));
            prepared.Journal.Record(new(OfficeOwnershipStep.DeleteIntent));
            prepared.Journal.Record(new(OfficeOwnershipStep.ProfileDeleted));
            var cache = Path.Combine(work.DirectoryPath, "temp", "cache.bin"); File.WriteAllText(cache, "owned temporary bytes");
            if (scenario is "locked" or "replaced")
            {
                using (var locked = new FileStream(cache, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Refuses(prepared.Retire, "Office retirement refuses a locked child before deleting: " + scenario);
                    check(File.Exists(work.SourcePath) && File.Exists(record), "Office locked retirement keeps snapshot and journal: " + scenario);
                    Refuses(() => { using var writer = new FileStream(source, FileMode.Open, FileAccess.Write, FileShare.Read); },
                        "Office retirement failure retains the original lease: " + scenario);
                }
            }
            if (scenario == "replaced")
            {
                var input = Path.Combine(work.DirectoryPath, "input"); var moved = Path.Combine(root, "held-" + work.ItemId.ToString("N"));
                check(Path.GetDirectoryName(input) == work.DirectoryPath && Path.GetDirectoryName(moved) == root &&
                    work.DirectoryPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "Office directory-substitution fixture stays in its owned context");
                Directory.Move(input, moved); Directory.CreateDirectory(input);
                try { prepared.Retire(); check(false, "Office retirement refuses a replaced owned directory"); }
                catch (IOException error) { check(error.Message.Contains("directory identity changed", StringComparison.Ordinal),
                    "Office retirement identifies the replaced directory before deletion"); }
                check(File.Exists(Path.Combine(moved, "source.docx")) && File.Exists(cache) && File.Exists(record),
                    "Office directory substitution preserves all existing evidence");
                Directory.Delete(input); Directory.Move(moved, input);
            }
            if (scenario == "linked")
            {
                var linked = Path.Combine(work.DirectoryPath, "temp", "source-alias.docx");
                if (!CreateHardLink(linked, source, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Refuses(prepared.Retire, "Office retirement refuses a hard-linked child before deletion");
                check(File.Exists(work.SourcePath) && File.Exists(cache) && File.Exists(record), "Office hard-link refusal keeps all context evidence");
                File.Delete(linked);
            }
            if (scenario == "unexpected")
            {
                var unrelated = Path.Combine(work.DirectoryPath, "unrelated.txt"); File.WriteAllText(unrelated, "keep");
                Refuses(prepared.Retire, "Office retirement refuses an unexpected context-root file");
                check(File.ReadAllText(unrelated) == "keep" && File.Exists(work.SourcePath), "Office unexpected file is preserved");
                File.Delete(unrelated);
            }
            if (scenario == "deep")
            {
                var child = Path.Combine(work.DirectoryPath, "temp");
                for (var index = 0; index < 33; index++) { child = Path.Combine(child, "d"); Directory.CreateDirectory(child); }
                Refuses(prepared.Retire, "Office retirement bounds directory depth before deletion");
                check(File.Exists(work.SourcePath) && File.Exists(cache) && File.Exists(record), "Office over-depth retirement retains evidence");
            }
            else
            {
                prepared.Retire();
                check(!Directory.Exists(work.DirectoryPath) && !File.Exists(record), "Office retirement removes generated context and journal: " + scenario);
                using var exclusive = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
                check(true, "Office retirement releases the original lease after cleanup: " + scenario);
            }
            check(File.ReadAllBytes(source).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(source) == written && Directory.Exists(runtime),
                "Office retirement preserves original bytes/time and the runtime: " + scenario);
        }

        await OfficeRetirementContracts.RunAsync(Path.GetFullPath(scratch), OpenXml("docx"), check);

        void Refuses(Action action, string name)
        {
            try { action(); check(false, name); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception) { check(true, name); }
        }
        async Task RejectAsync(Func<Task> action, string name)
        {
            try { await action(); check(false, name); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception) { check(true, name); }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string path, string existing, IntPtr security);
}
