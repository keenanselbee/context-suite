using System.Diagnostics;
using System.Text.Json;
using ContextSuite.Private.Office;

internal static class OfficeRuntimeContracts
{
    internal static async Task RunAsync(string host, string runtime, string inventory, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-runtime\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Use a new owned Office runtime contract directory.");
        Directory.CreateDirectory(root);
        var anchor = Path.Combine(root, "leased-host"); Directory.CreateDirectory(anchor);
        var executable = Path.Combine(anchor, "ContextSuite.OfficeHost.exe");
        var manifest = Path.Combine(anchor, "runtime-files.txt");
        File.Copy(host, executable); File.Copy(inventory, manifest);
        var dependencyNames = new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
        foreach (var name in dependencyNames) File.Copy(Path.Combine(Path.GetDirectoryName(host)!, name), Path.Combine(anchor, name));
        var checks = new List<string>();
        var timer = Stopwatch.StartNew();
        MembershipChecks(root, checks);
        OfficeRuntimeFileContracts.Run(root, checks);
        using (var stopped = new CancellationTokenSource())
        {
            stopped.Cancel();
            try
            {
                using var unexpected = await OfficeRuntimeLease.OpenAsync(executable, runtime, manifest, stopped.Token);
                throw new IOException("Pre-cancelled runtime verification completed.");
            }
            catch (OperationCanceledException) { checks.Add("pre-cancelled verification refuses"); }
        }
        await Refused(executable, runtime + "\\..", manifest, "ambiguous runtime path refuses",
            error => error is InvalidDataException && error.Message == "Use an ordinary local runtime path within the candidate path limit.");
        var badHost = Path.Combine(root, "changed-host.exe");
        var hostBytes = File.ReadAllBytes(executable); hostBytes[^1] ^= 1;
        File.WriteAllBytes(badHost, hostBytes);
        await Refused(badHost, runtime, manifest, "host checksum mismatch refuses", HashMismatch);
        Exclusive(badHost, "failed host verification releases its file");
        var badManifest = Path.Combine(root, "changed-inventory.txt");
        var changed = File.ReadAllBytes(manifest); changed[0] ^= 1;
        File.WriteAllBytes(badManifest, changed);
        await Refused(executable, runtime, badManifest, "same-length changed inventory refuses", HashMismatch);
        Exclusive(executable, "failed inventory verification releases host");
        Exclusive(badManifest, "failed inventory verification releases manifest");
        var incomplete = Path.Combine(root, "incomplete-runtime"); Directory.CreateDirectory(incomplete);
        await Refused(executable, incomplete, manifest, "missing runtime file refuses", error => error is FileNotFoundException);
        Exclusive(executable, "missing runtime file releases host");
        Exclusive(manifest, "missing runtime file releases manifest");
        var movedIncomplete = Path.Combine(root, "released-runtime");
        Directory.Move(incomplete, movedIncomplete);
        Directory.Move(movedIncomplete, incomplete);
        checks.Add("failed verification releases directory rename lease");

        foreach (var name in dependencyNames)
        {
            var path = Path.Combine(anchor, name);
            var backup = Path.Combine(root, "original-" + name);
            File.Move(path, backup);
            await Refused(executable, runtime, manifest, "missing startup dependency refuses: " + name,
                error => error is FileNotFoundException);
            var corrupted = File.ReadAllBytes(backup); corrupted[^1] ^= 1;
            File.WriteAllBytes(path, corrupted);
            await Refused(executable, runtime, manifest, "same-length startup dependency corruption refuses: " + name, HashMismatch);
            Exclusive(path, "failed startup dependency verification releases " + name);
            File.Delete(path); File.Move(backup, path);
        }
        var unlistedLibrary = Path.Combine(anchor, "unreviewed.dll");
        File.WriteAllText(unlistedLibrary, "Authored unexpected host dependency; never executed.");
        await Refused(executable, runtime, manifest, "unlisted startup DLL refuses before runtime acquisition",
            error => error is InvalidDataException && error.Message == "Unexpected Office host file.");
        File.Delete(unlistedLibrary);
        Exclusive(executable, "host membership refusal releases host");
        Exclusive(manifest, "host membership refusal releases inventory");
        var separateManifest = Path.Combine(root, "separate-inventory.txt");
        File.Copy(manifest, separateManifest);
        await Refused(executable, runtime, separateManifest, "valid inventory outside host directory refuses",
            error => error is InvalidDataException && error.Message == "Keep the pinned Office host and inventory in the same directory.");
        Exclusive(separateManifest, "separate inventory refusal releases its file");

        var runtimeFiles = File.ReadLines(manifest).Select(line => Path.Combine(runtime, line.Split('\t')[0].Replace('/', '\\'))).ToArray();
        var protectedFiles = new[] { executable, manifest, runtimeFiles[0], runtimeFiles[^1] }
            .Concat(dependencyNames.Select(name => Path.Combine(anchor, name))).ToArray();
        using (var lease = await OfficeRuntimeLease.OpenAsync(executable, runtime, manifest, CancellationToken.None))
        {
            if (lease.Executable != executable || lease.DirectoryPath != runtime || lease.ProgramDirectory != Path.Combine(runtime, "program"))
                throw new IOException("Verified runtime paths differ from the request.");
            checks.Add("complete pinned runtime and host accepted");
            foreach (var path in protectedFiles)
            {
                try
                {
                    // Open only; never write to or truncate the pinned payload.
                    using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                    throw new InvalidOperationException("Leased runtime file allowed a writer.");
                }
                catch (IOException error) when ((error.HResult & 0xffff) == 32)
                { checks.Add("write sharing refused: " + Path.GetFileName(path)); }
            }
            var movedAnchor = Path.Combine(root, "unexpectedly-moved-host");
            var renamed = false;
            try
            {
                try { Directory.Move(anchor, movedAnchor); renamed = true; }
                catch (IOException error) when ((error.HResult & 0xffff) == 32)
                { checks.Add("owned host directory rename refused while leased"); }
                if (renamed) throw new IOException("Verified host directory rename was allowed.");
            }
            finally
            {
                if (renamed) { lease.Dispose(); Directory.Move(movedAnchor, anchor); }
            }
            lease.VerifyMembership(CancellationToken.None);
            checks.Add("runtime exact membership rechecked after lease assertions");
            File.WriteAllText(unlistedLibrary, "Authored dependency inserted during a lease; never executed.");
            MembershipRefused("Unexpected Office host file.", "post-acquisition startup DLL insertion detected");
            File.Delete(unlistedLibrary);
            var extraDirectory = Path.Combine(anchor, "unlisted-runtime");
            Directory.CreateDirectory(extraDirectory);
            MembershipRefused("Unexpected Office host directory.", "post-acquisition host directory insertion detected");
            Directory.Delete(extraDirectory);
            lease.VerifyMembership(CancellationToken.None);
            checks.Add("restored host and runtime membership accepted");
            lease.Dispose(); lease.Dispose();
            try { lease.VerifyMembership(CancellationToken.None); throw new IOException("Disposed lease accepted reuse."); }
            catch (ObjectDisposedException) { checks.Add("disposed lease refuses reuse"); }

            void MembershipRefused(string message, string name)
            {
                try { lease.VerifyMembership(CancellationToken.None); throw new IOException("Changed host membership accepted."); }
                catch (InvalidDataException error) when (error.Message == message) { checks.Add(name); }
            }
        }
        foreach (var path in protectedFiles)
            Exclusive(path, "successful verification releases " + Path.GetFileName(path));
        var released = Path.Combine(root, "released-host");
        Directory.Move(anchor, released); Directory.Move(released, anchor);
        checks.Add("successful verification releases directory rename lease");
        var colocated = string.Equals(Path.GetDirectoryName(host), Path.GetDirectoryName(runtime), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetDirectoryName(host), Path.GetDirectoryName(inventory), StringComparison.OrdinalIgnoreCase);
        if (colocated)
        {
            using var packageLease = await OfficeRuntimeLease.OpenAsync(host, runtime, inventory, CancellationToken.None);
            packageLease.VerifyMembership(CancellationToken.None);
            checks.Add("actual colocated host inventory and runtime accepted without changing payload");
        }
        var result = new { Passed = true, Checks = checks, RuntimeFiles = runtimeFiles.Length,
            ColocatedPackageChecked = colocated,
            RuntimeBytes = OfficeRuntimeLease.TotalBytes, HostSha256 = OfficeRuntimeLease.HostSha256,
            InventorySha256 = OfficeRuntimeLease.InventorySha256, ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Scope = "Runtime verification only; no Office execution, profile creation, installation or publication." };
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Office runtime contracts passed: {checks.Count}. Evidence: {root}");

        static bool HashMismatch(Exception error) => error is InvalidDataException &&
            error.Message == "Office runtime file differs from its pinned identity.";
        async Task Refused(string candidate, string folder, string list, string name, Func<Exception, bool> expected)
        {
            try
            {
                using var unexpected = await OfficeRuntimeLease.OpenAsync(candidate, folder, list, CancellationToken.None);
                throw new InvalidOperationException("Invalid runtime was accepted: " + name);
            }
            catch (Exception error) when (expected(error)) { checks.Add(name); }
        }
        void Exclusive(string path, string name)
        {
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            checks.Add(name);
        }
    }

    private static void MembershipChecks(string root, List<string> checks)
    {
        var directory = Path.Combine(root, "authored-membership");
        Directory.CreateDirectory(Path.Combine(directory, "folder"));
        var file = Path.Combine(directory, "file.txt");
        File.WriteAllText(file, "Authored runtime membership fixture.");
        File.WriteAllText(Path.Combine(directory, "folder", "inside.txt"), "Nested authored fixture.");
        var files = new HashSet<string>(["file.txt", "folder/inside.txt"], StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(["folder"], StringComparer.OrdinalIgnoreCase);
        OfficeRuntimeLease.CheckMembership(directory, files, directories, CancellationToken.None);
        checks.Add("authored exact membership accepted");
        var extra = Path.Combine(directory, "unexpected.txt");
        File.WriteAllText(extra, "Unexpected file.");
        Refused("Unexpected Office runtime file.");
        File.Delete(extra);
        var empty = Path.Combine(directory, "unexpected-empty"); Directory.CreateDirectory(empty);
        Refused("Unexpected Office runtime directory.");
        Directory.Delete(empty);
        var moved = Path.Combine(root, "temporarily-removed.txt"); File.Move(file, moved);
        Refused("Office runtime membership changed.");
        File.Move(moved, file);
        var movedFolder = Path.Combine(root, "temporarily-removed-folder");
        Directory.Move(Path.Combine(directory, "folder"), movedFolder);
        Refused("Office runtime membership changed.");
        Directory.Move(movedFolder, Path.Combine(directory, "folder"));
        OfficeRuntimeLease.CheckMembership(directory, files, directories, CancellationToken.None);
        checks.Add("restored authored membership accepted");

        void Refused(string message)
        {
            try
            {
                OfficeRuntimeLease.CheckMembership(directory, files, directories, CancellationToken.None);
                throw new IOException("Changed authored membership was accepted.");
            }
            catch (InvalidDataException error) when (error.Message == message) { checks.Add(message); }
        }
    }
}
