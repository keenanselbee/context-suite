using System.Security.AccessControl;
using System.Security.Principal;
using ContextSuite.Core.Activation;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal static class ActivationStore
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "Prototype", "Activations");

    public static async Task<OperationRequest> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(full), "D", out var fileId) ||
            Path.GetExtension(full) != ".request")
            throw new InvalidDataException("The activation file is outside Context Suite's request directory.");
        RejectReparsePoints(full);
        using var identity = WindowsIdentity.GetCurrent();
        var directory = new DirectoryInfo(DirectoryPath);
        var security = new DirectorySecurity();
        security.SetOwner(identity.User!);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous);
        if (stream.Length is < 1 or > ActivationParser.MaximumBytes)
            throw new InvalidDataException("The activation file size is invalid.");
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var request = ActivationParser.Parse(bytes);
        if (request.RequestId != fileId) throw new InvalidDataException("The request filename does not match its identifier.");
        return request;
    }

    public static void Acknowledge(string path)
    {
        // Only called after ReadAsync and a positive queue acknowledgement.
        RejectReparsePoints(path);
        File.Delete(path);
    }

    public static void CleanupStale(string directory)
    {
        if (!Directory.Exists(directory)) return;
        RejectReparsePoints(directory);
        foreach (var path in Directory.EnumerateFiles(directory).Take(256))
        {
            if ((Path.GetExtension(path) is ".request" or ".tmp") &&
                Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out _) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void RejectReparsePoints(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed in activation storage.");
    }
}
