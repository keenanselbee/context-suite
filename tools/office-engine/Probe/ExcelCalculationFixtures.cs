using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Only copies the passive workbook authored by OfficeFixtures, never user input.
internal static class ExcelCalculationFixtures
{
    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cases = new[] { ("cached-auto", "auto", "257"), ("stale-auto", "auto", "918273"),
            ("stale-manual", "manual", "918273"), ("missing-auto", "auto", "") };
        foreach (var (name, mode, cache) in cases)
        {
            var path = Path.Combine(directory, "Excel calculation " + name + ".xlsx");
            File.Copy(Path.Combine(directory, "Excel \u00fc.xlsx"), path, false);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            foreach (var part in new[] { "xl/workbook.xml", "xl/worksheets/sheet1.xml" })
            {
                var entry = archive.GetEntry(part) ?? throw new InvalidDataException("Missing authored workbook part.");
                XDocument document;
                using (var input = entry.Open()) document = XDocument.Load(input);
                if (part == "xl/workbook.xml")
                {
                    var calculation = document.Root!.Element(ns + "calcPr")!;
                    calculation.SetAttributeValue("calcMode", mode);
                    calculation.SetAttributeValue("fullCalcOnLoad", "0");
                    calculation.SetAttributeValue("forceFullCalc", "0");
                }
                else
                {
                    var cells = document.Descendants(ns + "c").ToDictionary(cell => (string)cell.Attribute("r")!);
                    cells["A2"].Element(ns + "v")!.Value = "100";
                    cells["B2"].Element(ns + "v")!.Value = "157";
                    if (cells["B3"].Element(ns + "f")!.Value != "SUM(A2:B2)")
                        throw new InvalidDataException("Authored formula changed.");
                    if (cache.Length == 0) cells["B3"].Element(ns + "v")!.Remove();
                    else cells["B3"].Element(ns + "v")!.Value = cache;
                }
                entry.Delete();
                using var output = archive.CreateEntry(part, CompressionLevel.Optimal).Open();
                document.Save(output);
            }
        }
        return cases.Select(item => ("Excel calculation " + item.Item1 + ".xlsx", "calc_pdf_Export", 1)).ToArray();
    }

    public static string Observe(string name, string[] pages)
    {
        if (pages.Length != 1 || !pages[0].Contains("Formula result") || pages[0].Contains("HIDDEN") || pages[0].Contains("OUTSIDE"))
            throw new InvalidDataException("Calculation fixture text or print policy changed.");
        var computed = Regex.IsMatch(pages[0], @"(?<!\d)257(?!\d)");
        var stale = Regex.IsMatch(pages[0], @"(?<!\d)918273(?!\d)");
        if (computed == stale) throw new InvalidDataException("Cannot uniquely identify rendered formula result; inspect retained text.");
        if (name.Contains("cached-auto", StringComparison.Ordinal))
        {
            if (!computed) throw new InvalidDataException("Correct-cache control changed its value.");
            return "Matches correct cache and arithmetic; does not distinguish recalculation";
        }
        if (name.Contains("missing-auto", StringComparison.Ordinal) && stale)
            throw new InvalidDataException("Missing-cache fixture unexpectedly contains stale sentinel.");
        return computed ? "Calculated arithmetic result 257" : "Retained stale cached result 918273";
    }
}
