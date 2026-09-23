using System.Xml.Linq;
using ContextSuite.Core.Analysis;

internal static partial class DocumentAnalysisContracts
{
    private static async Task ExcelDdeContractsAsync(Action<bool, string> check)
    {
        const string sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string prefix = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
        foreach (var ns in new[] { sheet, "http://purl.oclc.org/ooxml/spreadsheetml/main" })
        foreach (var useDefault in new[] { false, true })
        {
            var declaration = useDefault ? $"<Default Extension=\"ddepart\" ContentType=\"{prefix}externalLink+xml\"/>" :
                $"<Override PartName=\"/unusual/unused.ddepart\" ContentType=\"{prefix}externalLink+xml\"/>";
            await Inspect(Parts(declaration, "unusual/unused.ddepart", $"<externalLink xmlns=\"{ns}\"><ddeLink ddeService=\"ContextSuiteDdeTest\" ddeTopic=\"AuthoredData\"/></externalLink>"),
                true, true, "DDE declaration under " + ns + "/default=" + useDefault);
        }
        foreach (var formula in new[] { "DDE(\"Service\",\"Topic\",\"Item\")", "SUM(1,dDe (\"s\",\"t\",\"i\"))",
            "_xlfn.DDE(\"s\",\"t\",\"i\")", "IF(TRUE,DDE(\"s\",\"t\",\"i\"),0)" })
        foreach (var named in new[] { false, true })
        {
            var element = new XElement(XName.Get(named ? "definedName" : "f", sheet), formula).ToString();
            var bytes = named ? Zip(OpenXmlParts("xlsx").Select(part => part.Name == "content/main.xml"
                ? (part.Name, part.Text.Replace("</workbook>", "<definedNames>" + element + "</definedNames></workbook>")) : part).ToArray())
                : Parts($"<Override PartName=\"/formula.xml\" ContentType=\"{prefix}worksheet+xml\"/>",
                    "formula.xml", $"<worksheet xmlns=\"{sheet}\">{element}</worksheet>");
            await Inspect(bytes, true, true, "DDE expression/name: " + formula);
        }
        foreach (var formula in new[] { "SUM(A1:A2)", "MYDDE(1)", "My.DDE(1)", "\"DDE(\"", "\"escaped \"\" DDE(\"",
            "'DDE('!A1", "'Escaped '' DDE('!A1", "DDE_LABEL+1" })
            await Inspect(Parts($"<Override PartName=\"/formula.xml\" ContentType=\"{prefix}worksheet+xml\"/>",
                "formula.xml", new XElement(XName.Get("worksheet", sheet), new XElement(XName.Get("f", sheet), formula)).ToString()),
                true, false, "ordinary formula or quoted DDE-looking text: " + formula);
        await Inspect(Parts($"<Override PartName=\"/link.xml\" ContentType=\"{prefix}externalLink+xml\"/>", "link.xml",
            $"<externalLink xmlns=\"{sheet}\"><externalBook/></externalLink>"), true, false, "ordinary external workbook is not DDE");
        foreach (var xml in new[] { "<externalLink", $"<!DOCTYPE externalLink [<!ENTITY x 'DDE'>]><externalLink xmlns=\"{sheet}\">&x;</externalLink>",
            $"<externalLink xmlns=\"{sheet}\">" + new string(' ', 256 * 1024) + "</externalLink>",
            string.Concat(Enumerable.Repeat("<n>", 34)) + string.Concat(Enumerable.Repeat("</n>", 34)) })
            await Inspect(Parts($"<Override PartName=\"/link.xml\" ContentType=\"{prefix}externalLink+xml\"/>", "link.xml", xml),
                false, false, "unavailable DDE inspection stays unknown");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var input = new MemoryStream(OpenXml("xlsx")); input.Position = 3;
        try { await ExcelDdeInspection.ReadAsync(input, canceled.Token); check(false, "DDE inspection swallowed cancellation"); }
        catch (OperationCanceledException) { check(input.Position == 3, "DDE inspection cancellation preserves source position"); }

        byte[] Parts(string declaration, string name, string xml)
        {
            var parts = OpenXmlParts("xlsx").ToArray();
            parts[0] = (parts[0].Name, parts[0].Text.Replace("</Types>", declaration + "</Types>"));
            return Zip([.. parts, (name, xml)]);
        }
        async Task Inspect(byte[] bytes, bool complete, bool dde, string label)
        {
            using var source = new MemoryStream(bytes, writable: false); source.Position = 3;
            var result = await ExcelDdeInspection.ReadAsync(source, default);
            check(result.Complete == complete && result.HasDde == dde && source.Position == 3 && source.ToArray().SequenceEqual(bytes), label);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("workbook.xlsx", source, default);
            check(preflight.Analysis.Identity.FormatId == "xlsx" && (dde
                ? preflight.Refusal?.Contains("DDE links") == true && preflight.Refusal.Contains("Analyze remains available")
                : preflight.Refusal is null), "DDE preflight: " + label);
        }
    }
}
