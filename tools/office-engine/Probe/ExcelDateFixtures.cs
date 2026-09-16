using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Authored numeric cells or explicit arithmetic formulas; no external links or customer documents.
internal static class ExcelDateFixtures
{
    public static (string Name, string Filter, int Pages)[] Create(string directory, bool formulas = false)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cases = new[] { "1900-default", "1900-explicit", "1904" };
        foreach (var name in cases)
        {
            var path = Path.Combine(directory, "Excel dates " + name + ".xlsx");
            File.Copy(Path.Combine(directory, "Excel \u00fc.xlsx"), path, false);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            var workbook = Read("xl/workbook.xml");
            if (name != "1900-default") workbook.Root!.AddFirst(new XElement(ns + "workbookPr", new XAttribute("date1904", name == "1904" ? "1" : "0")));
            workbook.Descendants(ns + "definedName").Single().Value = "'Print area'!$A$1:$B$8";
            Write("xl/workbook.xml", workbook.ToString());
            var sheet = Read("xl/worksheets/sheet1.xml");
            var data = sheet.Root!.Element(ns + "sheetData")!;
            data.RemoveNodes();
            data.Add(TextRow(1, "DATE SYSTEM " + name));
            string[] values = ["1", "59", "60", "61", "40729", "40729.5", "1.5"];
            for (var index = 0; index < values.Length; index++)
            {
                var row = index + 2;
                var element = TextRow(row, "R" + (index + 1).ToString("D2"));
                element.Add(new XElement(ns + "c", new XAttribute("r", "B" + row), new XAttribute("s", index == 5 ? "2" : index == 6 ? "3" : "1"),
                    formulas ? new XElement(ns + "f", "0+" + values[index]) : null,
                    new XElement(ns + "v", formulas && index < 4 ? "40729" : values[index])));
                data.Add(element);
            }
            data.Add(TextRow(20, "OUTSIDE PRINT AREA"));
            Write("xl/worksheets/sheet1.xml", sheet.ToString());
            Write("xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <numFmts count="3"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/><numFmt numFmtId="165" formatCode="yyyy-mm-dd hh:mm:ss"/><numFmt numFmtId="166" formatCode="[h]:mm:ss"/></numFmts>
                <fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);
            var types = Read("[Content_Types].xml");
            types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
            Write("[Content_Types].xml", types.ToString());
            var relationships = Read("xl/_rels/workbook.xml.rels");
            relationships.Root!.Add(new XElement(relationships.Root.Name.Namespace + "Relationship", new XAttribute("Id", "dateStyle"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")));
            Write("xl/_rels/workbook.xml.rels", relationships.ToString());

            XElement TextRow(int row, string text) => new(ns + "row", new XAttribute("r", row),
                new XElement(ns + "c", new XAttribute("r", "A" + row), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", text))));
            XDocument Read(string part) { using var stream = archive.GetEntry(part)!.Open(); return XDocument.Load(stream); }
            void Write(string part, string text)
            { archive.GetEntry(part)?.Delete(); using var writer = new StreamWriter(archive.CreateEntry(part).Open()); writer.Write(text); }
        }
        return cases.Select(name => ("Excel dates " + name + ".xlsx", "calc_pdf_Export", 1)).ToArray();
    }

    public static DateObservation[] Observe(string name, string[] pages, bool cachedFormulas = false)
    {
        if (pages.Length != 1 || !pages[0].Contains("DATE SYSTEM") || pages[0].Contains("HIDDEN") || pages[0].Contains("OUTSIDE"))
            throw new InvalidDataException("Date fixture text or print policy changed.");
        string[] expected = name.Contains("1904", StringComparison.Ordinal)
            ? ["1904-01-02", "1904-02-29", "1904-03-01", "1904-03-02", "2015-07-06", "2015-07-06 12:00:00", "36:00:00"]
            : ["1900-01-01", "1900-02-28", "1900-02-29", "1900-03-01", "2011-07-05", "2011-07-05 12:00:00", "36:00:00"];
        if (cachedFormulas)
            for (var index = 0; index < 4; index++) expected[index] = expected[4];
        var matches = Regex.Matches(pages[0], @"R(?<row>0[1-7])\s+(?<value>.*?)(?=R0[1-7]|$)", RegexOptions.Singleline);
        if (matches.Count != 7 || matches.Select(match => match.Groups["row"].Value).Distinct().Count() != 7)
            throw new InvalidDataException("Cannot associate all seven authored date cells with PDF text.");
        return matches.Select(match =>
        {
            var row = int.Parse(match.Groups["row"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var value = Regex.Replace(match.Groups["value"].Value, @"\s+", " ").Trim();
            return new DateObservation(row, expected[row - 1], value, expected[row - 1] == value);
        }).OrderBy(item => item.Row).ToArray();
    }

    internal sealed record DateObservation(int Row, string ExpectedExcelDisplay, string Observed, bool Matches);
}
