using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Passive literal-cell fixtures only. Expectations describe saved print settings,
// not a claim of an independently captured Microsoft Excel rendering baseline.
internal static class ExcelPrintFixtures
{
    private static readonly string[] Cases = ["manual-break", "repeat-title", "fit-one-page", "disjoint-areas", "hidden-cells"];

    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var name in Cases)
        {
            var path = Path.Combine(directory, "Excel print " + name + ".xlsx");
            File.Copy(Path.Combine(directory, "Excel \u00fc.xlsx"), path, false);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            var workbook = Read("xl/workbook.xml");
            var names = workbook.Root!.Element(ns + "definedNames")!;
            names.Elements().Single().Value = name == "disjoint-areas"
                ? "'Print area'!$A$1:$B$2,'Print area'!$A$5:$B$6" : "'Print area'!$A$1:$C$6";
            if (name == "repeat-title") names.Add(new XElement(ns + "definedName",
                new XAttribute("name", "_xlnm.Print_Titles"), new XAttribute("localSheetId", "0"), "'Print area'!$1:$1"));
            Write("xl/workbook.xml", workbook);
            var sheet = Read("xl/worksheets/sheet1.xml");
            var data = sheet.Root!.Element(ns + "sheetData")!;
            data.RemoveNodes();
            foreach (var (row, marker) in new[] { (1, "TITLE"), (2, "R02"), (3, "R03"), (5, "R05"), (6, "R06"), (20, "OUTSIDE") })
            {
                var element = new XElement(ns + "row", new XAttribute("r", row), new XAttribute("ht", "24"), new XAttribute("customHeight", "1"), Cell("A" + row, marker));
                if (name == "hidden-cells" && row == 3) element.SetAttributeValue("hidden", "1");
                if (name == "hidden-cells" && row == 2) element.Add(Cell("C2", "HIDDEN_COLUMN"));
                data.Add(element);
            }
            sheet.Root.Element(ns + "cols")!.Elements().Single().SetAttributeValue("width", "24");
            sheet.Root.Element(ns + "cols")!.Add(new XElement(ns + "col", new XAttribute("min", "3"),
                new XAttribute("max", "3"), new XAttribute("width", "20"), new XAttribute("customWidth", "1"),
                new XAttribute("hidden", name == "hidden-cells" ? "1" : "0")));
            var fit = name is "fit-one-page" or "hidden-cells";
            sheet.Descendants(ns + "pageSetUpPr").Single().SetAttributeValue("fitToPage", fit ? "1" : "0");
            var setup = sheet.Root.Element(ns + "pageSetup")!;
            if (!fit) { setup.Attribute("fitToWidth")!.Remove(); setup.Attribute("fitToHeight")!.Remove(); setup.SetAttributeValue("scale", "100"); }
            if (name is "manual-break" or "repeat-title" or "fit-one-page")
                sheet.Root.Add(new XElement(ns + "rowBreaks", new XAttribute("count", "1"), new XAttribute("manualBreakCount", "1"),
                    new XElement(ns + "brk", new XAttribute("id", "4"), new XAttribute("min", "0"), new XAttribute("max", "16383"), new XAttribute("man", "1"))));
            Write("xl/worksheets/sheet1.xml", sheet);

            XElement Cell(string address, string text) => new(ns + "c", new XAttribute("r", address), new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", new XElement(ns + "t", text)));
            XDocument Read(string part) { using var input = archive.GetEntry(part)!.Open(); return XDocument.Load(input); }
            void Write(string part, XDocument document)
            { archive.GetEntry(part)!.Delete(); using var output = archive.CreateEntry(part).Open(); document.Save(output); }
        }
        return Cases.Select(name => ("Excel print " + name + ".xlsx", "calc_pdf_Export", Expected(name).Length)).ToArray();
    }

    public static PrintObservation Observe(string name, string[] text)
    {
        var key = Path.GetFileNameWithoutExtension(name)["Excel print ".Length..];
        var expected = Expected(key);
        var actual = text.Select(page => Regex.Replace(page, @"\s+", " ").Trim()).ToArray();
        return new(expected, actual, expected.SequenceEqual(actual));
    }

    private static string[] Expected(string name) => name switch
    {
        "manual-break" => ["TITLE R02 R03", "R05 R06"],
        "repeat-title" => ["TITLE R02 R03", "TITLE R05 R06"],
        "fit-one-page" => ["TITLE R02 R03 R05 R06"],
        "disjoint-areas" => ["TITLE R02", "R05 R06"],
        "hidden-cells" => ["TITLE R02 R05 R06"],
        _ => throw new ArgumentException("Unknown authored print fixture.")
    };

    internal sealed record PrintObservation(string[] ExpectedPages, string[] ObservedPages, bool Matches);
}
