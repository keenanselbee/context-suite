using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

// Font-family selections for supported final-text runs in the main Word story.
// This is source evidence, not installed/glyph/embedding or rendered-font proof.
public sealed partial record WordBodyFontInspection(bool Available, int TextRuns, int ResolvedRuns,
    ImmutableArray<string> Families, ImmutableArray<string> CoverageIssues, int InspectedBytes,
    ImmutableArray<string> InactiveRevisionFamilies = default)
{
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string RelationPrefix = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string WordType = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
    private static readonly XNamespace W = Word;

    public static async Task<WordBodyFontInspection> ReadAsync(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Use a readable, seekable source lease.", nameof(input));
        var position = input.Position;
        var package = new DocumentPackageReader(input, 0, token);
        try
        {
            await package.InitializeAsync();
            var types = Parse(await package.ReadPartAsync("[Content_Types].xml"), token);
            if (types.Name != XName.Get("Types", ContentTypes)) throw new InvalidDataException("Unexpected content types.");
            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            var knownDependencies = true;
            foreach (var part in types.Elements(XName.Get("Override", ContentTypes)))
            {
                var name = (string?)part.Attribute("PartName");
                if (name is null || !name.StartsWith('/') || ResolveTarget("", name) != name[1..] ||
                    !declarations.TryAdd(name[1..], (string?)part.Attribute("ContentType") ?? ""))
                    throw new InvalidDataException("Ambiguous part declarations.");
            }
            var rootLinks = await ReadLinksAsync("");
            var main = SelectPart(rootLinks, "officeDocument", WordType + "document.main+xml", required: true)!;
            var document = Parse(await package.ReadPartAsync(main), token);
            if (document.Name != W + "document") throw new InvalidDataException("Unsupported Word namespace or root.");
            var body = One(document, "body") ?? throw new InvalidDataException("Missing main story.");
            var links = await ReadLinksAsync(main);
            var styleName = SelectPart(links, "styles", WordType + "styles+xml");
            var themeName = SelectPart(links, "theme", "application/vnd.openxmlformats-officedocument.theme+xml");
            var styles = styleName is null ? null : Parse(await package.ReadPartAsync(styleName), token);
            var theme = themeName is null ? null : Parse(await package.ReadPartAsync(themeName), token);
            var result = InspectBody(body, styles, theme, token);
            var inactive = InactiveFamilies(document, body, styles, theme, result);
            if (!inactive.IsEmpty && knownDependencies)
            {
                var settingsName = SelectPart(links, "settings", WordType + "settings+xml");
                if (settingsName is not null)
                {
                    var settings = Parse(await package.ReadPartAsync(settingsName), token);
                    knownDependencies = settings.Name == W + "settings" &&
                        settings.Elements().All(element => element.Name == W + "revisionView" || element.Name == W + "trackRevisions");
                }
                var fontTableName = SelectPart(links, "fontTable", WordType + "fontTable+xml");
                if (knownDependencies && fontTableName is not null)
                {
                    var fontTable = Parse(await package.ReadPartAsync(fontTableName), token);
                    knownDependencies = PlainFontTable(fontTable);
                    // Font programs and other font-table dependencies are not inspected.
                    if ((await ReadLinksAsync(fontTableName)).Count != 0) knownDependencies = false;
                }
            }
            token.ThrowIfCancellationRequested();
            return result with { InspectedBytes = package.InspectedBytes,
                InactiveRevisionFamilies = knownDependencies ? inactive : [] };

            async Task<Dictionary<string, List<string>>> ReadLinksAsync(string owner)
            {
                var slash = owner.LastIndexOf('/');
                var name = owner.Length == 0 ? "_rels/.rels" :
                    owner[..(slash + 1)] + "_rels/" + owner[(slash + 1)..] + ".rels";
                var links = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                if (!package.Contains(name)) return links;
                var root = Parse(await package.ReadPartAsync(name), token);
                if (root.Name != XName.Get("Relationships", Relationships)) throw new InvalidDataException("Invalid relationships.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var link in root.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    var id = (string?)link.Attribute("Id");
                    var type = (string?)link.Attribute("Type");
                    var mode = (string?)link.Attribute("TargetMode");
                    if (link.Name != XName.Get("Relationship", Relationships) || string.IsNullOrWhiteSpace(id) ||
                        !ids.Add(id) || string.IsNullOrWhiteSpace(type) || mode is not (null or "Internal" or "External"))
                        throw new InvalidDataException("Ambiguous relationships.");
                    if (type is not (RelationPrefix + "officeDocument" or RelationPrefix + "styles" or RelationPrefix + "theme" or RelationPrefix + "settings" or RelationPrefix + "fontTable"))
                    {
                        // Uninspected dependencies can supply fonts/aliases or rendered
                        // content. Keep their reports rather than claiming exclusivity.
                        knownDependencies = false;
                        continue;
                    }
                    // Only selected internal package parts are read. Never open external targets.
                    if (mode == "External") throw new InvalidDataException("External selected font dependency.");
                    if (!links.TryGetValue(type, out var targets)) links.Add(type, targets = []);
                    targets.Add(ResolveTarget(owner, (string?)link.Attribute("Target") ?? ""));
                }
                return links;
            }

            string? SelectPart(Dictionary<string, List<string>> links, string kind, string contentType, bool required = false)
            {
                if (!links.TryGetValue(RelationPrefix + kind, out var targets))
                {
                    if (required) throw new InvalidDataException("Missing main relationship.");
                    return null;
                }
                if (targets.Count != 1 || !declarations.TryGetValue(targets[0], out var actual) || actual != contentType)
                    throw new InvalidDataException("Missing or ambiguous selected content type.");
                return targets[0];
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
        {
            return new(false, 0, 0, [], ["package-unavailable"], package.InspectedBytes);
        }
        finally { input.Position = position; }
    }

    private static string ResolveTarget(string owner, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 1024 ||
            target.IndexOfAny(['\\', ':', '%', '?', '#']) >= 0 || target.Any(char.IsControl))
            throw new InvalidDataException("Unsupported package target.");
        var segments = new List<string>();
        if (!target.StartsWith('/')) segments.AddRange(owner.Split('/').SkipLast(1));
        foreach (var segment in (target.StartsWith('/') ? target[1..] : target).Split('/'))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("Package target escapes its root.");
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment.Length == 0) throw new InvalidDataException("Empty package target segment.");
            else segments.Add(segment);
        }
        if (segments.Count == 0) throw new InvalidDataException("Empty package target.");
        return string.Join('/', segments);
    }

    private static XElement Parse(byte[] bytes, CancellationToken token)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = DocumentPackageReader.MaximumPartBytes, IgnoreComments = true };
        using var input = new MemoryStream(bytes, false);
        using (var reader = XmlReader.Create(input, settings))
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.Depth > 32) throw new InvalidDataException("XML depth limit.");
            }
        input.Position = 0;
        using var bounded = XmlReader.Create(input, settings);
        return XElement.Load(bounded);
    }

    private static XElement? One(XElement? parent, string name)
    {
        var children = parent?.Elements(W + name).Take(2).ToArray() ?? [];
        return children.Length switch { 0 => null, 1 => children[0], _ => throw new InvalidDataException("Duplicate property.") };
    }

    private static string? Value(XElement? element) => (string?)element?.Attribute(W + "val");
}
