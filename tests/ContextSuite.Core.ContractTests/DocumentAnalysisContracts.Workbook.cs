using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static partial class DocumentAnalysisContracts
{
    private static async Task WorkbookSettingsContractsAsync(string scratch, Action<bool, string> check)
    {
        const string prefix = "document.workbook-";
        const string mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        foreach (var strict in new[] { false, true })
        {
            foreach (var (value, expected) in new[] { ("true", true), ("1", true), ("false", false), ("0", false), (" &#x9;1&#xA; ", true) })
            {
                var result = await AnalyzeAsync(Package($"<workbookPr date1904=\"{value}\" dateCompatibility=\"{value}\"/>",
                    $"<calcPr fullCalcOnLoad=\"{value}\" forceFullCalc=\"{value}\" calcOnSave=\"{value}\" iterate=\"{value}\" fullPrecision=\"{value}\"/>", strict));
                var flags = Settings(result).Where(fact => fact.Id != prefix + "calculation-mode").ToArray();
                check(result.Identity.FormatId == "xlsx" && result.Identity.Confidence == IdentificationConfidence.Likely &&
                    flags.Length == 7 && flags.All(fact => fact.Boolean == expected && fact.Text is null && fact.Availability == FactAvailability.Explicit),
                    $"workbook settings: {(strict ? "Strict" : "Transitional")} typed XML Boolean {value}");
            }
            foreach (var (value, expected) in new[] { ("auto", "Automatic"), ("manual", "Manual"), ("autoNoTable", "Automatic except data tables") })
            {
                var result = await AnalyzeAsync(Package(calculation: $"<calcPr calcMode=\"{value}\"/>", strict: strict));
                check(Fact(result, "calculation-mode").Text == expected && Fact(result, "calculation-mode").Availability == FactAvailability.Explicit,
                    $"workbook settings: {(strict ? "Strict" : "Transitional")} calculation mode {value}");
            }
            foreach (var empty in new[] { false, true })
            {
                var result = await AnalyzeAsync(Package(empty ? "<workbookPr/>" : "", empty ? "<calcPr/>" : "", strict));
                check(Settings(result).Count() == 8 && Settings(result).All(fact => fact.Availability == FactAvailability.NotEncoded &&
                    fact.Text is null && fact.Boolean is null && fact.Integer is null),
                    $"workbook settings: missing attributes stay not encoded, not inferred defaults ({strict}, {empty})");
            }
        }

        var overridden = await AnalyzeAsync(Package("<workbookPr date1904=\"true\" dateCompatibility=\"false\"/>", "<calcPr calcMode=\"manual\"/>"));
        check(Fact(overridden, "date-1904").Boolean == true && Fact(overridden, "date-compatibility").Boolean == false &&
            overridden.Facts.Single(fact => fact.Id == "document.workbook-settings-scope").Text!.Contains("override date1904") &&
            Settings(overridden).All(fact => fact.Availability != FactAvailability.Derived),
            "workbook settings: interacting date declarations are retained without inventing effective dates");

        foreach (var value in new[] { "", "True", "FALSE", "yes", "2", "-1", "0 1", "&#xA0;1" })
        {
            var badDate = await AnalyzeAsync(Package($"<workbookPr date1904=\"true\" dateCompatibility=\"{value}\"/>", "<calcPr calcMode=\"manual\"/>"));
            check(DateUnavailable(badDate) && Fact(badDate, "calculation-mode").Text == "Manual" && badDate.Identity.FormatId == "xlsx",
                "workbook settings: invalid later date flag discards the date group only: " + value);
            var badCalc = await AnalyzeAsync(Package("<workbookPr date1904=\"false\"/>", $"<calcPr calcMode=\"auto\" fullPrecision=\"{value}\"/>"));
            check(CalculationUnavailable(badCalc) && Fact(badCalc, "date-1904").Boolean == false && badCalc.Identity.FormatId == "xlsx",
                "workbook settings: invalid later calculation flag discards the calculation group only: " + value);
        }
        foreach (var mode in new[] { "AUTO", "automatic", "", "unknown-private-marker-" + new string('x', 10000) })
        {
            var result = await AnalyzeAsync(Package(calculation: $"<calcPr calcMode=\"{mode}\"/>"));
            check(CalculationUnavailable(result) && result.Identity.FormatId == "xlsx" &&
                result.Facts.All(fact => fact.Text?.Contains("unknown-private-marker") != true) &&
                result.Warnings.All(warning => !warning.Contains("unknown-private-marker")),
                "workbook settings: unknown calculation values are unavailable without echoing file content (length " + mode.Length + ")");
        }
        foreach (var properties in new[]
        {
            "<workbookPr date1904=\"1\"/><workbookPr date1904=\"1\"/>",
            "<workbookPr date1904=\"1\"/><workbookPr date1904=\"0\"/>",
            "<holder><workbookPr date1904=\"1\"/></holder>",
            "<workbookPr xmlns=\"urn:other\" date1904=\"1\"/>",
            "<workbookPr xmlns:x=\"urn:other\" x:date1904=\"1\"/>",
            "<workbookPr><child/></workbookPr>",
            "<workbookPr>not-empty</workbookPr>"
        })
        {
            var result = await AnalyzeAsync(Package(properties, "<calcPr calcMode=\"auto\"/>"));
            check(DateUnavailable(result) && Fact(result, "calculation-mode").Text == "Automatic" &&
                result.Identity.FormatId == "xlsx" && result.Warnings.Any(warning => warning.StartsWith("Workbook date declarations")),
                "workbook settings: ambiguous date structure preserves other facts: " + properties);
        }
        foreach (var calculation in new[]
        {
            "<calcPr calcMode=\"auto\"/><calcPr calcMode=\"manual\"/>",
            "<holder><calcPr calcMode=\"manual\"/></holder>",
            "<calcPr xmlns=\"urn:other\" calcMode=\"auto\"/>",
            "<calcPr xmlns:x=\"urn:other\" x:calcMode=\"auto\"/>",
            "<calcPr><child/></calcPr>",
            "<calcPr>not-empty</calcPr>"
        })
        {
            var result = await AnalyzeAsync(Package("<workbookPr date1904=\"1\"/>", calculation));
            check(CalculationUnavailable(result) && Fact(result, "date-1904").Boolean == true &&
                result.Identity.FormatId == "xlsx" && result.Warnings.Any(warning => warning.StartsWith("Workbook calculation declarations")),
                "workbook settings: ambiguous calculation structure preserves other facts: " + calculation);
        }
        foreach (var directive in new[]
        {
            $"<mc:AlternateContent xmlns:mc=\"{mc}\"><mc:Fallback><workbookPr date1904=\"1\"/></mc:Fallback></mc:AlternateContent>",
            $"<holder xmlns:mc=\"{mc}\" mc:ProcessContent=\"x:item\" xmlns:x=\"urn:other\"/>"
        })
        {
            var result = await AnalyzeAsync(Package(directive, "<calcPr calcMode=\"manual\"/>"));
            check(DateUnavailable(result) && CalculationUnavailable(result) && result.Identity.FormatId == "xlsx",
                "workbook settings: unresolved compatibility processing cannot yield complete declarations");
        }
        var ignorable = await AnalyzeAsync(Package("<workbookPr xmlns:date1904=\"urn:harmless-prefix\" date1904=\"1\"/>",
            "<calcPr calcMode=\"manual\"/>", attributes: $"xmlns:mc=\"{mc}\" xmlns:x=\"urn:extension\" mc:Ignorable=\"x\""));
        check(Fact(ignorable, "date-1904").Boolean == true && Fact(ignorable, "calculation-mode").Text == "Manual",
            "workbook settings: an Ignorable attribute or namespace prefix alone does not change literal declarations");
        foreach (var id in new[] { "docx", "pptx" })
        {
            var result = await AnalyzeAsync(OpenXml(id));
            check(!Settings(result).Any(), "workbook settings: no spreadsheet flags invented for " + id);
        }

        var root = Path.Combine(scratch, "workbook-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var bytes = Package("<workbookPr date1904=\"0\"/>", "<calcPr calcMode=\"manual\" fullCalcOnLoad=\"1\"/>");
        var path = Path.Combine(root, "misleading.png");
        await File.WriteAllBytesAsync(path, bytes);
        var before = SHA256.HashData(bytes);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var read = await FileAnalysisReader.ReadAsync(path, CancellationToken.None);
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        check(read.Identity.FormatId == "xlsx" && Fact(read, "calculation-mode").Text == "Manual" &&
            Fact(read, "full-calculation-on-load").Boolean == true && read.InspectedBytes <= bytes.Length &&
            before.SequenceEqual(after) && timestamp == File.GetLastWriteTimeUtc(path),
            "workbook settings: real reader handles a misleading image name and preserves source bytes/timestamp");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await AnalyzeAsync(bytes, cancellationToken: canceled.Token); check(false, "Workbook cancellation was swallowed."); }
        catch (OperationCanceledException) { check(true, "workbook settings: cancellation propagates without a partial result"); }

        static AnalysisFact Fact(FileAnalysis result, string suffix) => result.Facts.Single(fact => fact.Id == prefix + suffix);
        static IEnumerable<AnalysisFact> Settings(FileAnalysis result) => result.Facts.Where(fact => fact.Id.StartsWith(prefix) && fact.Id != prefix + "settings-scope");
        static bool DateUnavailable(FileAnalysis result)
        {
            var values = Settings(result).Where(fact => fact.Id.StartsWith(prefix + "date-")).ToArray();
            return values.Length == 2 && values.All(fact => fact.Availability == FactAvailability.Unavailable && fact.Text is null && fact.Boolean is null);
        }
        static bool CalculationUnavailable(FileAnalysis result)
        {
            var values = Settings(result).Where(fact => !fact.Id.StartsWith(prefix + "date-")).ToArray();
            return values.Length == 6 && values.All(fact => fact.Availability == FactAvailability.Unavailable && fact.Text is null && fact.Boolean is null);
        }
        static byte[] Package(string properties = "", string calculation = "", bool strict = false, string attributes = "") =>
            Zip(OpenXmlParts("xlsx", strict).Select(part => part.Name == "content/main.xml" ?
                (part.Name, part.Text.Replace("<workbook ", "<workbook " + attributes + " ").Replace("<sheets>", properties + "<sheets>")
                    .Replace("</workbook>", calculation + "</workbook>")) : part).ToArray());
    }
}
