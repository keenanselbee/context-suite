using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

// Stored-value evidence for conversion policy, never a complete rendering permit.
// Counts declared worksheet parts, including hidden or potentially unused sheets.
public sealed record ExcelStoredDateInspection(bool Complete, int? EarlyDateCells,
    int? DateFormulaCells, int WorksheetParts, int InspectedBytes)
{
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string Prefix = "application/vnd.openxmlformats-officedocument.spreadsheetml.";

    public static async Task<ExcelStoredDateInspection> ReadAsync(Stream input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("Use a readable, seekable source lease.", nameof(input));
        var position = input.Position;
        var package = new DocumentPackageReader(input, 0, token);
        var sheets = 0;
        try
        {
            await package.InitializeAsync();
            var types = Parse(await package.ReadPartAsync("[Content_Types].xml"), token);
            if (types.Name != XName.Get("Types", Types)) throw new InvalidDataException("Unexpected content declarations.");
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in types.Elements(XName.Get("Override", Types)))
            {
                var name = (string?)part.Attribute("PartName") ?? "";
                var kind = (string?)part.Attribute("ContentType") ?? "";
                if (!name.StartsWith('/') || name.Length < 2 || name.IndexOfAny(['\\', ':', '%', '?', '#']) >= 0 ||
                    name[1..].Split('/').Any(segment => segment is "" or "." or "..") || !names.TryAdd(name[1..], kind))
                    throw new InvalidDataException("Ambiguous part declarations.");
            }
            var workbookName = names.Where(pair => pair.Value == Prefix + "sheet.main+xml").Select(pair => pair.Key).ToArray();
            if (workbookName.Length != 1) throw new InvalidDataException("Ordinary workbook declaration required.");
            var workbook = Parse(await package.ReadPartAsync(workbookName[0]), token);
            Require(workbook, "workbook");
            var properties = workbook.Elements(XName.Get("workbookPr", Spreadsheet)).ToArray();
            if (properties.Length > 1) throw new InvalidDataException("Ambiguous date base.");
            var date1904 = (string?)properties.SingleOrDefault()?.Attribute("date1904");
            if (date1904 is not (null or "0" or "1" or "true" or "false")) throw new InvalidDataException("Unknown date base.");
            // Strict date semantics and compatibility extensions need separate evidence.
            if (properties.SingleOrDefault()?.Attribute("dateCompatibility") is not null)
                throw new InvalidDataException("Compatibility date semantics require separate inspection.");
            var stylesNames = names.Where(pair => pair.Value == Prefix + "styles+xml").Select(pair => pair.Key).ToArray();
            if (stylesNames.Length != 1) throw new InvalidDataException("Explicit styles required for date inspection.");
            var styles = Parse(await package.ReadPartAsync(stylesNames[0]), token);
            Require(styles, "styleSheet");
            var formats = new Dictionary<int, string>();
            foreach (var format in styles.Elements(XName.Get("numFmts", Spreadsheet)).Elements(XName.Get("numFmt", Spreadsheet)))
            {
                var id = Integer(format, "numFmtId"); var code = (string?)format.Attribute("formatCode");
                if (code is null || code.Length > 512 || !formats.TryAdd(id, code)) throw new InvalidDataException("Invalid number format.");
            }
            var groups = styles.Elements(XName.Get("cellXfs", Spreadsheet)).ToArray();
            if (groups.Length != 1) throw new InvalidDataException("Explicit cell formats required.");
            var cellFormats = groups[0].Elements(XName.Get("xf", Spreadsheet)).ToArray();
            if (cellFormats.Length is < 1 or > 4096) throw new InvalidDataException("Cell format limit.");
            var early = 0; var formulas = 0; var cells = 0;
            foreach (var name in names.Where(pair => pair.Value == Prefix + "worksheet+xml").Select(pair => pair.Key))
            {
                if (++sheets > 64) throw new InvalidDataException("Worksheet part limit.");
                var sheet = Parse(await package.ReadPartAsync(name), token); Require(sheet, "worksheet");
                // These can supply an effective format different from a cell's direct style.
                if (sheet.Descendants().Any(element => element.Name.LocalName == "conditionalFormatting") ||
                    sheet.Descendants(XName.Get("row", Spreadsheet)).Any(row => row.Attribute("s") is not null) ||
                    sheet.Descendants(XName.Get("col", Spreadsheet)).Any(col => col.Attribute("style") is not null))
                    throw new InvalidDataException("Effective formatting requires additional interpretation.");
                foreach (var cell in sheet.Elements(XName.Get("sheetData", Spreadsheet)).Elements(XName.Get("row", Spreadsheet)).Elements(XName.Get("c", Spreadsheet)))
                {
                    token.ThrowIfCancellationRequested();
                    if (++cells > 65536) throw new InvalidDataException("Cell inspection limit.");
                    var style = cell.Attribute("s") is null ? 0 : Integer(cell, "s");
                    if (style >= cellFormats.Length) throw new InvalidDataException("Missing cell format.");
                    var xf = cellFormats[style];
                    if ((string?)xf.Attribute("applyNumberFormat") is "0" or "false" || xf.Attribute("numFmtId") is null)
                        throw new InvalidDataException("Inherited number format needs additional interpretation.");
                    var id = Integer(xf, "numFmtId");
                    var date = formats.TryGetValue(id, out var code) ? IsCalendarFormat(code) : id switch
                    {
                        >= 14 and <= 17 or 22 => true,
                        0 or 1 or 2 or 3 or 4 or 9 or 10 or 11 or 12 or 13 or >= 18 and <= 21 or >= 37 and <= 40 or >= 45 and <= 49 => false,
                        _ => throw new InvalidDataException("Locale-dependent or unknown number format.")
                    };
                    if (!date) continue;
                    var formula = cell.Elements(XName.Get("f", Spreadsheet)).ToArray();
                    if (formula.Length > 1) throw new InvalidDataException("Ambiguous formula.");
                    if (formula.Length == 1) formulas++;
                    if ((string?)cell.Attribute("t") is not (null or "n")) continue;
                    var values = cell.Elements(XName.Get("v", Spreadsheet)).ToArray();
                    if (values.Length > 1 || values.Any(value => value.HasElements)) throw new InvalidDataException("Ambiguous numeric cell.");
                    if (values.Length == 0) continue;
                    if (!double.TryParse(values[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                        throw new InvalidDataException("Invalid stored numeric value.");
                    if (date1904 is not ("1" or "true") && value >= 0 && value < 61) early++;
                }
            }
            if (sheets == 0) throw new InvalidDataException("No declared worksheets inspected.");
            return new(true, early, formulas, sheets, package.InspectedBytes);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or DecoderFallbackException)
        { return new(false, null, null, sheets, package.InspectedBytes); }
        finally { input.Position = position; }
    }

    private static int Integer(XElement element, string name) => int.TryParse((string?)element.Attribute(name),
        NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : throw new InvalidDataException("Invalid style index.");

    private static bool IsCalendarFormat(string code)
    {
        // One uncomplicated section only. Do not guess conditional/localized formats.
        var tokens = new StringBuilder();
        var elapsed = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = char.ToLowerInvariant(code[index]);
            if (character == '"')
            {
                var end = code.IndexOf('"', index + 1);
                if (end < 0) throw new InvalidDataException("Unclosed format literal.");
                index = end;
            }
            else if (character is '\\' or '_' or '*')
            { if (++index >= code.Length) throw new InvalidDataException("Incomplete format escape."); }
            else if (character == '[')
            {
                var end = code.IndexOf(']', index + 1);
                if (end < 0 || code[(index + 1)..end].ToLowerInvariant() is not ("h" or "hh" or "m" or "mm" or "s" or "ss"))
                    throw new InvalidDataException("Conditional or localized number format.");
                elapsed = true; tokens.Append(code[(index + 1)..end].ToLowerInvariant()); index = end;
            }
            else if (character is ';' or ']' || character > 127) throw new InvalidDataException("Unsupported format section or locale.");
            else tokens.Append(character);
        }
        var text = tokens.ToString().Replace("am/pm", "", StringComparison.Ordinal).Replace("a/p", "", StringComparison.Ordinal);
        return text.Contains('y') || text.Contains('d') || !elapsed && text.Contains('m') && !text.Contains('h') && !text.Contains('s');
    }

    private static XElement Parse(byte[] bytes, CancellationToken token)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = DocumentPackageReader.MaximumPartBytes };
        using var stream = new MemoryStream(bytes, false);
        using (var reader = XmlReader.Create(stream, settings))
            while (reader.Read()) { token.ThrowIfCancellationRequested(); if (reader.Depth > 32) throw new InvalidDataException("Date XML depth limit."); }
        stream.Position = 0;
        using var bounded = XmlReader.Create(stream, settings);
        var result = XElement.Load(bounded);
        if (result.DescendantsAndSelf().Any(element => element.Name.NamespaceName is not (Spreadsheet or Types) ||
            element.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length != 0 &&
                attribute.Name != XNamespace.Xml + "space" && attribute.Name != XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))))
            throw new InvalidDataException("Qualified or compatibility declarations need interpretation.");
        return result;
    }

    private static void Require(XElement element, string name)
    { if (element.Name != XName.Get(name, Spreadsheet)) throw new InvalidDataException("Unsupported worksheet namespace or root."); }
}
