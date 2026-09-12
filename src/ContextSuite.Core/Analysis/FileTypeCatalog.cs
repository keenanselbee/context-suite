using System.Collections.Immutable;
using System.Text.Json;

namespace ContextSuite.Core.Analysis;

public sealed record CatalogMimeType(string Value, string Source);

public sealed record FileTypeDescription(string Id, string Name, string Family, string CommonUses,
    ImmutableArray<string> Extensions, string Source, bool TextCompatible = false,
    ImmutableArray<string> FileNames = default)
{
    public ImmutableArray<CatalogMimeType> MimeTypes { get; init; } = [];
}

public sealed record FileTypeCatalog(int SchemaVersion, string Revision, ImmutableArray<FileTypeDescription> Types)
{
    private static readonly Lazy<FileTypeCatalog> Packaged = new(Load);
    public static FileTypeCatalog Default => Packaged.Value;

    public FileTypeDescription Get(string id) => Types.First(type => type.Id == id);

    public ImmutableArray<FileTypeDescription> FindByName(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var exact = Types.Where(type => !type.FileNames.IsDefault && type.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToImmutableArray();
        if (!exact.IsEmpty) return exact;
        var suffixes = Types.SelectMany(type => type.Extensions.Select(extension => (Type: type, Extension: extension)))
            .Where(item => name.EndsWith(item.Extension, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (suffixes.Length == 0) return [];
        var longest = suffixes.Max(item => item.Extension.Length);
        return suffixes.Where(item => item.Extension.Length == longest).Select(item => item.Type).Distinct().ToImmutableArray();
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Revision) || Types.IsDefaultOrEmpty || Types.Length > 4096)
            throw new InvalidDataException("Unsupported file-type catalog.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in Types)
        {
            if (type is null || string.IsNullOrWhiteSpace(type.Id) || !ids.Add(type.Id) ||
                type.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
                string.IsNullOrWhiteSpace(type.Name) || type.Name.Length > 100 || string.IsNullOrWhiteSpace(type.Family) ||
                string.IsNullOrWhiteSpace(type.CommonUses) || type.CommonUses.Length > 512 || type.Extensions.IsDefault ||
                type.Extensions.Any(extension => extension is null || extension.Length < 2 || extension[0] != '.' ||
                    extension.Any(character => character != '.' && !char.IsAsciiLetterOrDigit(character))) ||
                type.Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != type.Extensions.Length ||
                (!type.FileNames.IsDefault && (type.FileNames.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 255 ||
                    name.IndexOfAny(['/', '\\', ':', '\0', '\r', '\n']) >= 0) ||
                    type.FileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != type.FileNames.Length)) ||
                !Uri.TryCreate(type.Source, UriKind.Absolute, out var source) || source.Scheme != "https")
                throw new InvalidDataException("Invalid file-type catalog entry.");
            if (type.MimeTypes.IsDefault || type.MimeTypes.Length > 16 ||
                type.MimeTypes.Any(mime => mime is null || !ValidMimeType(mime.Value) ||
                    !Uri.TryCreate(mime.Source, UriKind.Absolute, out var reference) || reference.Scheme != "https") ||
                type.MimeTypes.Select(mime => mime.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != type.MimeTypes.Length)
                throw new InvalidDataException("Invalid catalog MIME description.");
        }
    }

    private static bool ValidMimeType(string? value)
    {
        // Canonical, parameter-free registry names; not an HTTP Content-Type parser.
        if (value is null || value.Length > 255) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is > 0 and <= 127 &&
            char.IsAsciiLetterOrDigit(part[0]) && part.All(character =>
                char.IsAsciiDigit(character) || character is >= 'a' and <= 'z' || "!#$&-^_.+".Contains(character)));
    }

    private static FileTypeCatalog Load()
    {
        using var stream = typeof(FileTypeCatalog).Assembly.GetManifestResourceStream("ContextSuite.Core.Analysis.file-types.json")
            ?? throw new InvalidDataException("The packaged file-type catalog is missing.");
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("The file-type catalog exceeds its size limit.");
        var catalog = JsonSerializer.Deserialize<FileTypeCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
            ?? throw new InvalidDataException("The file-type catalog is empty.");
        catalog.Validate();
        return catalog;
    }
}
