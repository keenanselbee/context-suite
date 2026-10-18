using System.Text.Json;

namespace ContextSuite.Core.Installation;

// Installation selection limits entry points, not shared probes used by other tools.
public sealed class InstalledTools
{
    public const string FileName = "installation-tools.json";
    public static InstalledTools All { get; } = new(true, true, true);
    public bool Analyze { get; }
    public bool Convert { get; }
    public bool Optimize { get; }

    private InstalledTools(bool analyze, bool convert, bool optimize)
    {
        Analyze = analyze;
        Convert = convert;
        Optimize = optimize;
    }

    public bool IncludesOperation(string operation) => operation switch
    {
        "analyze" => Analyze,
        "convert" => Convert,
        "optimize" => Optimize,
        _ => false
    };

    public static string UnavailableMessage(string operation)
    {
        var name = operation switch { "analyze" => "Analyze", "convert" => "Convert", "optimize" => "Optimize", _ => "This tool" };
        return $"{name} is not installed. Select this tool when installing Context Suite.";
    }

    public static InstalledTools Load(string applicationDirectory)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(applicationDirectory, FileName));
            if (stream.Length is 0 or > 4096) throw new InvalidDataException("Invalid selection length.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            using var document = JsonDocument.Parse(bytes.AsMemory(offset), new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a selection object.");
            var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (properties.Length != 2 || !properties.Contains("schema") || !properties.Contains("tools") ||
                root.GetProperty("schema").ValueKind != JsonValueKind.Number ||
                !root.GetProperty("schema").TryGetInt32(out var schema) || schema != 1)
                throw new InvalidDataException("Unsupported selection schema.");
            var selected = root.GetProperty("tools");
            if (selected.ValueKind != JsonValueKind.Array || selected.GetArrayLength() is < 1 or > 3)
                throw new InvalidDataException("Select at least one tool.");
            var tools = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in selected.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not ("Analyze" or "Convert" or "Optimize") ||
                    !tools.Add(item.GetString()!))
                    throw new InvalidDataException("Unknown or repeated tool.");
            }
            return new(tools.Contains("Analyze"), tools.Contains("Convert"), tools.Contains("Optimize"));
        }
        catch (FileNotFoundException) { return All; } // Legacy installations and development builds have no marker.
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new InstallationToolsException("Context Suite could not read its installed tool selection. The installation files need attention. Reinstall Context Suite.", error);
        }
    }
}
