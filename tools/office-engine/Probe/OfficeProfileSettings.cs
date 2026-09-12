using System.Xml;
using System.Xml.Linq;

// Declaration checks for owned evaluation profiles, not proof of engine isolation.
internal static class OfficeProfileSettings
{
    private static readonly XNamespace Registry = "http://openoffice.org/2001/registry";
    private const string Scripting = "/org.openoffice.Office.Common/Security/Scripting";
    private const string Updates = "/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments";
    private const string Calculation = "/org.openoffice.Office.Calc/Formula/Load";
    private static readonly (string Path, string Name, string Type, string Value)[] Required = [
        (Scripting, "DisableMacrosExecution", "boolean", "true"),
        (Scripting, "DisableActiveContent", "boolean", "true"),
        (Scripting, "DisablePythonRuntime", "boolean", "true"),
        (Scripting, "MacroSecurityLevel", "int", "3"),
        (Updates, "AutoCheckEnabled", "boolean", "false"),
        (Updates, "AutoDownloadEnabled", "boolean", "false"),
        ("/org.openoffice.Office.Common/Misc", "UseOpenCL", "boolean", "false")];

    public static void Apply(string path, int? calculationMode = null)
    {
        if (calculationMode is not (null or 0 or 1)) throw new ArgumentOutOfRangeException(nameof(calculationMode));
        var document = File.Exists(path) ? Read(path) : new XDocument(new XElement(Registry + "items"));
        document.Root!.SetAttributeValue(XNamespace.Xmlns + "oor", Registry.NamespaceName);
        document.Root.SetAttributeValue(XNamespace.Xmlns + "xs", "http://www.w3.org/2001/XMLSchema");
        foreach (var property in Properties(calculationMode))
        {
            foreach (var old in Find(document, property.Path, property.Name).ToArray()) old.Remove();
            document.Root.Add(new XElement("item", new XAttribute(Registry + "path", property.Path),
                new XElement("prop", new XAttribute(Registry + "name", property.Name),
                    new XAttribute(Registry + "op", "fuse"), new XAttribute(Registry + "type", "xs:" + property.Type),
                    new XElement("value", property.Value))));
        }
        document.Save(path);
        Verify(path, calculationMode);
    }

    public static void Verify(string path, int? calculationMode = null)
    {
        var document = Read(path);
        foreach (var property in Properties(calculationMode))
        {
            var matches = Find(document, property.Path, property.Name).ToArray();
            if (matches.Length != 1 || matches[0].Elements().Count() != 1 ||
                matches[0].Element("value") is not { } value || value.HasElements || value.HasAttributes || value.Value != property.Value ||
                (string?)matches[0].Attribute(Registry + "op") != "fuse")
                throw new InvalidDataException("Evaluation profile setting missing, ambiguous or changed: " + property.Name);
        }
    }

    private static IEnumerable<(string Path, string Name, string Type, string Value)> Properties(int? calculationMode)
    {
        foreach (var property in Required) yield return property;
        if (calculationMode is { } mode) yield return (Calculation, "OOXMLRecalcMode", "int", mode.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IEnumerable<XElement> Find(XDocument document, string path, string name) =>
        document.Root!.Elements("item").Where(item => (string?)item.Attribute(Registry + "path") == path)
            .SelectMany(item => item.Elements("prop")).Where(property => (string?)property.Attribute(Registry + "name") == name);

    private static XDocument Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != Registry + "items") throw new InvalidDataException("Unexpected evaluation profile root.");
        return document;
    }
}
