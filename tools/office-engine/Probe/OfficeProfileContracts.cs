using System.Xml;
using System.Xml.Linq;

internal static class OfficeProfileContracts
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.xcu");
        XNamespace registry = "http://openoffice.org/2001/registry";
        OfficeProfileSettings.Apply(path);
        var baseline = File.ReadAllText(path);
        var count = 1;
        foreach (var name in new[] { "DisableMacrosExecution", "DisableActiveContent", "DisablePythonRuntime", "MacroSecurityLevel",
            "AutoCheckEnabled", "AutoDownloadEnabled", "UseOpenCL" })
        {
            var changed = XDocument.Parse(baseline);
            changed.Descendants("prop").Single(property => (string?)property.Attribute(registry + "name") == name)
                .Element("value")!.Value = "unexpected";
            changed.Save(path);
            Reject(() => OfficeProfileSettings.Verify(path));
        }
        var duplicate = XDocument.Parse(baseline);
        duplicate.Root!.Add(new XElement(duplicate.Root.Elements().First()));
        duplicate.Save(path);
        Reject(() => OfficeProfileSettings.Verify(path));
        OfficeProfileSettings.Apply(path); // Repair conflicts before any engine is started.
        OfficeProfileSettings.Verify(path); count++;
        foreach (var mode in new[] { 0, 1 })
        {
            OfficeProfileSettings.Apply(path, mode);
            OfficeProfileSettings.Verify(path, mode); count++;
            Reject(() => OfficeProfileSettings.Verify(path, 1 - mode));
        }
        File.WriteAllText(path, "<!DOCTYPE r [<!ENTITY x 'value'>]><r>&x;</r>");
        Reject(() => OfficeProfileSettings.Apply(path));
        File.WriteAllText(path, "<unrelated/>");
        Reject(() => OfficeProfileSettings.Apply(path));
        File.WriteAllText(path, baseline);
        var retained = XDocument.Load(path);
        retained.Root!.Add(new XElement("item", new XAttribute(registry + "path", "/authored-unrelated"),
            new XElement("prop", new XAttribute(registry + "name", "Keep"), new XElement("value", "retained"))));
        retained.Save(path);
        OfficeProfileSettings.Apply(path);
        if (!XDocument.Load(path).Descendants("prop").Any(property => (string?)property.Attribute(registry + "name") == "Keep"))
            throw new InvalidDataException("Unrelated initialized profile setting was lost.");
        count++;
        Console.WriteLine($"PASS: {count} profile declaration contracts, including changed values, duplicate conflicts, recalculation modes and XML refusal.");

        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { count++; return; }
            catch (XmlException) { count++; return; }
            throw new InvalidOperationException("Expected invalid profile declaration refusal.");
        }
    }
}
