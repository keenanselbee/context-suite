using System.Text.Json;

namespace ContextSuite.Private.IO;

// Explicit recipient opt-in for compatible rebuilt libraries. Executable/bridge
// identities and the application's media policies remain selected by the defaults.
internal static class LocalLibraryManifest
{
    internal const string FileName = "ContextSuite.LocalLibraries.json";
    private const int MaximumBytes = 65536;

    internal static IReadOnlyDictionary<string, string> Read(string directory, string anchor,
        IReadOnlyDictionary<string, string> defaults, RuntimeDirectoryLeases directories, List<FileStream> leases)
    {
        FileStream stream;
        try { stream = directories.OpenFileRead(Path.Combine(directory, FileName)); }
        catch (FileNotFoundException) { return defaults; }
        leases.Add(stream);
        if (stream.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("The local library manifest exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Parse(bytes, anchor, defaults);
    }

    internal static IReadOnlyDictionary<string, string> Parse(ReadOnlyMemory<byte> bytes, string anchor,
        IReadOnlyDictionary<string, string> defaults)
    {
        if (bytes.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("The local library manifest exceeds its size limit.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!fields.Add(property.Name)) throw Invalid();
            if (!fields.SetEquals(["formatVersion", "anchorName", "anchorSha256", "libraries"]) ||
                root.GetProperty("formatVersion").ValueKind != JsonValueKind.Number ||
                !root.GetProperty("formatVersion").TryGetInt32(out var version) || version != 1 ||
                root.GetProperty("anchorName").ValueKind != JsonValueKind.String ||
                root.GetProperty("anchorName").GetString() != anchor ||
                Hash(root.GetProperty("anchorSha256")) != defaults[anchor]) throw Invalid();
            var libraries = root.GetProperty("libraries");
            if (libraries.ValueKind != JsonValueKind.Object) throw Invalid();
            var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [anchor] = defaults[anchor] };
            foreach (var property in libraries.EnumerateObject())
            {
                var name = property.Name;
                if (selected.Count >= 33 || name.Length is < 5 or > 128 ||
                    !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name.Any(value => !(char.IsAsciiLetterOrDigit(value) || value is '.' or '-' or '_')) ||
                    name[0] == '.' || name.Contains("..", StringComparison.Ordinal) ||
                    !selected.TryAdd(name, Hash(property.Value))) throw Invalid();
            }
            if (defaults.Keys.Any(name => !selected.ContainsKey(name))) throw Invalid();
            return selected;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The local library manifest is not valid JSON.", error);
        }
    }

    private static string Hash(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) throw Invalid();
        var value = element.GetString()!;
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character))) throw Invalid();
        return value.ToUpperInvariant();
    }

    private static InvalidDataException Invalid() => new(
        "The local library manifest does not match this engine. Restore the original libraries or recreate the manifest for compatible replacements.");
}
