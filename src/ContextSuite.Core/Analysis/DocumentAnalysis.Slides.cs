using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public static partial class DocumentAnalysis
{
    private static async Task AddSlideVisibilityAsync(DocumentPackageReader package, XElement presentation, XElement types,
        string mainPart, ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings, CancellationToken token)
    {
        int? visible = null, hidden = null, defaults = null;
        try
        {
            RequireSlideCompatibility(presentation, token);
            var ns = presentation.Name.Namespace;
            var lists = presentation.Elements(ns + "sldIdLst").ToArray();
            var slides = lists.SelectMany(list => list.Elements()).ToArray();
            if (lists.Length > 1 || slides.Length > 128 || slides.Any(slide => slide.Name != ns + "sldId"))
                throw new InvalidDataException("Unsupported slide list.");
            var countHidden = 0;
            var countDefaults = 0;
            if (slides.Length > 0)
            {
                var separator = mainPart.LastIndexOf('/');
                var folder = separator < 0 ? "" : mainPart[..(separator + 1)];
                var relationshipPart = folder + "_rels/" + mainPart[(separator + 1)..] + ".rels";
                var relationships = ParseXml(await package.ReadPartAsync(relationshipPart), token);
                RequireRoot(relationships, Relationships, "Relationships");
                var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var relation in relationships.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    var id = (string?)relation.Attribute("Id");
                    if (relation.Name != XName.Get("Relationship", Relationships) || string.IsNullOrWhiteSpace(id) || !byId.TryAdd(id, relation))
                        throw new InvalidDataException("Ambiguous slide relationships.");
                }
                var office = ns.NamespaceName.Contains("purl.oclc.org", StringComparison.Ordinal)
                    ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                var ids = new HashSet<uint>();
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var slide in slides)
                {
                    token.ThrowIfCancellationRequested();
                    if (!uint.TryParse((string?)slide.Attribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                        id is < 256 or >= 2147483648 || !ids.Add(id) ||
                        !byId.TryGetValue((string?)slide.Attribute(XName.Get("id", office)) ?? "", out var relation) ||
                        (string?)relation.Attribute("Type") != office + "/slide" ||
                        (string?)relation.Attribute("TargetMode") is not (null or "Internal"))
                        throw new InvalidDataException("Incomplete or conflicting slide reference.");
                    var target = ResolveSlidePart(folder, (string?)relation.Attribute("Target") ?? "");
                    if (!targets.Add(target)) throw new InvalidDataException("Repeated slide target.");
                    var declarations = types.Elements(XName.Get("Override", ContentTypes)).Where(element =>
                        (string?)element.Attribute("PartName") == "/" + target).ToArray();
                    if (declarations.Length != 1 || (string?)declarations[0].Attribute("ContentType") !=
                        "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")
                        throw new InvalidDataException("Missing or conflicting slide content type.");
                    var content = ParseXml(await package.ReadPartAsync(target), token);
                    RequireRoot(content, ns.NamespaceName, "sld");
                    RequireSlideCompatibility(content, token);
                    if (content.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == "show" && attribute.Name.NamespaceName.Length != 0))
                        throw new InvalidDataException("Unsupported visibility attribute namespace.");
                    var show = (string?)content.Attribute("show");
                    if (show is null) countDefaults++;
                    else if (show.Trim(' ', '\t', '\r', '\n') is "false" or "0") countHidden++;
                    else if (show.Trim(' ', '\t', '\r', '\n') is not ("true" or "1"))
                        throw new InvalidDataException("Unsupported slide visibility declaration.");
                }
            }
            visible = slides.Length - countHidden;
            hidden = countHidden;
            defaults = countDefaults;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or System.Text.DecoderFallbackException)
        {
            warnings.Add("Slide visibility is unavailable: slide references or declarations are inconsistent, unsupported or exceed the analysis limits.");
        }
        var available = visible.HasValue;
        facts.Add(new("document.slides-visible", "PowerPoint", "Visible slides (saved settings)", Integer: visible,
            Availability: !available ? FactAvailability.Unavailable : defaults > 0 ? FactAvailability.Derived : FactAvailability.Explicit));
        facts.Add(new("document.slides-hidden", "PowerPoint", "Hidden slides (saved settings)", Integer: hidden,
            Availability: available ? FactAvailability.Explicit : FactAvailability.Unavailable));
        facts.Add(new("document.slides-default-visibility", "PowerPoint", "Slides using default visibility", Integer: defaults,
            Availability: available ? FactAvailability.Explicit : FactAvailability.Unavailable));
        facts.Add(new("document.slide-visibility-scope", "PowerPoint", "Slide visibility inspection scope",
            Text: "Referenced slide XML only, up to 128 slides within shared package limits. An omitted show attribute means visible. " +
                "Custom shows, notes, animations and export settings are not evaluated; these counts are not PDF page counts or conversion approval."));
    }

    private static string ResolveSlidePart(string folder, string target)
    {
        if (target.Length == 0 || target.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0 || target.Any(char.IsControl))
            throw new InvalidDataException("Unsupported slide part target.");
        var segments = target.StartsWith('/') ? new List<string>() : folder.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var segment in (target.StartsWith('/') ? target[1..] : target).Split('/'))
        {
            if (segment.Length == 0) throw new InvalidDataException("Empty slide target segment.");
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("Slide target leaves package.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        if (segments.Count == 0) throw new InvalidDataException("Slide target is not a part.");
        return string.Join('/', segments);
    }

    private static void RequireSlideCompatibility(XElement root, CancellationToken token)
    {
        const string compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        foreach (var element in root.DescendantsAndSelf())
        {
            token.ThrowIfCancellationRequested();
            if (element.Name.NamespaceName == compatibility || element.Attributes().Any(attribute =>
                attribute.Name.NamespaceName == compatibility && attribute.Name.LocalName != "Ignorable"))
                throw new InvalidDataException("Unsupported slide compatibility processing.");
        }
    }
}
