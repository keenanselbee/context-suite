using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Activation;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.TestHost;

// Test-host only: copy reviewed scratch fixtures before a simulated-access UI run.
internal sealed class ManualWorkflowSelection : IDisposable
{
    private sealed record Input(string Path, string Sha256, bool Locked = false);
    private sealed record Specification(string Operation, string Action, Input[] Files);
    private readonly Specification _specification;
    private readonly byte[][] _contents;
    private readonly List<FileStream> _locks = [];

    private ManualWorkflowSelection(Specification specification, byte[][] contents)
    {
        _specification = specification;
        _contents = contents;
    }

    internal static ManualWorkflowSelection Read(string path)
    {
        var specification = JsonSerializer.Deserialize<Specification>(ReadLocal(path, 64 * 1024),
            new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        if (specification?.Files is not { Length: > 0 and <= 32 } ||
            string.IsNullOrWhiteSpace(specification.Operation) || string.IsNullOrWhiteSpace(specification.Action))
            throw new InvalidDataException("A manual selection needs one command and between one and 32 reviewed inputs.");
        if (specification.Files.Any(input => input.Locked) && specification.Operation != "analyze")
            throw new InvalidDataException("An unreadable-input control is supported only for Analyze.");
        var contents = new List<byte[]>();
        long total = 0;
        foreach (var input in specification.Files)
        {
            var bytes = ReadLocal(input.Path, 64 * 1024 * 1024);
            total += bytes.Length;
            if (total > 256 * 1024 * 1024 || Convert.ToHexString(SHA256.HashData(bytes)) != input.Sha256)
                throw new InvalidDataException("Manual input identity or total size differs from the reviewed selection.");
            contents.Add(bytes);
        }
        new OperationRequest(Guid.NewGuid(), specification.Operation, specification.Action,
            specification.Files.Select(input => Path.GetFullPath(input.Path)).ToImmutableArray()).Validate();
        return new(specification, contents.ToArray());
    }

    internal string CreateRequest(string root, Guid id)
    {
        var directory = Path.Combine(root, "Inputs");
        if (Directory.Exists(directory)) throw new IOException("Do not reuse manual workflow inputs.");
        Directory.CreateDirectory(directory);
        var copies = new List<string>();
        for (var index = 0; index < _specification.Files.Length; index++)
        {
            var input = _specification.Files[index];
            var path = Path.Combine(directory, $"{index + 1:D2} - {Path.GetFileName(input.Path)}");
            using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(_contents[index]);
            copies.Add(path);
        }
        var text = $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={_specification.Operation}\naction={_specification.Action}\npathCount={copies.Count}\n" +
            string.Concat(copies.Select(path => "path=" + path + "\n"));
        ActivationParser.Parse(Encoding.UTF8.GetBytes(text));
        File.WriteAllText(Path.Combine(root, "manual-inputs.json"), JsonSerializer.Serialize(new
        {
            _specification.Operation, _specification.Action,
            Inputs = _specification.Files.Select((input, index) => new { Original = input.Path, Copy = copies[index], input.Sha256, input.Locked }),
            Scope = "Generated/reviewed scratch fixtures only. Simulated TEST-ONLY access; no installed Explorer acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        for (var index = 0; index < copies.Count; index++)
            if (_specification.Files[index].Locked)
                _locks.Add(new FileStream(copies[index], FileMode.Open, FileAccess.Read, FileShare.None));
        return text;
    }

    private static byte[] ReadLocal(string path, int maximum)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ContextSuite.Production.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Run the repository test host.");
        var full = Path.GetFullPath(path);
        var scratch = Path.Combine(repository.FullName, ".codex-temp") + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(path) || path.Split(['\\', '/']).Contains("..") ||
            path.Any(char.IsControl) || !full.StartsWith(scratch, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manual selections must use absolute repository scratch paths.");
        for (FileSystemInfo? entry = new FileInfo(full); entry is not null && entry.FullName != repository.FullName;
             entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked manual workflow inputs are refused.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Manual workflow input exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return bytes;
    }

    public void Dispose()
    {
        foreach (var stream in _locks) stream.Dispose();
        _locks.Clear();
    }
}
