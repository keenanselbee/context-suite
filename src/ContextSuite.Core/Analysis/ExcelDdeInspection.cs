using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

// Positive-risk guard for demonstrated DDE calculation loss. Incomplete coverage
// remains unknown; this is not a package security certificate or link resolver.
public sealed record ExcelDdeInspection(bool Complete, bool HasDde, int InspectedBytes)
{
    private const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string Prefix = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private static readonly Regex DdeCall = new(@"(?<![\p{L}\p{N}_.])(?:_xlfn\.)?DDE\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static async Task<ExcelDdeInspection> ReadAsync(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Use a readable, seekable source lease.", nameof(input));
        var position = input.Position;
        var package = new DocumentPackageReader(input, 0, token);
        try
        {
            await package.InitializeAsync();
            var declarations = Parse(await package.ReadPartAsync("[Content_Types].xml"), token);
            if (declarations.Name != XName.Get("Types", Types)) throw new InvalidDataException("Unexpected content declarations.");
            var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in declarations.Elements(XName.Get("Override", Types)))
            {
                var name = (string?)entry.Attribute("PartName") ?? "";
                var kind = (string?)entry.Attribute("ContentType") ?? "";
                if (!name.StartsWith('/') || name.Length < 2 || name.IndexOfAny(['\\', ':', '%', '?', '#']) >= 0 ||
                    name[1..].Split('/').Any(segment => segment is "" or "." or "..") || !parts.TryAdd(name[1..], kind))
                    throw new InvalidDataException("Ambiguous part declarations.");
            }
            var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in declarations.Elements(XName.Get("Default", Types)))
            {
                var extension = (string?)entry.Attribute("Extension") ?? "";
                var kind = (string?)entry.Attribute("ContentType") ?? "";
                if (extension.Length == 0 || extension.Any(character => !char.IsAsciiLetterOrDigit(character)) || !defaults.Add(extension))
                    throw new InvalidDataException("Ambiguous default declarations.");
                foreach (var name in package.Names.Where(name => name.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase)))
                    parts.TryAdd(name, kind);
            }
            // Inspect link declarations first, then names and formula cells.
            var names = parts.Where(pair => pair.Value == Prefix + "externalLink+xml")
                .Concat(parts.Where(pair => pair.Value == Prefix + "sheet.main+xml" || pair.Value == Prefix + "worksheet+xml"))
                .Select(pair => pair.Key).ToArray();
            if (names.Length > 128) throw new InvalidDataException("DDE part inspection limit.");
            foreach (var name in names)
            {
                var document = Parse(await package.ReadPartAsync(name), token);
                foreach (var element in document.DescendantsAndSelf())
                {
                    token.ThrowIfCancellationRequested();
                    if (element.Name.NamespaceName is not ("http://schemas.openxmlformats.org/spreadsheetml/2006/main" or
                        "http://purl.oclc.org/ooxml/spreadsheetml/main")) continue;
                    if (element.Name.LocalName == "ddeLink" ||
                        (element.Name.LocalName is "f" or "definedName" && IsDdeFormula(element.Value)))
                        return new(true, true, package.InspectedBytes);
                }
            }
            return new(true, false, package.InspectedBytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException or RegexMatchTimeoutException)
        { return new(false, false, package.InspectedBytes); }
        finally { input.Position = position; }
    }

    private static bool IsDdeFormula(string formula)
    {
        // Ignore quoted text and quoted sheet/workbook identifiers, including
        // doubled-quote escapes. XML entities have already been decoded safely.
        var visible = new StringBuilder(formula.Length);
        char quote = '\0';
        for (var index = 0; index < formula.Length; index++)
        {
            var value = formula[index];
            if (quote != '\0')
            {
                if (value == quote)
                {
                    if (index + 1 < formula.Length && formula[index + 1] == quote) index++;
                    else quote = '\0';
                }
                visible.Append(' ');
            }
            else if (value is '\'' or '"') { quote = value; visible.Append(' '); }
            else visible.Append(value);
        }
        return DdeCall.IsMatch(visible.ToString());
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
                if (reader.Depth > 32) throw new InvalidDataException("DDE XML depth limit.");
            }
        stream.Position = 0;
        using var final = XmlReader.Create(stream, settings);
        return XElement.Load(final);
    }
}
