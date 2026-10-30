using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ContextSuite.Private.Office;

// Candidate runtime verification, not engine adoption or a rendering permit.
// Retain through execution. Existing files and directory names cannot be changed
// through ordinary file operations while leased. Check membership again after
// execution: directory handles do not prevent somebody creating a new child.
internal sealed class OfficeRuntimeLease : IDisposable
{
    internal const string HostSha256 = "3F5254F6D6188F5B3637C13BFF29520750073D1F88EA3687630EB4DB5E870059";
    internal const string InventorySha256 = "085E2D0DDE0B7F496FBE1E2562E410C569AD22CB44A6C4EA1C77DC0712690ED8";
    internal const int FileCount = 2348;
    internal const long TotalBytes = 435607802;
    private const int InventoryBytes = 291377;
    private static readonly (string Name, long Bytes, string Sha256)[] HostDependencies =
    [
        ("msvcp140.dll", 643512, "7C26614E1D733892C2DEAC7E245CE115504B1D80592DD0A01B08E3E5A55F89CA"),
        ("vcruntime140.dll", 178616, "D1F4225DF2CD877DBF130D5668A021DCE3F94118455FF5EC952061C30AFC9CE7"),
        ("vcruntime140_1.dll", 50112, "A7146C08F89FE5B04541AB507CDB59FF7B44534D4BA3C668A426C6450A03434E")
    ];
    private readonly List<FileStream> _files = [];
    private readonly OfficeDirectoryLeases _directories = new();
    private readonly HashSet<string> _expectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _hostDirectory;
    private readonly string _inventoryPath;
    // The empty package-cache tree is part of the source-built payload;
    // the file inventory alone cannot represent these directories.
    private readonly HashSet<string> _expectedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "share/uno_packages", "share/uno_packages/cache", "share/uno_packages/cache/uno_packages" };
    private bool _disposed;

    private OfficeRuntimeLease(string executable, string directory, string inventory)
    {
        Executable = executable;
        DirectoryPath = directory;
        ProgramDirectory = Path.Combine(directory, "program");
        _hostDirectory = Path.GetDirectoryName(executable)!;
        _inventoryPath = inventory;
    }

    internal string Executable { get; }
    internal string DirectoryPath { get; }
    internal string ProgramDirectory { get; }

    internal static async Task<OfficeRuntimeLease> OpenAsync(string executable, string directory, string inventory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lease = new OfficeRuntimeLease(OfficeDirectoryLeases.LocalPath(executable), OfficeDirectoryLeases.LocalPath(directory),
            OfficeDirectoryLeases.LocalPath(inventory));
        try
        {
            lease._directories.Open(lease.DirectoryPath);
            await lease.OpenPinnedAsync(lease.Executable, 112640, HostSha256, cancellationToken);
            var manifest = await lease.OpenPinnedAsync(lease._inventoryPath, InventoryBytes, InventorySha256, cancellationToken);
            foreach (var dependency in HostDependencies)
                await lease.OpenPinnedAsync(Path.Combine(lease._hostDirectory, dependency.Name), dependency.Bytes, dependency.Sha256, cancellationToken);
            lease.VerifyHostMembership(cancellationToken);
            manifest.Position = 0;
            using var reader = new StreamReader(manifest, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);
            long bytes = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length != 3 || !RelativePath(parts[0]) ||
                    !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0 ||
                    parts[2].Length != 64 || !parts[2].All(char.IsAsciiHexDigit) ||
                    !lease._expectedFiles.Add(parts[0]) || lease._expectedFiles.Count > FileCount)
                    throw new InvalidDataException("Invalid pinned Office inventory.");
                bytes = checked(bytes + size);
                var path = Path.Combine(lease.DirectoryPath, parts[0].Replace('/', '\\'));
                for (var parent = Path.GetDirectoryName(path); parent != lease.DirectoryPath; parent = Path.GetDirectoryName(parent!))
                    lease._expectedDirectories.Add(Path.GetRelativePath(lease.DirectoryPath, parent!).Replace('\\', '/'));
                await lease.OpenPinnedAsync(path, size, parts[2], cancellationToken);
            }
            if (lease._expectedFiles.Count != FileCount || bytes != TotalBytes)
                throw new InvalidDataException("Incomplete pinned Office inventory.");
            foreach (var relative in lease._expectedDirectories)
                lease._directories.Open(Path.Combine(lease.DirectoryPath, relative.Replace('/', '\\')));
            lease.VerifyMembership(cancellationToken);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal void VerifyMembership(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyHostMembership(cancellationToken);
        CheckMembership(DirectoryPath, _expectedFiles, _expectedDirectories, cancellationToken);
    }

    private void VerifyHostMembership(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetDirectoryName(_inventoryPath), _hostDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Keep the pinned Office host and inventory in the same directory.");
        var expected = new HashSet<string>([Executable, _inventoryPath], StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in HostDependencies) expected.Add(Path.Combine(_hostDirectory, dependency.Name));
        var runtimeChild = string.Equals(Path.GetDirectoryName(DirectoryPath), _hostDirectory, StringComparison.OrdinalIgnoreCase);
        var sawRuntime = false;
        foreach (var path in Directory.EnumerateFileSystemEntries(_hostDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked Office host entries are not supported.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!runtimeChild || !string.Equals(path, DirectoryPath, StringComparison.OrdinalIgnoreCase) || sawRuntime)
                    throw new InvalidDataException("Unexpected Office host directory.");
                sawRuntime = true;
            }
            else if (!expected.Remove(path))
                throw new InvalidDataException("Unexpected Office host file.");
        }
        if (expected.Count != 0 || sawRuntime != runtimeChild)
            throw new InvalidDataException("Office host membership changed.");
    }

    internal static void CheckMembership(string root, IReadOnlySet<string> expectedFiles, IReadOnlySet<string> expectedDirectories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked Office runtime entries are not supported.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!expectedDirectories.Contains(relative) || !directories.Add(relative))
                        throw new InvalidDataException("Unexpected Office runtime directory.");
                    pending.Push(path);
                }
                else if (!expectedFiles.Contains(relative) || !files.Add(relative))
                    throw new InvalidDataException("Unexpected Office runtime file.");
            }
        if (!files.SetEquals(expectedFiles) || !directories.SetEquals(expectedDirectories))
            throw new InvalidDataException("Office runtime membership changed.");
    }

    private async Task<FileStream> OpenPinnedAsync(string path, long size, string hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var stream = _directories.OpenFileRead(path);
        _files.Add(stream);
        if (stream.Length != size || Convert.ToHexString(await SHA256.HashDataAsync(stream, token)) != hash)
            throw new InvalidDataException("Office runtime file differs from its pinned identity.");
        return stream;
    }

    private static bool RelativePath(string path) => path.Length is > 0 and < 240 &&
        path.IndexOfAny(['\\', ':', '\0', '\r', '\n']) < 0 &&
        path.Split('/').All(part => part.Length != 0 && !part.EndsWith(' ') && !part.EndsWith('.'));

    public void Dispose()
    {
        if (_disposed) return;
        for (var index = _files.Count - 1; index >= 0; index--) _files[index].Dispose();
        _directories.Dispose();
        _files.Clear(); _disposed = true;
    }
}
