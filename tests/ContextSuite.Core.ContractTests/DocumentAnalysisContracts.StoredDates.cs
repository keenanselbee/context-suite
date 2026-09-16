using ContextSuite.Core.Analysis;
using System.Xml.Linq;

internal static partial class DocumentAnalysisContracts
{
    private static async Task StoredExcelDatesAsync(Action<bool, string> check)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string content = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
        (string Name, string Text)[] Parts(string code = "yyyy-mm-dd", string values = "1 59 60 61 40729 40729.5",
            string properties = "", int formatId = 164)
        {
            var cells = values.Split(' ').Select((value, index) => new XElement(XName.Get("c", ns),
                new XAttribute("r", "A" + (index + 1)), new XAttribute("s", "1"), new XElement(XName.Get("v", ns), value)));
            var sheet = new XElement(XName.Get("worksheet", ns), new XElement(XName.Get("sheetData", ns),
                cells.Select((cell, index) => new XElement(XName.Get("row", ns), new XAttribute("r", index + 1), cell))));
            var number = new XElement(XName.Get("numFmt", ns), new XAttribute("numFmtId", formatId), new XAttribute("formatCode", code));
            return [
                ("[Content_Types].xml", $"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/xl/workbook.xml' ContentType='{content}sheet.main+xml'/><Override PartName='/xl/styles.xml' ContentType='{content}styles+xml'/><Override PartName='/xl/worksheets/sheet1.xml' ContentType='{content}worksheet+xml'/></Types>"),
                ("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='main' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>"),
                ("xl/workbook.xml", $"<workbook xmlns='{ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'>{properties}<sheets><sheet name='Dates' sheetId='1' r:id='s1'/></sheets></workbook>"),
                ("xl/styles.xml", $"<styleSheet xmlns='{ns}'><numFmts count='1'>{number}</numFmts><cellXfs count='2'><xf numFmtId='0'/><xf numFmtId='{formatId}' applyNumberFormat='1'/></cellXfs></styleSheet>"),
                ("xl/worksheets/sheet1.xml", sheet.ToString())];
        }
        async Task<ExcelStoredDateInspection> Inspect((string Name, string Text)[] parts)
        {
            var bytes = Zip(parts); using var input = new MemoryStream(bytes, false); input.Position = 7;
            var result = await ExcelStoredDateInspection.ReadAsync(input, default);
            check(input.Position == 7 && input.CanRead && input.ToArray().SequenceEqual(bytes), "stored Excel dates: source and position preserved");
            return result;
        }
        foreach (var properties in new[] { "", "<workbookPr date1904='0'/>", "<workbookPr date1904='false'/>" })
        {
            var result = await Inspect(Parts(properties: properties));
            check(result.Complete && result.EarlyDateCells == 3 && result.DateFormulaCells == 0 && result.WorksheetParts == 1,
                "stored Excel dates: January/February serials distinguished from modern dates");
        }
        foreach (var properties in new[] { "<workbookPr date1904='1'/>", "<workbookPr date1904='true'/>" })
            check((await Inspect(Parts(properties: properties))).EarlyDateCells == 0, "stored Excel dates: 1904 system does not inherit 1900 risk");
        foreach (var code in new[] { "yyyy-mm-dd", "m/d/yy h:mm", "mmm", "dd", "yy" })
            check((await Inspect(Parts(code))).EarlyDateCells == 3, "stored Excel dates: calendar code " + code);
        foreach (var code in new[] { "0.00", "General", "[h]:mm:ss", "[mm]", "hh:mm", "mm:ss", "\"day\"0", "\\d0", "0_y" })
            check((await Inspect(Parts(code))).EarlyDateCells == 0, "stored Excel dates: numeric/time/literal format is not a calendar date " + code);
        var boundary = await Inspect(Parts(values: "0 0.5 1 59.999 60 60.999 61 61.001 -1"));
        check(boundary.EarlyDateCells == 6, "stored Excel dates: zero and fractional boundaries are retained as risks");
        foreach (var (id, expected) in new[] { (14, 3), (15, 3), (16, 3), (17, 3), (22, 3), (18, 0), (21, 0), (45, 0), (46, 0), (49, 0) })
        {
            var parts = Parts(formatId: id);
            var styles = XElement.Parse(parts[3].Text); styles.Element(XName.Get("numFmts", ns))?.Remove(); parts[3] = (parts[3].Name, styles.ToString());
            check((await Inspect(parts)).EarlyDateCells == expected, "stored Excel dates: built-in format " + id);
        }
        var formulas = Parts();
        formulas[4] = (formulas[4].Name, formulas[4].Text.Replace("<v>1</v>", "<f>DATE(1900,1,1)</f><v>1</v>"));
        var formula = await Inspect(formulas);
        check(formula.EarlyDateCells == 3 && formula.DateFormulaCells == 1,
            "stored Excel dates: cached formula evidence is counted without calculation");
        foreach (var code in new[] { "[Red]yyyy-mm-dd", "[>1]yyyy-mm-dd;0", "[$-409]yyyy-mm-dd", "yyyy\"", "0\\" })
        {
            var result = await Inspect(Parts(code));
            check(!result.Complete && result.EarlyDateCells is null, "stored Excel dates: unsupported format yields unavailable rather than zero: " + code);
        }
        foreach (var (label, index, from, to) in new[] {
            ("ambiguous workbook", 2, "<sheets>", "<workbookPr date1904='0'/><workbookPr date1904='1'/><sheets>"),
            ("compatibility dates", 2, "<sheets>", "<workbookPr dateCompatibility='1'/><sheets>"),
            ("invalid date flag", 2, "<sheets>", "<workbookPr date1904='yes'/><sheets>"),
            ("strict namespace", 2, ns, "http://purl.oclc.org/ooxml/spreadsheetml/main"),
            ("unknown style", 4, "s=\"1\"", "s=\"2\""),
            ("invalid numeric", 4, "<v>1</v>", "<v>NaN</v>"),
            ("repeated value", 4, "<v>1</v>", "<v>1</v><v>1</v>"),
            ("conditional format", 4, "</worksheet>", "<conditionalFormatting/></worksheet>"),
            ("row format", 4, "<row r=\"1\">", "<row r=\"1\" s=\"1\">"),
            ("inherited format", 3, "applyNumberFormat='1'", "applyNumberFormat='0'"),
            ("qualified style", 3, "numFmtId='164'", "p:numFmtId='164' xmlns:p='urn:unknown'") })
        {
            var parts = Parts(); check(parts[index].Text.Contains(from), "stored Excel dates: mutation applies " + label);
            parts[index] = (parts[index].Name, parts[index].Text.Replace(from, to));
            var result = await Inspect(parts);
            check(!result.Complete && result.EarlyDateCells is null && result.DateFormulaCells is null, "stored Excel dates: unavailable for " + label);
        }
        var oversized = Parts(); oversized[4] = (oversized[4].Name, new string('x', 256 * 1024 + 1));
        check(!(await Inspect(oversized)).Complete, "stored Excel dates: part allocation budget enforced");
        var dtd = Parts(); dtd[4] = (dtd[4].Name, "<!DOCTYPE worksheet [<!ENTITY x SYSTEM 'file:///not-read'>]>" + dtd[4].Text);
        check(!(await Inspect(dtd)).Complete, "stored Excel dates: DTD rejected without resolution");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var source = new MemoryStream(Zip(Parts()), false);
        try { await ExcelStoredDateInspection.ReadAsync(source, canceled.Token); check(false, "stored Excel dates: cancellation swallowed"); }
        catch (OperationCanceledException) { check(source.Position == 0, "stored Excel dates: cancellation precedes reads"); }
        using var preflightInput = new MemoryStream(Zip(Parts()), false);
        var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("fixture.xlsx", preflightInput, default);
        check(preflight.FormatId == "xlsx" && preflight.Refusal is null && preflight.StoredDates?.EarlyDateCells == 3,
            "stored Excel dates: preflight returns separate evidence without silently deciding pending launch policy");
    }
}
