using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

internal static class OfficeDdeFixtures
{
    internal static string[] Create(string directory, string service)
    {
        if (!service.StartsWith("ContextSuiteDde", StringComparison.Ordinal) || !service.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Use the authored data-only DDE service.", nameof(service));
        Directory.CreateDirectory(directory);
        var support = Path.Combine(directory, "authored-support");
        var templates = OfficeExternalLinkFixtures.Create(support);
        string[] names = ["DDE cached.xlsx", "DDE recalculate.xlsx", "DDE direct cached.xlsx", "DDE direct recalculate.xlsx"];
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            File.Copy(Path.Combine(support, templates[0]), path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
            XDocument sheet;
            using (var input = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()) sheet = XDocument.Load(input);
            sheet.Descendants(ns + "f").Single().Value = name.Contains("direct", StringComparison.Ordinal)
                ? $"DDE(\"{service}\",\"{OfficeDdeCanary.Topic}\",\"{OfficeDdeCanary.Item}\")" : "[1]!VALUE";
            sheet.Descendants(ns + "t").Single().Value = "DDE SAVED VALUE";
            Replace("xl/worksheets/sheet1.xml", sheet);
            zip.GetEntry("xl/externalLinks/_rels/externalLink1.xml.rels")!.Delete();
            var link = new XDocument(new XElement(ns + "externalLink", new XElement(ns + "ddeLink",
                new XAttribute("ddeService", service), new XAttribute("ddeTopic", OfficeDdeCanary.Topic),
                new XElement(ns + "ddeItems", new XElement(ns + "ddeItem", new XAttribute("name", OfficeDdeCanary.Item),
                    new XAttribute("advise", "0"), new XAttribute("ole", "0"),
                    new XElement(ns + "values", new XAttribute("rows", "1"), new XAttribute("cols", "1"),
                        new XElement(ns + "value", new XAttribute("t", "n"), new XElement(ns + "val", "123456"))))))));
            Replace("xl/externalLinks/externalLink1.xml", link);

            void Replace(string part, XDocument xml)
            {
                zip.GetEntry(part)?.Delete();
                using var writer = new StreamWriter(zip.CreateEntry(part).Open(), new UTF8Encoding(false));
                xml.Save(writer);
            }
        }
        return names;
    }
}
