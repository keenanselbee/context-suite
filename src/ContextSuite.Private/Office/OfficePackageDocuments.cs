using System.Text;
using System.Xml.Linq;

namespace ContextSuite.Private.Office;

public static partial class OfficePackageOptimizer
{
    private const string OdfManifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string OdfOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string EpubContainer = "urn:oasis:names:tc:opendocument:xmlns:container";
    private const string EpubPackage = "http://www.idpf.org/2007/opf";

    private static string IdentifyDocument(byte[] source, List<Entry> entries, CancellationToken token)
    {
        if (entries.Any(entry => entry.Name == "[Content_Types].xml")) throw new InvalidDataException("Conflicting document package families.");
        var first = entries[0];
        if (first.Name != "mimetype" || first.Method != 0 || first.Descriptor.Length != 0 || first.Expanded > 128)
            throw new NotSupportedException("Document mimetype must be the first, stored member without a descriptor.");
        var mime = new UTF8Encoding(false, true).GetString(Expand(source, first, token));
        var family = mime switch
        {
            "application/vnd.oasis.opendocument.text" => "odt",
            "application/vnd.oasis.opendocument.spreadsheet" => "ods",
            "application/vnd.oasis.opendocument.presentation" => "odp",
            "application/epub+zip" => "epub",
            _ => throw new NotSupportedException("Only ordinary ODT, ODS, ODP, and EPUB packages are supported.")
        };
        var xmlParts = new HashSet<string>(StringComparer.Ordinal);
        if (family == "epub") IdentifyEpub(source, entries, xmlParts, token);
        else IdentifyOdf(source, entries, mime, family, xmlParts, token);
        RequirePassiveDocument(source, entries, xmlParts, token);
        return family;
    }

    private static XElement DocumentXml(byte[] source, List<Entry> entries, string name, HashSet<string> xmlParts, CancellationToken token)
    {
        var entry = entries.SingleOrDefault(entry => entry.Name == name) ?? throw new InvalidDataException("Required document part is missing.");
        xmlParts.Add(name);
        return ReadXml(source, entry, token);
    }

    private static void IdentifyOdf(byte[] source, List<Entry> entries, string mime, string family, HashSet<string> xmlParts, CancellationToken token)
    {
        var manifest = DocumentXml(source, entries, "META-INF/manifest.xml", xmlParts, token);
        if (manifest.Name != XName.Get("manifest", OdfManifest)) throw new InvalidDataException("Invalid OpenDocument manifest root.");
        if (manifest.Descendants().Any(element => element.Name == XName.Get("encryption-data", OdfManifest) || element.Name == XName.Get("encrypted-key", OdfManifest)))
            throw new NotSupportedException("Encrypted OpenDocument parts cannot be optimized.");
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = 0; var contentDeclared = false;
        foreach (var file in manifest.Elements())
        {
            token.ThrowIfCancellationRequested();
            if (file.Name != XName.Get("file-entry", OdfManifest)) throw new NotSupportedException("Unsupported OpenDocument manifest declaration.");
            var path = (string?)file.Attribute(XName.Get("full-path", OdfManifest)) ?? "";
            var type = (string?)file.Attribute(XName.Get("media-type", OdfManifest)) ?? "";
            if (!declared.Add(path)) throw new InvalidDataException("Ambiguous OpenDocument manifest path.");
            if (path == "/")
            {
                if (type != mime) throw new InvalidDataException("OpenDocument root media type conflicts with mimetype.");
                roots++; continue;
            }
            RequireName(path);
            if (path.EndsWith('/')) continue;
            if (!entries.Any(entry => entry.Name == path)) throw new InvalidDataException("OpenDocument manifest names a missing part.");
            if (type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || type is "text/xml" or "application/xml") xmlParts.Add(path);
            if (path == "content.xml") { contentDeclared = true; xmlParts.Add(path); }
            if (type.Contains("script", StringComparison.OrdinalIgnoreCase) || type.Contains("basic", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Script-bearing OpenDocument packages are unsupported.");
        }
        if (roots != 1 || !contentDeclared) throw new InvalidDataException("OpenDocument main content declaration is missing.");
        var content = DocumentXml(source, entries, "content.xml", xmlParts, token);
        var bodies = content.Elements(XName.Get("body", OdfOffice)).ToArray();
        var kind = family switch { "odt" => "text", "ods" => "spreadsheet", _ => "presentation" };
        if (content.Name != XName.Get("document-content", OdfOffice) || bodies.Length != 1 || bodies[0].Elements().Count() != 1 ||
            bodies[0].Elements().Single().Name != XName.Get(kind, OdfOffice))
            throw new InvalidDataException("OpenDocument main content conflicts with its family.");
    }

    private static void IdentifyEpub(byte[] source, List<Entry> entries, HashSet<string> xmlParts, CancellationToken token)
    {
        var container = DocumentXml(source, entries, "META-INF/container.xml", xmlParts, token);
        if (container.Name != XName.Get("container", EpubContainer)) throw new InvalidDataException("Invalid EPUB container root.");
        var rootfiles = container.Elements(XName.Get("rootfiles", EpubContainer)).ToArray();
        if (rootfiles.Length != 1 || rootfiles[0].Elements().Count() != 1 || rootfiles[0].Elements().Single().Name != XName.Get("rootfile", EpubContainer))
            throw new NotSupportedException("EPUB requires one unambiguous package rootfile.");
        var rootfile = rootfiles[0].Elements().Single();
        var mainPath = (string?)rootfile.Attribute("full-path") ?? "";
        RequireName(mainPath);
        if ((string?)rootfile.Attribute("media-type") != "application/oebps-package+xml") throw new InvalidDataException("Invalid EPUB package media type.");
        var package = DocumentXml(source, entries, mainPath, xmlParts, token);
        if (package.Name != XName.Get("package", EpubPackage) || (string?)package.Attribute("version") is not ("2.0" or "3.0"))
            throw new NotSupportedException("Unsupported EPUB package declaration.");
        var manifest = Single("manifest"); var spine = Single("spine"); var metadata = Single("metadata");
        var identifier = (string?)package.Attribute("unique-identifier") ?? "";
        if (identifier.Length == 0 || metadata.Elements(XName.Get("identifier", "http://purl.org/dc/elements/1.1/")).Count(item =>
            (string?)item.Attribute("id") == identifier && !string.IsNullOrWhiteSpace(item.Value)) != 1)
            throw new InvalidDataException("EPUB unique identifier is missing or ambiguous.");
        var items = new Dictionary<string, (string Path, string Type, XElement Declaration)>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Elements())
        {
            token.ThrowIfCancellationRequested();
            if (item.Name != XName.Get("item", EpubPackage)) throw new InvalidDataException("Invalid EPUB manifest member.");
            var id = (string?)item.Attribute("id") ?? ""; var type = (string?)item.Attribute("media-type") ?? "";
            var href = (string?)item.Attribute("href") ?? "";
            var path = href.StartsWith('/') ? null : ResolveImageTarget(PartRelationships(mainPath), href);
            if (id.Length == 0 || type.Length == 0 || path is null || !paths.Add(path) || !entries.Any(entry => entry.Name == path) ||
                !items.TryAdd(id, (path, type, item))) throw new InvalidDataException("EPUB manifest part is missing, ambiguous, or nonlocal.");
            if (type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || type is "text/xml" or "application/xml") xmlParts.Add(path);
            if (type.Contains("javascript", StringComparison.OrdinalIgnoreCase) || type.Contains("ecmascript", StringComparison.OrdinalIgnoreCase) ||
                Tokens((string?)item.Attribute("properties")).Contains("scripted")) throw new NotSupportedException("Scripted EPUB content is unsupported.");
        }
        foreach (var item in items.Values)
            foreach (var attribute in new[] { "fallback", "media-overlay" })
                if ((string?)item.Declaration.Attribute(attribute) is { } reference && !items.ContainsKey(reference))
                    throw new InvalidDataException("EPUB manifest reference is unresolved.");
        var chapters = spine.Elements().ToArray();
        if (chapters.Length == 0) throw new InvalidDataException("EPUB spine is empty.");
        foreach (var chapter in chapters)
        {
            if (chapter.Name != XName.Get("itemref", EpubPackage) || !items.TryGetValue((string?)chapter.Attribute("idref") ?? "", out var item) ||
                item.Type is not ("application/xhtml+xml" or "image/svg+xml")) throw new NotSupportedException("EPUB spine needs a supported local content document.");
            var content = DocumentXml(source, entries, item.Path, xmlParts, token);
            if (content.Name != (item.Type == "image/svg+xml" ? XName.Get("svg", "http://www.w3.org/2000/svg") : XName.Get("html", "http://www.w3.org/1999/xhtml")))
                throw new InvalidDataException("EPUB spine content conflicts with its media type.");
        }
        var toc = (string?)spine.Attribute("toc");
        if ((string?)package.Attribute("version") == "2.0" && string.IsNullOrEmpty(toc))
            throw new InvalidDataException("EPUB 2 requires its NCX navigation reference.");
        if (toc is not null)
        {
            if (!items.TryGetValue(toc, out var tocItem) || tocItem.Type != "application/x-dtbncx+xml")
                throw new InvalidDataException("EPUB navigation reference is invalid.");
            var ncx = DocumentXml(source, entries, tocItem.Path, xmlParts, token);
            if (ncx.Name != XName.Get("ncx", "http://www.daisy.org/z3986/2005/ncx/"))
                throw new InvalidDataException("EPUB NCX navigation content is invalid.");
        }
        if ((string?)package.Attribute("version") == "3.0")
        {
            var navigation = items.Values.Where(item => Tokens((string?)item.Declaration.Attribute("properties")).Contains("nav")).ToArray();
            if (navigation.Length != 1 || navigation[0].Type != "application/xhtml+xml") throw new InvalidDataException("EPUB navigation document is missing or ambiguous.");
            var nav = DocumentXml(source, entries, navigation[0].Path, xmlParts, token);
            if (nav.Name != XName.Get("html", "http://www.w3.org/1999/xhtml")) throw new InvalidDataException("EPUB navigation content is invalid.");
        }

        XElement Single(string name)
        {
            var matches = package.Elements(XName.Get(name, EpubPackage)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Required EPUB package declaration is missing or ambiguous.");
            return matches[0];
        }
    }

    private static string PartRelationships(string part)
    {
        var slash = part.LastIndexOf('/');
        return slash < 0 ? "_rels/" + part + ".rels" : part[..(slash + 1)] + "_rels/" + part[(slash + 1)..] + ".rels";
    }

    private static string[] Tokens(string? value) => (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static void RequirePassiveDocument(byte[] source, List<Entry> entries, HashSet<string> xmlParts, CancellationToken token)
    {
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var segments = entry.Name.Split('/');
            if (segments.Any(segment => segment.Equals("Basic", StringComparison.OrdinalIgnoreCase) || segment.Equals("Scripts", StringComparison.OrdinalIgnoreCase)) ||
                Path.GetExtension(entry.Name).ToLowerInvariant() is ".js" or ".mjs" or ".vbs" ||
                entry.Name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) &&
                (entry.Name.Contains("signature", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith("encryption.xml", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith("rights.xml", StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("Encrypted, signed, or script-bearing document packages are unsupported.");
            if (!xmlParts.Contains(entry.Name) && Path.GetExtension(entry.Name).ToLowerInvariant() is not (".xml" or ".xhtml" or ".html" or ".opf" or ".ncx" or ".svg")) continue;
            var xml = ReadXml(source, entry, token);
            foreach (var element in xml.DescendantsAndSelf())
            {
                token.ThrowIfCancellationRequested();
                var name = element.Name.LocalName;
                if (name is "encryption-data" or "encrypted-key" or "Signature" or "EncryptedData" or "EncryptionMethod" or
                    "script" or "event-listener" || name == "scripts" && element.HasElements)
                    throw new NotSupportedException("Protected, signed, encrypted, or scripted document content is unsupported.");
                foreach (var attribute in element.Attributes())
                    if (attribute.Name.LocalName.Contains("protection-key", StringComparison.OrdinalIgnoreCase) ||
                        attribute.Name.LocalName is "protected" or "structure-protected" && attribute.Value is not ("false" or "0") ||
                        attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) &&
                        element.Name.NamespaceName is "http://www.w3.org/1999/xhtml" or "http://www.w3.org/2000/svg" ||
                        attribute.Value.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Protected or scripted document declarations are unsupported.");
            }
        }
    }
}
