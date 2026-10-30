using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;
using ContextSuite.Private.Office;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
internal static class OfficeResourceContracts
{
    internal static async Task RunAsync(string probe, string expectedHash, string stage)
    {
        var full = Path.GetFullPath(stage);
        if (full != stage || !full.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || Directory.Exists(stage))
            throw new ArgumentException("Use a fresh owned Office isolation directory.");
        using (var native = MediaFiles.OpenRead(probe))
            if (Convert.ToHexString(SHA256.HashData(native)) != expectedHash) throw new IOException("Resource probe hash mismatch.");
        var binary = Path.Combine(stage, "binary"); var writable = Path.Combine(stage, "writable");
        Directory.CreateDirectory(binary); Directory.CreateDirectory(writable);
        var executable = Path.Combine(binary, "ContextSuite.Office.ResourceProbe.exe"); File.Copy(probe, executable);
        var original = Path.Combine(binary, "original.txt"); File.WriteAllText(original, "Disposable original; must remain unchanged.");
        var originalBytes = File.ReadAllBytes(original);
        var profile = "ContextSuite.Office.Evaluation." + Path.GetFileName(stage);
        File.WriteAllText(Path.Combine(stage, "profile-name.txt"), profile);
        var checks = new List<string>(); var cases = new List<object>();
        void Check(bool condition, string message) { if (!condition) throw new IOException(message); checks.Add(message); }
        var owner = OfficeSandboxOwner.Create(profile);
        File.WriteAllText(Path.Combine(stage, "profile-sid.txt"), owner.Sid);
        var profileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", profile);
        var mapping = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + owner.Sid;
        Exception? failure = null;
        try
        {
            owner.GrantDirectory(binary, false); owner.GrantDirectory(writable, true);
            await RunCaseAsync("control", "initial-control");
            foreach (var mode in new[] { "memory", "aggregate", "process", "stdout", "stderr" })
            {
                await RunCaseAsync(mode, mode);
                await RunCaseAsync("control", "after-" + mode);
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { owner.Dispose(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(mapping);
            Check(!Directory.Exists(profileFolder) && key is null, "disposable profile and SID mapping removed");
            var entries = Directory.EnumerateFileSystemEntries(stage, "*", SearchOption.AllDirectories).ToArray();
            foreach (var path in entries)
            {
                var descriptor = Directory.Exists(path)
                    ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm()
                    : new FileInfo(path).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
                var acl = new RawSecurityDescriptor(descriptor, 0).DiscretionaryAcl;
                if (acl is not null && acl.Cast<GenericAce>().OfType<KnownAce>().Any(ace => ace.SecurityIdentifier.Value == owner.Sid))
                    throw new IOException("Resource test retained an owned ACL entry: " + path);
            }
            Check(true, "all " + entries.Length + " scratch ACLs are free of the test SID");
            Check(File.ReadAllBytes(original).SequenceEqual(originalBytes), "original bytes unchanged");
            using var retained = MediaFiles.OpenRead(executable);
            Check(Convert.ToHexString(SHA256.HashData(retained)) == expectedHash, "probe bytes unchanged");
        }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        File.WriteAllText(Path.Combine(stage, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = failure is null, Checks = checks, Cases = cases, Profile = profile, Sid = owner.Sid, Failure = failure?.ToString()
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (failure is not null) throw new IOException("Office resource checks failed; inspect retained results: " + stage, failure);
        Console.WriteLine($"Passed {checks.Count} Office resource checks: {stage}");

        async Task RunCaseAsync(string mode, string name)
        {
            var marker = Path.Combine(writable, name + ".json");
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["APPDATA"] = writable, ["LOCALAPPDATA"] = writable, ["PATH"] = Path.Combine(windows, "System32"),
                ["SYSTEMROOT"] = windows, ["TEMP"] = writable, ["TMP"] = writable, ["USERPROFILE"] = writable, ["WINDIR"] = windows,
                ["CS_OFFICE_PROFILE_NAME"] = profile, ["CS_OFFICE_REQUEST_ID"] = Guid.NewGuid().ToString("N"),
                ["CS_OFFICE_FORMAT"] = "docx", ["CS_OFFICE_CALCULATION"] = mode,
                ["CS_OFFICE_SOURCE"] = original, ["CS_OFFICE_OUTPUT"] = marker, ["CS_OFFICE_PROFILE"] = writable,
                ["CS_OFFICE_PROGRAM"] = binary, ["CS_OFFICE_SOURCE_IDENTITY"] = "test-only",
                ["CS_OFFICE_OUTPUT_IDENTITY"] = "test-only", ["CS_OFFICE_PROFILE_IDENTITY"] = "test-only",
                ["CS_OFFICE_PROGRAM_IDENTITY"] = "test-only"
            };
            byte[]? reply = null; string? refusal = null;
            var started = Stopwatch.GetTimestamp();
            try { reply = await OfficeSandboxProcess.RunAsync(executable, binary, environment, CancellationToken.None, TimeSpan.FromSeconds(15)); }
            catch (InvalidDataException error) { refusal = error.Message; }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            using var report = JsonDocument.Parse(File.ReadAllBytes(marker)); var root = report.RootElement;
            Check(root.GetProperty("ProcessLimit").GetInt32() == 8 && root.GetProperty("MemoryLimit").GetInt64() == 1073741824,
                name + ": helper observes production job limits");
            var identities = root.GetProperty("Children").EnumerateArray().Prepend(root.GetProperty("Root")).ToArray();
            foreach (var item in identities)
            {
                var pid = item.GetProperty("Pid").GetInt32(); var created = item.GetProperty("Created").GetInt64();
                try
                {
                    using var process = Process.GetProcessById(pid);
                    Check(process.HasExited || process.StartTime.ToUniversalTime().ToFileTimeUtc() != created, name + ": recorded process stopped " + pid);
                }
                catch (ArgumentException) { Check(true, name + ": recorded process stopped " + pid); }
            }
            if (mode == "control")
            {
                Check(refusal is null && reply is not null && Encoding.UTF8.GetString(reply) == "{\"Control\":true}", name + ": successful launcher control");
                Check(root.GetProperty("Allocated").GetBoolean() && root.GetProperty("Requested").GetInt64() == 600L * 1024 * 1024 &&
                    root.GetProperty("PrivateAfter").GetInt64() >= root.GetProperty("PrivateBefore").GetInt64() + 600L * 1024 * 1024,
                    name + ": actual 600 MiB private commit succeeds");
            }
            else
            {
                var expected = mode is "stdout" or "stderr" ? "Office output or diagnostics exceeded its bound." : "Office export did not complete successfully.";
                Check(reply is null && refusal == expected, name + ": exact bounded launcher refusal");
                if (mode is "stdout" or "stderr")
                    Check(root.GetProperty("FloodBytes").GetInt32() == (mode == "stdout" ? OfficeHostProtocol.MaximumReplyBytes : 65536) + 1,
                        name + ": fixture exceeds the current channel bound by exactly one byte");
                if (mode is "memory" or "aggregate")
                    Check(!root.GetProperty("Allocated").GetBoolean() && root.GetProperty("Error").GetInt32() != 0 &&
                        root.GetProperty("Requested").GetInt64() == (mode == "memory" ? 1040L : 600L) * 1024 * 1024 &&
                        root.GetProperty("PrivateAfter").GetInt64() < root.GetProperty("PrivateBefore").GetInt64() + root.GetProperty("Requested").GetInt64(),
                        name + ": commit request refused without allocating its bytes");
                Check(identities.Length == (mode == "process" ? 8 : mode == "aggregate" ? 2 : 1), name + ": exact owned process count");
                if (mode == "process") Check(root.GetProperty("Error").GetInt32() == 1816, "ninth job process refused with ERROR_NOT_ENOUGH_QUOTA");
            }
            cases.Add(new { Name = name, ElapsedMilliseconds = elapsed, Refusal = refusal, Report = root.Clone(), ProcessesStopped = true });
        }
    }
}
