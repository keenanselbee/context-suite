using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

// Conservative conversion guard for the current renderer's chart omission.
// Includes hidden/unreferenced declarations; this is not a general fidelity scan.
public sealed record ExcelChartInspection(bool Complete, bool HasCharts, int InspectedBytes)
{
    private const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static async Task<ExcelChartInspection> ReadAsync(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Use a readable, seekable source lease.", nameof(input));
        var position = input.Position;
        var package = new DocumentPackageReader(input, 0, token);
        try
        {
            await package.InitializeAsync();
            var types = Parse(await package.ReadPartAsync("[Content_Types].xml"), token);
            if (types.Name != XName.Get("Types", Types)) throw new InvalidDataException("Unexpected content declarations.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in types.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (part.HasElements) throw new InvalidDataException("Nested content declarations.");
                var kind = (string?)part.Attribute("ContentType");
                if (string.IsNullOrWhiteSpace(kind)) throw new InvalidDataException("Missing content type.");
                if (part.Name == XName.Get("Override", Types))
                {
                    var name = (string?)part.Attribute("PartName") ?? "";
                    if (!name.StartsWith('/') || name.Length < 2 || name.IndexOfAny(['\\', ':', '%', '?', '#']) >= 0 ||
                        name[1..].Split('/').Any(segment => segment is "" or "." or "..") || !names.Add(name))
                        throw new InvalidDataException("Ambiguous part declarations.");
                }
                else if (part.Name == XName.Get("Default", Types))
                {
                    var extension = (string?)part.Attribute("Extension") ?? "";
                    if (extension.Length == 0 || extension.Any(character => !char.IsAsciiLetterOrDigit(character)) || !extensions.Add(extension))
                        throw new InvalidDataException("Ambiguous default declarations.");
                }
                else throw new InvalidDataException("Unknown content declaration.");
                if (kind is "application/vnd.openxmlformats-officedocument.drawingml.chart+xml" or "application/vnd.ms-office.chartex+xml")
                    return new(true, true, package.InspectedBytes);
            }
            var relationships = package.Names.Where(name => name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (relationships.Length > 128) throw new InvalidDataException("Chart relationship inspection limit.");
            foreach (var name in relationships)
            {
                var root = Parse(await package.ReadPartAsync(name), token);
                if (root.Name != XName.Get("Relationships", Relationships)) throw new InvalidDataException("Unexpected relationships.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var relationship in root.Elements())
                {
                    token.ThrowIfCancellationRequested();
                    var id = (string?)relationship.Attribute("Id");
                    var type = (string?)relationship.Attribute("Type");
                    if (relationship.Name != XName.Get("Relationship", Relationships) || relationship.HasElements ||
                        string.IsNullOrWhiteSpace(id) || !ids.Add(id) || string.IsNullOrWhiteSpace(type) ||
                        string.IsNullOrWhiteSpace((string?)relationship.Attribute("Target")))
                        throw new InvalidDataException("Ambiguous relationship declaration.");
                    if (type is "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" or
                        "http://purl.oclc.org/ooxml/officeDocument/relationships/chart" or
                        "http://schemas.microsoft.com/office/2014/relationships/chartEx")
                        return new(true, true, package.InspectedBytes);
                }
            }
            return new(true, false, package.InspectedBytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
        { return new(false, false, package.InspectedBytes); }
        finally { input.Position = position; }
    }

    private static XElement Parse(byte[] bytes, CancellationToken token)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = DocumentPackageReader.MaximumPartBytes };
        using var stream = new MemoryStream(bytes, false);
        using (var reader = XmlReader.Create(stream, settings))
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.Depth > 8) throw new InvalidDataException("Chart declaration XML depth limit.");
            }
        stream.Position = 0;
        using var final = XmlReader.Create(stream, settings);
        return XElement.Load(final);
    }
}
