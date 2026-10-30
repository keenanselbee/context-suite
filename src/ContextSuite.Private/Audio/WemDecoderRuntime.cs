using System.Security.Cryptography;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// A fixed two-file evaluation runtime. The handles prevent ordinary replacement
// while the native child runs; membership is checked again after execution.
internal sealed class WemDecoderRuntime : IDisposable
{
    internal const string Identity = "vgmstream r2117 WEM-only; vorbis-1.3.7";
    private const string ExecutableHash = "CA9D68C1ACFBB865B6118A4043599EFDC97E62CCC929C6E712008419AA00D818";
    private const string VorbisHash = "5F330A20B19B3C70DC43EEDCC95891E65940FCA04A77155C573EFCDD4C2CA665";
    private readonly RuntimeDirectoryLeases _directories = new();
    private readonly List<FileStream> _files = [];
    private readonly string _directory;
    private bool _disposed;

    internal WemDecoderRuntime(string binaryDirectory)
    {
        _directory = RuntimeDirectoryLeases.LocalPath(binaryDirectory);
        Executable = Path.Combine(_directory, "vgmstream-cli.exe");
        try
        {
            _directories.Open(_directory);
            Pin("vgmstream-cli.exe", 1623552, ExecutableHash);
            Pin("libvorbis.dll", 287232, VorbisHash);
            VerifyMembership();
        }
        catch { Dispose(); throw; }
    }

    internal string Executable { get; }

    internal void VerifyMembership()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var expected = new HashSet<string>(["vgmstream-cli.exe", "libvorbis.dll"], StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFileSystemEntries(_directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("WEM decoder runtime membership changed.");
            if (string.Equals(Path.GetFileName(path), "provenance", StringComparison.OrdinalIgnoreCase) &&
                (attributes & FileAttributes.Directory) != 0) continue;
            if ((attributes & FileAttributes.Directory) != 0 || !expected.Remove(Path.GetFileName(path)))
                throw new InvalidDataException("WEM decoder runtime membership changed.");
        }
        if (expected.Count != 0) throw new InvalidDataException("WEM decoder runtime is incomplete.");
    }

    private void Pin(string name, long size, string sha256)
    {
        var stream = _directories.OpenFileRead(Path.Combine(_directory, name));
        _files.Add(stream);
        if (stream.Length != size || Convert.ToHexString(SHA256.HashData(stream)) != sha256)
            throw new InvalidDataException("WEM decoder identity mismatch.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var file in _files) file.Dispose();
        _files.Clear(); _directories.Dispose();
    }
}
