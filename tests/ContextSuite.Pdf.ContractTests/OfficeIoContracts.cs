using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Private.Office;
using Microsoft.Win32;

internal static class OfficeIoContracts
{
    internal static async Task RunAsync(string worker, string fixtures, string evidence)
    {
        if (evidence != Path.GetFullPath(evidence) || !evidence.Contains("\\.codex-temp\\office-io\\", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(evidence)) throw new ArgumentException("Use a fresh owned Office I/O evidence directory.");
        Directory.CreateDirectory(evidence);
        var engine = Path.Combine(Path.GetDirectoryName(worker)!, "office-engine");
        var checks = new List<string>(); var profiles = new List<object>(); var reports = new List<object>(); var failures = new List<object>();
        var sids = new HashSet<string>(StringComparer.Ordinal);
        void Check(bool value, string message) { if (!value) throw new IOException(message); checks.Add(message); }
        await using var client = new WorkerClient(worker, Path.Combine(evidence, "worker-scratch"));
        foreach (var format in new[] { "docx", "xlsx", "pptx" })
        {
            var original = Directory.EnumerateFiles(fixtures, "*." + format).Single();
            var originalHash = Hash(original); var originalTime = File.GetLastWriteTimeUtc(original);
            await RunCaseAsync("direct-refusal");
            await RunCaseAsync("worker-refusal");
            var reused = client.ProcessId;
            await RunCaseAsync("following");
            Check(reused is not null && client.ProcessId == reused, format + ": following export and PDF validation reuse the same worker");
            Check(Hash(original) == originalHash && File.GetLastWriteTimeUtc(original) == originalTime, format + ": original bytes and timestamp unchanged");

            async Task RunCaseAsync(string mode)
            {
                var id = Guid.NewGuid();
                var work = new OfficeExportWork(id, Path.Combine(evidence, "office-" + id.ToString("N")),
                    "ContextSuite.Office.Evaluation." + id.ToString("N"), format, format == "xlsx" ? "cached" : "none",
                    new FileInfo(original).Length, originalHash);
                foreach (var name in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(work.DirectoryPath, name));
                File.Copy(original, work.SourcePath); File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) | FileAttributes.ReadOnly);
                var owner = OfficeSandboxOwner.Create(work.ProfileName); sids.Add(owner.Sid);
                var receipt = Path.Combine(evidence, id.ToString("N") + "-profile.json");
                await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { work.ProfileName, Sid = owner.Sid, Removed = false }));
                try
                {
                    owner.GrantDirectory(engine, false); owner.GrantDirectory(Path.Combine(work.DirectoryPath, "input"), false);
                    // A real filesystem denial limited to this disposable SID and
                    // output tree. No inherited user permissions are changed.
                    owner.GrantDirectory(Path.Combine(work.DirectoryPath, "output"), mode == "following");
                    foreach (var name in new[] { "profile", "temp" }) owner.GrantDirectory(Path.Combine(work.DirectoryPath, name), true);
                    OfficeExportCandidate? candidate = null;
                    Exception? failure = null;
                    try
                    {
                        candidate = mode == "direct-refusal"
                            ? await OfficeFileAdapter.ExportAsync(engine, work, Guid.NewGuid(), CancellationToken.None)
                            : await client.ExportOfficeAsync(work, CancellationToken.None);
                    }
                    catch (Exception error) when (error is InvalidDataException or MediaWorkerException) { failure = error; }
                    if (mode != "following")
                    {
                        Check(candidate is null && failure is not null, format + " " + mode + ": output creation failure returns no accepted completion");
                        if (mode == "direct-refusal")
                            Check(failure is InvalidDataException { Message: "Office export did not complete successfully.", InnerException: IOException details } &&
                                details.Message.Contains("Office host did not complete: PDF export", StringComparison.Ordinal),
                                format + ": direct diagnostic proves failure during real PDF save");
                        else
                            Check(failure is MediaWorkerException { Failure: ImageFailure.InvalidInput }, format + ": worker returns its existing sanitized failure");
                        Check(!Directory.EnumerateFileSystemEntries(Path.Combine(work.DirectoryPath, "output")).Any(),
                            format + " " + mode + ": denied output tree contains no PDF or workbook");
                        Check(File.Exists(Path.Combine(work.EngineProfilePath, "user", "registrymodifications.xcu")),
                            format + " " + mode + ": actual renderer initialized before refusal");
                        failures.Add(new { Format = format, Mode = mode, Work = work, Failure = failure!.Message,
                            Diagnostic = mode == "direct-refusal" ? failure.InnerException?.Message : null });
                    }
                    else
                    {
                        Check(failure is null && candidate is not null && Hash(work.CandidatePath) == candidate.Completion.OutputSha256,
                            format + ": following real export matches its completion");
                        var reservation = Path.Combine(evidence, ".context-suite-" + id.ToString("N") + ".tmp");
                        using (File.Open(reservation, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) { }
                        var validated = await client.ValidateOfficePdfAsync(new(work, candidate!, reservation), CancellationToken.None);
                        Check(validated.Validation.MatchesPlan && validated.PageCount > 0 && Hash(reservation) == Hash(work.CandidatePath),
                            format + ": following PDF passes independent readers and exact reservation copy");
                        File.Delete(reservation);
                        reports.Add(new { Work = work, Candidate = candidate, Output = work.CandidatePath });
                    }
                    Check(Hash(work.SourcePath) == originalHash, format + " " + mode + ": read-only source snapshot unchanged");
                }
                finally
                {
                    await owner.DisposeAsync();
                    using var mapping = Registry.CurrentUser.OpenSubKey(
                        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + owner.Sid);
                    Check(mapping is null && !Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages", work.ProfileName)), format + " " + mode + ": profile folder and mapping removed");
                    await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { work.ProfileName, Sid = owner.Sid, Removed = true }));
                    profiles.Add(new { work.ProfileName, Sid = owner.Sid, Removed = true });
                }
                using (new FileStream(work.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                Check(true, format + " " + mode + ": source lease released");
                await File.WriteAllTextAsync(Path.Combine(evidence, "progress.json"), JsonSerializer.Serialize(new { Checks = checks, Profiles = profiles, Failures = failures, Reports = reports }));
                Console.WriteLine("Completed I/O case: " + format + " " + mode);
            }
        }
        await client.DisposeAsync(); Check(client.ProcessId is null, "worker stops after all I/O cases");
        var audited = 0;
        foreach (var path in new[] { engine, evidence }.SelectMany(root => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Prepend(root)))
        {
            var bytes = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm()
                : new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
            var acl = new RawSecurityDescriptor(bytes, 0).DiscretionaryAcl;
            if (acl is not null && acl.Cast<GenericAce>().OfType<KnownAce>().Any(ace => sids.Contains(ace.SecurityIdentifier.Value)))
                throw new IOException("Retained test SID in runtime/context ACL: " + path);
            audited++;
        }
        Check(true, "all " + audited + " runtime/context ACL entries are free of all nine test SIDs");
        foreach (var process in Process.GetProcessesByName("ContextSuite.OfficeHost"))
            using (process)
                Check(process.HasExited || !string.Equals(process.MainModule?.FileName, Path.Combine(engine, "ContextSuite.OfficeHost.exe"),
                    StringComparison.OrdinalIgnoreCase), "no native host remains for the isolated runtime");
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
            { Passed = true, Checks = checks, Profiles = profiles, Failures = failures, Reports = reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Passed " + checks.Count + " Office I/O checks.");
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
