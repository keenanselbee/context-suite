using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Read the pinned MSI as a database. Never run an install sequence/custom action.
if (args.Length != 2 || args[0] is not ("--inspect" or "--unpack")) return 2;
var root = Path.GetFullPath(args[1]);
if (!root.Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar + "office-engine" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Use an isolated office-engine scratch directory.");
var msi = Path.Combine(root, "upstream.msi");
using var archiveLease = new FileStream(msi, FileMode.Open, FileAccess.Read, FileShare.Read);
if (Convert.ToHexString(SHA256.HashData(archiveLease)) != "F9877032FD908BEB9C0DDF06DF4AF5C2E85F419C42E14876C4CCE5AAE5FB2660")
    throw new InvalidDataException("MSI identity mismatch.");
Native.Check(Native.MsiOpenDatabase(msi, IntPtr.Zero, out var database)); // MSIDBOPEN_READONLY
try
{
    var directories = Rows("SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`", 3);
    var components = Rows("SELECT `Component`, `Directory_` FROM `Component`", 2);
    var files = Rows("SELECT `File`, `Component_`, `FileName`, `FileSize` FROM `File`", 4);
    var media = Rows("SELECT `Cabinet` FROM `Media`", 1);
    if (args[0] == "--inspect")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { Directories = directories, Components = components.Length, Files = files.Length, Media = media }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    var payload = Path.Combine(root, "unpacked"); var raw = Path.Combine(root, "cabinets");
    if (Directory.Exists(payload) || Directory.Exists(raw)) throw new IOException("Use fresh unpacking directories.");
    Directory.CreateDirectory(payload); Directory.CreateDirectory(raw);
    var directoryMap = directories.ToDictionary(row => row[0]);
    var componentMap = components.ToDictionary(row => row[0], row => row[1]);
    var rawFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var cabinet in media.Select(row => row[0]).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!cabinet.StartsWith('#')) throw new InvalidDataException("Expected an embedded cabinet.");
        var name = cabinet[1..]; SafePart(name);
        var cabinetPath = Path.Combine(raw, name);
        var destination = Path.Combine(raw, Path.GetFileNameWithoutExtension(name)); Directory.CreateDirectory(destination);
        Native.Check(Native.MsiDatabaseOpenView(database, "SELECT `Data` FROM `_Streams` WHERE `Name`='" + name.Replace("'", "''") + "'", out var view));
        try
        {
            Native.Check(Native.MsiViewExecute(view, 0)); Native.Check(Native.MsiViewFetch(view, out var record));
            try
            {
                using var output = new FileStream(cabinetPath, FileMode.CreateNew);
                var buffer = new byte[65536]; long total = 0;
                while (true)
                {
                    uint size = (uint)buffer.Length; Native.Check(Native.MsiRecordReadStream(record, 1, buffer, ref size));
                    if (size == 0) break;
                    total += size; if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Cabinet exceeds evaluation budget.");
                    output.Write(buffer, 0, (int)size);
                }
            }
            finally { Native.MsiCloseHandle(record); }
        }
        finally { Native.MsiCloseHandle(view); }
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var value in new[] { "-F:*", cabinetPath, destination }) start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException("Cannot expand the cabinet.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { process.Kill(true); throw; }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new IOException("Cabinet expansion failed: " + await stderr);
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(file) != destination || !rawFiles.TryAdd(Path.GetFileName(file), file))
                throw new InvalidDataException("Unexpected nested or duplicate cabinet member.");
        }
    }
    var inventory = new List<object>(); var excluded = new List<string>(); var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files)
    {
        var directory = ResolveDirectory(componentMap[file[1]], new HashSet<string>());
        if (directory is null) { excluded.Add(file[0]); continue; }
        SafePart(file[0]); var name = file[2].Split('|')[^1]; SafePart(name);
        var relative = Path.Combine(directory, name); var target = Path.GetFullPath(Path.Combine(payload, relative));
        if (!target.StartsWith(payload + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !outputNames.Add(target))
            throw new InvalidDataException("Unsafe or duplicate mapped output.");
        if (!rawFiles.TryGetValue(file[0], out var original) || new FileInfo(original).Length != long.Parse(file[3], System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException("Cabinet member does not match MSI File table.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(original, target, false);
        using var input = File.OpenRead(target);
        inventory.Add(new { Path = relative.Replace('\\', '/'), Bytes = input.Length, Sha256 = Convert.ToHexString(SHA256.HashData(input)) });
    }
    File.WriteAllText(Path.Combine(root, "inventory.json"), JsonSerializer.Serialize(new { Files = inventory, ExcludedFileIds = excluded }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Mapped {inventory.Count} files; excluded {excluded.Count} files outside INSTALLLOCATION. No installer actions executed.");
    return 0;

    string? ResolveDirectory(string id, HashSet<string> seen)
    {
        if (id == "INSTALLLOCATION") return "";
        if (!seen.Add(id) || seen.Count > 64) throw new InvalidDataException("Directory cycle or excessive depth.");
        if (!directoryMap.TryGetValue(id, out var row) || row[1].Length == 0) return null;
        var parent = ResolveDirectory(row[1], seen); if (parent is null) return null;
        var part = row[2].Split(':')[0].Split('|')[^1];
        if (part == ".") return parent;
        SafePart(part); return Path.Combine(parent, part);
    }
    string[][] Rows(string query, uint count)
    {
        Native.Check(Native.MsiDatabaseOpenView(database, query, out var view));
        try
        {
            Native.Check(Native.MsiViewExecute(view, 0)); var result = new List<string[]>();
            while (true)
            {
                var status = Native.MsiViewFetch(view, out var record); if (status == 259) break; Native.Check(status);
                try
                {
                    if (result.Count >= 100000) throw new InvalidDataException("MSI table exceeds evaluation budget.");
                    var row = new string[count];
                    for (uint field = 1; field <= count; field++)
                    {
                        var buffer = new StringBuilder(32768); uint size = 32768;
                        Native.Check(Native.MsiRecordGetString(record, field, buffer, ref size)); row[field - 1] = buffer.ToString();
                    }
                    result.Add(row);
                }
                finally { Native.MsiCloseHandle(record); }
            }
            return result.ToArray();
        }
        finally { Native.MsiCloseHandle(view); }
    }
}
finally { Native.MsiCloseHandle(database); }

static void SafePart(string value)
{
    if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.EndsWith('.') || value.EndsWith(' '))
        throw new InvalidDataException("Invalid MSI path component.");
}
internal static class Native
{
    public static void Check(uint code) { if (code != 0) throw new System.ComponentModel.Win32Exception((int)code); }
    [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode)] public static extern uint MsiOpenDatabase(string path, IntPtr mode, out uint database);
    [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode)] public static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
    [DllImport("msi.dll")] public static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")] public static extern uint MsiViewFetch(uint view, out uint record);
    [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode)] public static extern uint MsiRecordGetString(uint record, uint field, StringBuilder value, ref uint length);
    [DllImport("msi.dll")] public static extern uint MsiRecordReadStream(uint record, uint field, byte[] buffer, ref uint length);
    [DllImport("msi.dll")] public static extern uint MsiCloseHandle(uint handle);
}
