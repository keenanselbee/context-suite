using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public static partial class DocumentAnalysis
{
    private static async Task<(string Names, string Themes, int Parts)> ReadFontReferencesAsync(
        DocumentPackageReader package, CancellationToken token)
    {
        var types = ParseXml(await package.ReadPartAsync("[Content_Types].xml"), token);
        RequireRoot(types, ContentTypes, "Types");
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in types.Elements(XName.Get("Override", ContentTypes)))
        {
            var type = (string?)declaration.Attribute("ContentType") ?? "";
            if (!type.EndsWith("+xml", StringComparison.Ordinal) ||
                !(type.StartsWith("application/vnd.openxmlformats-officedocument.wordprocessingml.", StringComparison.Ordinal) ||
                  type.StartsWith("application/vnd.openxmlformats-officedocument.spreadsheetml.", StringComparison.Ordinal) ||
                  type.StartsWith("application/vnd.openxmlformats-officedocument.presentationml.", StringComparison.Ordinal) ||
                  type.StartsWith("application/vnd.ms-word.", StringComparison.Ordinal) ||
                  type.StartsWith("application/vnd.ms-excel.", StringComparison.Ordinal) ||
                  type.StartsWith("application/vnd.ms-powerpoint.", StringComparison.Ordinal) ||
                  type is "application/vnd.openxmlformats-officedocument.theme+xml" or "application/vnd.openxmlformats-officedocument.themeOverride+xml")) continue;
            var name = (string?)declaration.Attribute("PartName") ?? "";
            if (!name.StartsWith('/') || name.Length < 2 || name.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0 ||
                name[1..].Split('/').Any(segment => segment is "" or "." or "..") || !parts.Add(name[1..]) || parts.Count > 32)
                throw new InvalidDataException("Ambiguous or oversized font declaration part selection.");
        }
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var themes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var part in parts.Order(StringComparer.Ordinal))
        {
            var root = ParseXml(await package.ReadPartAsync(part), token);
            foreach (var element in root.DescendantsAndSelf())
            {
                token.ThrowIfCancellationRequested();
                var ns = element.Name.NamespaceName;
                var local = element.Name.LocalName;
                if (ns is "http://schemas.openxmlformats.org/wordprocessingml/2006/main" or "http://purl.oclc.org/ooxml/wordprocessingml/main")
                {
                    if (local == "rFonts")
                    {
                        foreach (var attr in new[] { "ascii", "hAnsi", "eastAsia", "cs" }) Add(names, (string?)element.Attribute(XName.Get(attr, ns)));
                        foreach (var attr in new[] { "asciiTheme", "hAnsiTheme", "eastAsiaTheme", "cstheme" }) Add(themes, (string?)element.Attribute(XName.Get(attr, ns)));
                    }
                    else if (local == "font") Add(names, (string?)element.Attribute(XName.Get("name", ns)));
                }
                else if (ns is "http://schemas.openxmlformats.org/spreadsheetml/2006/main" or "http://purl.oclc.org/ooxml/spreadsheetml/main")
                {
                    if (local == "name" && element.Parent?.Name == XName.Get("font", ns) ||
                        local == "rFont" && element.Parent?.Name == XName.Get("rPr", ns)) Add(names, (string?)element.Attribute("val"));
                    else if (local == "scheme" && element.Parent?.Name == XName.Get("font", ns)) Add(themes, (string?)element.Attribute("val"));
                }
                else if (ns is "http://schemas.openxmlformats.org/drawingml/2006/main" or "http://purl.oclc.org/ooxml/drawingml/main")
                {
                    if (local is "latin" or "ea" or "cs" or "sym" or "font")
                    {
                        var name = (string?)element.Attribute("typeface");
                        Add(name?.StartsWith('+') == true ? themes : names, name);
                    }
                }
            }
        }
        var formatting = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
        return (names.Count == 0 ? "None observed in selected parts" : JsonSerializer.Serialize(names, formatting),
            themes.Count == 0 ? "None observed in selected parts" : JsonSerializer.Serialize(themes, formatting), parts.Count);

        void Add(SortedSet<string> values, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (value.Length > 128 || value.Any(char.IsControl) || !value.Any(character => !char.IsWhiteSpace(character)))
                throw new InvalidDataException("Unsupported font declaration value.");
            values.Add(value);
            if (names.Count + themes.Count > 64) throw new InvalidDataException("Font declaration count exceeds the analysis limit.");
        }
    }
}
