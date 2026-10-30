using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

internal static class OfficeWorkerContracts
{
    internal static async Task RunAsync(string worker, string fixtures, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Use a new owned Office worker contract directory.");
        Directory.CreateDirectory(root);
        var engine = Path.Combine(Path.GetDirectoryName(worker)!, "office-engine");
        var checks = new List<string>(); var reports = new List<object>();
        foreach (var (format, name) in new[] { ("docx", "Word ü.docx"), ("xlsx", "Excel ü.xlsx"), ("pptx", "PowerPoint ü.pptx") })
        {
            var original = Path.Combine(fixtures, name);
            var originalHash = Hash(original); var originalTime = File.GetLastWriteTimeUtc(original);
            using var prepared = await OfficeContextPreparation.CreateAsync(root, engine, original, format,
                format == "xlsx" ? "cached" : "none", CancellationToken.None);
            var work = prepared.Work; var directory = work.DirectoryPath;
            Check(prepared.OriginalIdentity.Sha256 == originalHash && work.SourceSha256 == originalHash &&
                prepared.Journal.Changes.Count == 1, format + ": application prepares the exact source and durable intent before profile creation");
            var sourceTime = File.GetLastWriteTimeUtc(work.SourcePath);
            OfficeExportCandidate? candidate = null;
            var journal = prepared.Journal;
            var owner = OfficeSandboxOwner.Create(journal);
            WorkerClient? client = null;
            var entered = false;
            var sid = owner.Sid;
            var receipt = Path.Combine(root, format + "-profile.json");
            await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { work.ProfileName, Sid = sid, Removed = false }));
            try
            {
                Check(ProfileState(work.ProfileName, sid) == (true, true), format + ": application owns the profile folder and mapping");
                try { owner.ReleaseRecoveryLeases(); throw new IOException("Original owner released recovery leases."); }
                catch (InvalidOperationException) { checks.Add(format + ": original ownership cannot abandon its native cleanup through recovery release"); }
                try { prepared.Dispose(); throw new InvalidOperationException("Prepared context released before profile cleanup."); }
                catch (IOException) { checks.Add(format + ": preparation retains its leases until native profile cleanup completes"); }
                owner.GrantDirectory(engine, false);
                owner.GrantDirectory(Path.Combine(directory, "input"), false);
                foreach (var child in new[] { "output", "profile", "temp" }) owner.GrantDirectory(Path.Combine(directory, child), true);
                client = new WorkerClient(worker, Path.Combine(root, "worker-scratch"));
                await client.GetCapabilitiesAsync(CancellationToken.None);
                using var previousWorker = Process.GetProcessById(client.ProcessId!.Value);
                _ = previousWorker.Handle;
                if (previousWorker.HasExited) throw new IOException("Ordinary worker exited before its transition test.");
                try
                {
                    await client.ExportOfficeAsync(work, CancellationToken.None, _ =>
                    {
                        Check(previousWorker.HasExited && client.ProcessId != previousWorker.Id,
                            format + ": ordinary worker stops before journaled Office dispatch");
                        throw new IOException("Simulated ownership recording failure.");
                    });
                    throw new IOException("Office dispatch ignored its recording failure.");
                }
                catch (MediaWorkerException error) when (error.InnerException is IOException cause && cause.Message == "Simulated ownership recording failure.")
                { checks.Add(format + ": recording failure refuses Office dispatch"); }
                Check(client.ProcessId is null && journal.Changes.Count == 12 && !File.Exists(work.CandidatePath) && !Directory.Exists(work.EngineProfilePath),
                    format + ": failed recording stops the idle worker without starting Office or extending the journal");
                void BeforeRequest(WorkerLifetimeIdentity lifetime)
                {
                    if (!entered)
                    {
                        using var process = Process.GetProcessById(client.ProcessId!.Value);
                        using var job = OpenJobObject(4, false, lifetime.Name);
                        Check(!job.IsInvalid && IsProcessInJob(process.SafeHandle, job, out var member) && member,
                            format + ": verified worker belongs to the recorded named lifetime before dispatch");
                    }
                    journal.RecordEngineIntent(lifetime); entered = true;
                }
                File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) & ~FileAttributes.ReadOnly);
                try
                {
                    await client.ExportOfficeAsync(work, CancellationToken.None, BeforeRequest);
                    throw new IOException("Mutable Office source snapshot was accepted.");
                }
                catch (MediaWorkerException error) when (error.Failure == ImageFailure.InvalidInput)
                { checks.Add(format + ": worker rejects a mutable source snapshot"); }
                finally { File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) | FileAttributes.ReadOnly); }
                try
                {
                    await client.ExportOfficeAsync(work with { SourceSha256 = new string('0', 64) }, CancellationToken.None, BeforeRequest);
                    throw new IOException("Wrong Office source identity was accepted.");
                }
                catch (MediaWorkerException error) when (error.Failure == ImageFailure.InvalidInput)
                { checks.Add(format + ": worker rejects changed source identity"); }
                Check(!File.Exists(work.CandidatePath) && !Directory.Exists(work.EngineProfilePath), format + ": refusals leave engine profile and candidate absent");
                candidate = await client.ExportOfficeAsync(work, CancellationToken.None, BeforeRequest);
                Check(candidate.ItemId == work.ItemId && candidate.Completion.OutputBytes == new FileInfo(work.CandidatePath).Length &&
                    candidate.Completion.OutputSha256 == Hash(work.CandidatePath), format + ": actual worker candidate matches host completion and bytes");
                Check(Hash(work.SourcePath) == originalHash && File.GetLastWriteTimeUtc(work.SourcePath) == sourceTime,
                    format + ": copied source unchanged");
                try
                {
                    await client.ExportOfficeAsync(work, CancellationToken.None, BeforeRequest);
                    throw new IOException("Completed Office context was reused.");
                }
                catch (MediaWorkerException error) when (error.Failure == ImageFailure.InvalidInput)
                { checks.Add(format + ": completed context cannot be reused"); }
                Check(Hash(work.CandidatePath) == candidate.Completion.OutputSha256, format + ": refused reuse preserves candidate");
                await prepared.VerifyAsync(CancellationToken.None);
                Check(true, format + ": original and prepared snapshot remain bound after actual Office export");
            }
            finally
            {
                // Record shutdown only after the application client has verified
                // its lifetime job is empty. Failed stop retains the owner/journal.
                if (client is not null) await client.DisposeAsync();
                if (entered) journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
                await owner.DisposeAsync();
                if (ProfileState(work.ProfileName, sid) != (false, false)) throw new IOException("Owned test profile remains after cleanup.");
                await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { work.ProfileName, Sid = sid, Removed = true }));
            }
            Check(ProfileState(work.ProfileName, sid) == (false, false), format + ": profile folder and mapping removed");
            Check(journal.Version == 4 && journal.Owner.ContextDirectories is not null &&
                journal.Changes.Count == 27 && journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted &&
                journal.Changes.Single(change => change.Step == OfficeOwnershipStep.EngineIntent).Lifetime is not null,
                format + ": actual export and cleanup have a complete ownership journal");
            Check(Hash(original) == originalHash && File.GetLastWriteTimeUtc(original) == originalTime, format + ": original fixture unchanged");
            prepared.Dispose();
            foreach (var path in new[] { work.SourcePath, work.CandidatePath, Path.Combine(work.EngineProfilePath, "user", "registrymodifications.xcu") })
            {
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                checks.Add(format + ": all processes release " + Path.GetFileName(path));
            }
            reports.Add(new { Work = work, Candidate = candidate, Output = work.CandidatePath });
            Console.WriteLine("Completed Office worker export and cleanup: " + format);
            await File.WriteAllTextAsync(Path.Combine(root, "progress.json"), JsonSerializer.Serialize(reports));
        }
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Reports = reports },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Office worker contracts passed: {checks.Count}.");

        void Check(bool condition, string name) { if (!condition) throw new IOException(name); checks.Add(name); }
    }

    private static (bool Folder, bool Mapping) ProfileState(string name, string sid)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", name);
        using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
        return (Directory.Exists(folder), mapping is not null);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    [DllImport("kernel32.dll", EntryPoint = "OpenJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool member);
}
