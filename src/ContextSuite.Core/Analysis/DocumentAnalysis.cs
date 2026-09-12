using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public static partial class DocumentAnalysis
{
    private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string ContainerWarning = "The container alone does not confirm the document type suggested by the filename.";

    // The caller holds a stable, read-only lease and supplies a deadline. The
    // stream position is not preserved; no path, renderer or publication is used.
    public static async Task<FileAnalysis> AddPackageAsync(FileAnalysis header, Stream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (header.Identity.FormatId != "zip" || header.Identity.Basis != IdentificationBasis.Content) return header;
        var package = new DocumentPackageReader(stream, header.InspectedBytes, cancellationToken);
        try
        {
            await package.InitializeAsync();
            var hasOpenXml = package.Contains("[Content_Types].xml") || package.Contains("_rels/.rels");
            var hasOpenDocument = package.Contains("mimetype") && package.Contains("META-INF/manifest.xml");
            if (hasOpenXml && hasOpenDocument) throw new InvalidDataException("Conflicting document families.");
            var facts = ImmutableArray.CreateBuilder<AnalysisFact>();
            var detailWarnings = ImmutableArray.CreateBuilder<string>();
            string? id = null;
            if (hasOpenXml) id = await ReadOpenXmlAsync(package, facts, detailWarnings, cancellationToken);
            else if (hasOpenDocument) id = await ReadOpenDocumentAsync(package, facts, cancellationToken);
            var warnings = header.Warnings.AddRange(detailWarnings);
            if (hasOpenXml && id is not null)
            {
                try
                {
                    var links = await ReadRelationshipsAsync(package, cancellationToken);
                    facts.Add(new("document.relationship-parts", "Document", "Relationship files inspected", Integer: links.Parts));
                    facts.Add(new("document.external-relationships", "Document", "Declared external links (relationship files)", Integer: links.External));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
                {
                    facts.Add(new("document.external-relationships", "Document", "Declared external links (relationship files)", Availability: FactAvailability.Unavailable));
                    warnings = warnings.Add("External-link details are unavailable: relationship files are inconsistent, unsupported or exceed the analysis limits.");
                }
                facts.Add(new("document.relationship-scope", "Document", "Link inspection scope",
                    Text: "Relationship declarations only; targets were not opened. Document fields and embedded content were not scanned."));
                try
                {
                    var fonts = await ReadFontReferencesAsync(package, cancellationToken);
                    facts.Add(new("document.font-names", "Document", "Font names declared in selected XML parts", Text: fonts.Names));
                    facts.Add(new("document.font-themes", "Document", "Unresolved font theme references", Text: fonts.Themes));
                    facts.Add(new("document.font-parts", "Document", "XML parts inspected for font declarations", Integer: fonts.Parts));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
                {
                    facts.Add(new("document.font-names", "Document", "Font names declared in selected XML parts", Availability: FactAvailability.Unavailable));
                    warnings = warnings.Add("Font declarations are unavailable: selected XML parts are inconsistent, unsupported or exceed the analysis limits.");
                }
                facts.Add(new("document.font-scope", "Document", "Font inspection scope",
                    Text: "Selected content-type overrides only, including potentially unused parts and styles. Names and theme references are declarations, not resolved fonts. Installed fonts, glyph coverage and embedding rights were not checked."));
            }
            facts.Add(new("package.entries", "Package", "Directory entries", Integer: package.Count));
            facts.Add(new("package.bytes-read", "Package", "Additional bytes read (including repeat reads)", Integer: package.BytesRead));
            var evidence = ImmutableArray.Create("ZIP directory inspected with fixed limits; unrelated entry contents were not read or validated.");
            var identity = header.Identity with { Evidence = evidence };
            if (id is not null)
            {
                var type = FileTypeCatalog.Default.Get(id);
                identity = new(type.Id, type.Name, type.Family, type.CommonUses, IdentificationConfidence.Likely,
                    evidence.Add("Document family identified from agreeing package declarations and, when unencrypted, the main XML structure. This does not validate rendering, active content, signatures or the complete document."),
                    IdentificationBasis.Content);
                warnings = warnings.Remove(ContainerWarning).Remove(HeaderAnalyzer.FilenameOnlyWarning);
                if (!header.FilenameHints.IsDefaultOrEmpty && header.FilenameHints.All(hint => hint.Id != id && hint.Id != "zip"))
                    warnings = warnings.Add("The document package indicates a different type from the filename hint.");
            }
            return header with { Identity = identity, Facts = header.Facts.AddRange(facts), Warnings = warnings, InspectedBytes = package.InspectedBytes };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
        {
            return header with
            {
                Identity = header.Identity with { Evidence = ["Initial ZIP signature found. Additional package inspection did not establish a supported document family."] },
                Warnings = header.Warnings.Add("Document package details are unavailable: the package is unsupported, inconsistent or exceeds the analysis limits. Basic file information is shown."),
                InspectedBytes = package.InspectedBytes
            };
        }
    }

    private static async Task<(int Parts, int External)> ReadRelationshipsAsync(DocumentPackageReader package, CancellationToken cancellationToken)
    {
        var names = package.Names.Where(name => name.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase) || name.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))).ToArray();
        var external = 0;
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segments = name.Split('/');
            if (segments.Length < 2 || segments[^2] != "_rels" || !name.EndsWith(".rels", StringComparison.Ordinal) ||
                segments.Any(segment => segment is "" or "." or "..") || name.Contains('\\'))
                throw new InvalidDataException("Noncanonical relationship part name.");
            var relationships = ParseXml(await package.ReadPartAsync(name), cancellationToken);
            RequireRoot(relationships, Relationships, "Relationships");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relation in relationships.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = (string?)relation.Attribute("Id");
                var type = (string?)relation.Attribute("Type");
                var target = (string?)relation.Attribute("Target");
                var mode = (string?)relation.Attribute("TargetMode");
                if (relation.Name != XName.Get("Relationship", Relationships) || relation.HasElements ||
                    string.IsNullOrWhiteSpace(id) || !ids.Add(id) || string.IsNullOrWhiteSpace(type) ||
                    string.IsNullOrWhiteSpace(target) || mode is not (null or "Internal" or "External"))
                    throw new InvalidDataException("Incomplete or ambiguous relationship declaration.");
                // External targets may be relative. Never resolve, open or fetch
                // a target, including an ordinary hyperlink or an unknown type.
                if (mode == "External") external++;
            }
        }
        return (names.Length, external);
    }

    private static async Task<string?> ReadOpenXmlAsync(DocumentPackageReader package,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings, CancellationToken cancellationToken)
    {
        var types = ParseXml(await package.ReadPartAsync("[Content_Types].xml"), cancellationToken);
        var relationships = ParseXml(await package.ReadPartAsync("_rels/.rels"), cancellationToken);
        RequireRoot(types, ContentTypes, "Types");
        RequireRoot(relationships, Relationships, "Relationships");
        var mainLinks = relationships.Elements(XName.Get("Relationship", Relationships)).Where(element =>
            (string?)element.Attribute("Type") is "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" or
                "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument").ToArray();
        if (mainLinks.Length == 0) return null;
        if (mainLinks.Length != 1 || (string?)mainLinks[0].Attribute("TargetMode") is not (null or "Internal"))
            throw new InvalidDataException("Ambiguous or external main document relationship.");
        var target = (string?)mainLinks[0].Attribute("Target") ?? "";
        if (target.StartsWith('/')) target = target[1..];
        if (target.Length == 0 || target.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0 ||
            target.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("Unsupported document part name.");
        var declarations = types.Elements(XName.Get("Override", ContentTypes)).Where(element =>
            (string?)element.Attribute("PartName") == "/" + target).ToArray();
        if (declarations.Length != 1) throw new InvalidDataException("Missing or ambiguous main document content type.");
        var contentType = (string?)declarations[0].Attribute("ContentType") ?? "";
        var id = contentType switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" or
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml" or
            "application/vnd.ms-word.document.macroEnabled.main+xml" or
            "application/vnd.ms-word.template.macroEnabledTemplate.main+xml" => "docx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" or
            "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml" or
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml" or
            "application/vnd.ms-excel.template.macroEnabled.main+xml" => "xlsx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" or
            "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml" or
            "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml" or
            "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml" or
            "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml" or
            "application/vnd.ms-powerpoint.template.macroEnabled.main+xml" => "pptx",
            _ => null
        };
        if (id is null) return null;
        var main = ParseXml(await package.ReadPartAsync(target), cancellationToken);
        var family = id switch { "docx" => "wordprocessingml", "xlsx" => "spreadsheetml", _ => "presentationml" };
        var ns = main.Name.NamespaceName;
        if (ns != $"http://schemas.openxmlformats.org/{family}/2006/main" && ns != $"http://purl.oclc.org/ooxml/{family}/main")
            throw new InvalidDataException("Unexpected document namespace.");
        RequireRoot(main, ns, id switch { "docx" => "document", "xlsx" => "workbook", _ => "presentation" });
        if (id == "xlsx") AddWorkbookDeclarations(main, facts, warnings, cancellationToken);
        facts.Add(new("document.content-type", "Document", "Declared main content type", Text: contentType));
        facts.Add(new("document.macro-type", "Document", "Macro-enabled package type (not a macro scan)", Boolean: contentType.Contains("macroEnabled", StringComparison.Ordinal)));
        if (id == "docx")
        {
            if (main.Elements(XName.Get("body", ns)).Count() != 1) throw new InvalidDataException("Missing document body.");
            facts.Add(new("document.pages", "Document", "Rendered pages", Availability: FactAvailability.Unavailable));
        }
        else
        {
            var containers = main.Elements(XName.Get(id == "xlsx" ? "sheets" : "sldIdLst", ns)).ToArray();
            if (containers.Length > 1) throw new InvalidDataException("Ambiguous document list.");
            var count = containers.SelectMany(element => element.Elements(XName.Get(id == "xlsx" ? "sheet" : "sldId", ns))).Count();
            facts.Add(new(id == "xlsx" ? "document.sheets" : "document.slides", "Document",
                id == "xlsx" ? "Declared sheets (contents not validated)" : "Declared slides (contents not validated)", Integer: count));
        }
        return id;
    }

    private static async Task<string?> ReadOpenDocumentAsync(DocumentPackageReader package,
        ImmutableArray<AnalysisFact>.Builder facts, CancellationToken cancellationToken)
    {
        var mime = new UTF8Encoding(false, true).GetString(await package.ReadPartAsync("mimetype"));
        var id = mime switch
        {
            "application/vnd.oasis.opendocument.text" or "application/vnd.oasis.opendocument.text-template" => "odt",
            "application/vnd.oasis.opendocument.spreadsheet" or "application/vnd.oasis.opendocument.spreadsheet-template" => "ods",
            "application/vnd.oasis.opendocument.presentation" or "application/vnd.oasis.opendocument.presentation-template" => "odp",
            _ => null
        };
        if (id is null) return null;
        var manifest = ParseXml(await package.ReadPartAsync("META-INF/manifest.xml"), cancellationToken);
        RequireRoot(manifest, Manifest, "manifest");
        var entries = manifest.Elements(XName.Get("file-entry", Manifest)).ToArray();
        var roots = entries.Where(entry => (string?)entry.Attribute(XName.Get("full-path", Manifest)) == "/").ToArray();
        var contentEntries = entries.Where(entry => (string?)entry.Attribute(XName.Get("full-path", Manifest)) == "content.xml").ToArray();
        if (roots.Length != 1 || (string?)roots[0].Attribute(XName.Get("media-type", Manifest)) != mime ||
            contentEntries.Length != 1 || !package.Contains("content.xml"))
            throw new InvalidDataException("Conflicting OpenDocument manifest.");
        var encrypted = contentEntries[0].Elements(XName.Get("encryption-data", Manifest)).Any();
        facts.Add(new("document.content-type", "Document", "Declared package content type", Text: mime));
        facts.Add(new("document.encrypted-content", "Document", "Main content declared encrypted", Boolean: encrypted));
        if (encrypted)
        {
            facts.Add(new("document.content", "Document", "Encrypted document content", Availability: FactAvailability.Unavailable));
            return id;
        }
        var content = ParseXml(await package.ReadPartAsync("content.xml"), cancellationToken);
        RequireRoot(content, Office, "document-content");
        var bodies = content.Elements(XName.Get("body", Office)).ToArray();
        var family = id switch { "odt" => "text", "ods" => "spreadsheet", _ => "presentation" };
        if (bodies.Length != 1 || bodies[0].Elements().Count() != 1 || bodies[0].Elements().Single().Name != XName.Get(family, Office))
            throw new InvalidDataException("Conflicting OpenDocument content family.");
        var body = bodies[0].Elements().Single();
        if (id == "odt") facts.Add(new("document.pages", "Document", "Rendered pages", Availability: FactAvailability.Unavailable));
        else
        {
            var child = id == "ods" ? XName.Get("table", "urn:oasis:names:tc:opendocument:xmlns:table:1.0") :
                XName.Get("page", "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0");
            facts.Add(new(id == "ods" ? "document.sheets" : "document.slides", "Document",
                id == "ods" ? "Sheet elements" : "Slide elements", Integer: body.Elements(child).Count()));
        }
        return id;
    }

    private static XElement ParseXml(byte[] bytes, CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = DocumentPackageReader.MaximumPartBytes, IgnoreComments = true };
        using var input = new MemoryStream(bytes, writable: false);
        using (var reader = XmlReader.Create(input, settings))
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > 32) throw new InvalidDataException("Document XML depth limit exceeded.");
            }
        }
        input.Position = 0;
        using var boundedReader = XmlReader.Create(input, settings);
        return XElement.Load(boundedReader);
    }

    private static void RequireRoot(XElement root, string ns, string name)
    {
        if (root.Name != XName.Get(name, ns)) throw new InvalidDataException("Unexpected document declaration root.");
    }
}
