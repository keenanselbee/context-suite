using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ContextSuite.Core.Audio;

public sealed record WwiseInstallation(string ConsolePath, Version ProductVersion, string ExecutableSha256)
{
    public const long MaximumExecutableBytes = 256L * 1024 * 1024;
    public const int MaximumCandidates = 64;
    private const int MaximumRoots = 8;
    private const int MaximumRootEntries = 64;
    private const int MaximumDirectoriesPerRoot = 256;

    // A path, version and hash identify the inspected installation at this
    // moment. They do not establish vendor authenticity or game compatibility.
    public static async Task<WwiseInstallation> InspectAsync(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var consolePath = ResolveConsolePath(path);
        if (!File.Exists(consolePath))
            throw new FileNotFoundException("WwiseConsole.exe was not found in the selected Wwise installation.", consolePath);
        await using var source = new FileStream(consolePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length is < 128 or > MaximumExecutableBytes)
            throw new InvalidDataException("WwiseConsole.exe is empty or exceeds the inspection size limit.");

        var dos = new byte[64];
        await source.ReadExactlyAsync(dos, token);
        if (!dos.AsSpan(0, 2).SequenceEqual("MZ"u8))
            throw new InvalidDataException("WwiseConsole.exe is not a PE executable.");
        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(0x3c));
        if (peOffset is < 64 or > 4096 || peOffset > source.Length - 26)
            throw new InvalidDataException("WwiseConsole.exe has an invalid PE header offset.");
        source.Position = peOffset;
        var pe = new byte[26];
        await source.ReadExactlyAsync(pe, token);
        if (!pe.AsSpan(0, 4).SequenceEqual("PE\0\0"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4)) != 0x8664 ||
            BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(20)) < 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(24)) != 0x20b)
            throw new NotSupportedException("WwiseConsole.exe must be a native x64 PE executable.");

        token.ThrowIfCancellationRequested();
        var version = FileVersionInfo.GetVersionInfo(consolePath);
        if (!string.Equals(version.ProductName, "Wwise", StringComparison.OrdinalIgnoreCase) ||
            version.CompanyName?.StartsWith("Audiokinetic", StringComparison.OrdinalIgnoreCase) != true ||
            !string.Equals(version.FileDescription, "WwiseConsole", StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(version.ProductVersion, out var productVersion) ||
            productVersion.Major < 1)
            throw new NotSupportedException("WwiseConsole.exe lacks the expected Audiokinetic Wwise product identity.");

        source.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token));
        return new(consolePath, productVersion, hash);
    }

    public static string ResolveConsolePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Select a Wwise installation or WwiseConsole.exe.", nameof(path));
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            !char.IsAsciiLetter(path[0]) || path.Length < 3 || path[1] != ':' || path[2] is not ('\\' or '/') ||
            path.AsSpan(2).Contains(':') || path.Any(character => char.IsControl(character) || character is '<' or '>' or '"' or '|' or '?' or '*') ||
            path.Replace('/', '\\').Split('\\').Skip(1).Any(part => part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')))
            throw new ArgumentException("Select a fully qualified local Wwise installation path without ambiguous segments.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        string consolePath;
        if (name.Equals("WwiseConsole.exe", StringComparison.OrdinalIgnoreCase))
            consolePath = fullPath;
        else if (name.Equals("bin", StringComparison.OrdinalIgnoreCase))
            consolePath = Path.Combine(fullPath, "WwiseConsole.exe");
        else if (name.Equals("Authoring", StringComparison.OrdinalIgnoreCase))
            consolePath = Path.Combine(fullPath, "x64", "Release", "bin", "WwiseConsole.exe");
        else if (Directory.Exists(Path.Combine(fullPath, "Authoring")))
            consolePath = Path.Combine(fullPath, "Authoring", "x64", "Release", "bin", "WwiseConsole.exe");
        else
            throw new ArgumentException("Select a Wwise installation, its Authoring x64 Release bin folder, or WwiseConsole.exe.", nameof(path));

        var bin = Path.GetDirectoryName(consolePath)!;
        var release = Directory.GetParent(bin);
        var architecture = release?.Parent;
        var authoring = architecture?.Parent;
        var installation = authoring?.Parent;
        if (!Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(release?.Name, "Release", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(architecture?.Name, "x64", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(authoring?.Name, "Authoring", StringComparison.OrdinalIgnoreCase) ||
            installation is null)
            throw new ArgumentException("WwiseConsole.exe must be in an Authoring/x64/Release/bin installation.", nameof(path));
        return consolePath;
    }

    public static async Task<IReadOnlyList<WwiseInstallation>> DiscoverAsync(
        IEnumerable<string>? audiokineticRoots = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var rootEntries = new List<string>();
        foreach (var root in audiokineticRoots ?? StandardRoots())
        {
            token.ThrowIfCancellationRequested();
            if (rootEntries.Count == MaximumRootEntries)
                throw new ArgumentException("Wwise discovery accepts at most 64 root entries.", nameof(audiokineticRoots));
            rootEntries.Add(root);
        }
        var roots = rootEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length > MaximumRoots)
            throw new ArgumentException("Wwise discovery accepts at most eight Audiokinetic roots.", nameof(audiokineticRoots));
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            string[] directories;
            try
            {
                directories = Directory.EnumerateDirectories(root).Take(MaximumDirectoriesPerRoot + 1).ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (directories.Length > MaximumDirectoriesPerRoot)
                throw new InvalidDataException("An Audiokinetic root exceeds the bounded directory scan limit.");
            candidates.AddRange(directories.Where(directory =>
                Path.GetFileName(directory).StartsWith("Wwise", StringComparison.OrdinalIgnoreCase)));
            if (candidates.Count > MaximumCandidates)
                throw new InvalidDataException("Wwise discovery exceeds its 64-installation candidate limit.");
        }
        var installations = new List<WwiseInstallation>();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try { installations.Add(await InspectAsync(candidate, token)); }
            catch (OperationCanceledException) { throw; }
            catch (ArgumentException) { }
            catch (FileNotFoundException) { }
            catch (InvalidDataException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (Win32Exception) { }
        }
        return installations.OrderByDescending(item => item.ProductVersion)
            .ThenBy(item => item.ConsolePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> StandardRoots()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var programFiles = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, "Audiokinetic");
        }
    }
}
