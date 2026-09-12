using System.Collections.Immutable;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public static partial class DocumentAnalysis
{
    private static void AddWorkbookDeclarations(XElement workbook, ImmutableArray<AnalysisFact>.Builder facts,
        ImmutableArray<string>.Builder warnings, CancellationToken token)
    {
        const string compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var requiresCompatibilityProcessing = false;
        foreach (var element in workbook.DescendantsAndSelf())
        {
            token.ThrowIfCancellationRequested();
            if (element.Name.NamespaceName == compatibility ||
                element.Attributes().Any(attribute => attribute.Name.NamespaceName == compatibility && attribute.Name.LocalName != "Ignorable"))
                requiresCompatibilityProcessing = true;
        }

        ReadGroup("workbookPr", "date", [
            ("date1904", "date-1904", "1904 date-base declaration (date1904)"),
            ("dateCompatibility", "date-compatibility", "Compatibility date-base declaration (dateCompatibility)")]);
        ReadGroup("calcPr", "calculation", [
            ("calcMode", "calculation-mode", "Calculation mode declaration"),
            ("fullCalcOnLoad", "full-calculation-on-load", "Full calculation on load requested"),
            ("forceFullCalc", "force-full-calculation", "Forced full calculation requested"),
            ("calcOnSave", "calculation-on-save", "Calculation on save requested"),
            ("iterate", "iterative-calculation", "Iterative calculation enabled (declaration)"),
            ("fullPrecision", "full-precision", "Full-precision calculation enabled (declaration)")]);
        facts.Add(new("document.workbook-settings-scope", "Workbook", "Workbook settings inspection scope",
            Text: "Saved workbook XML declarations only; missing attributes are not supplied with defaults. " +
                "dateCompatibility can override date1904. No cell dates, formulas, cached results, sheet settings or renderer behavior were checked."));

        void ReadGroup(string elementName, string description, (string Attribute, string Id, string Label)[] fields)
        {
            var parsed = ImmutableArray.CreateBuilder<AnalysisFact>();
            try
            {
                if (requiresCompatibilityProcessing) throw new InvalidDataException("Unsupported compatibility processing.");
                XElement? declaration = null;
                foreach (var element in workbook.Descendants())
                {
                    token.ThrowIfCancellationRequested();
                    if (element.Name.LocalName != elementName) continue;
                    if (declaration is not null || element.Parent != workbook || element.Name.Namespace != workbook.Name.Namespace ||
                        element.HasElements || !string.IsNullOrWhiteSpace(element.Value))
                        throw new InvalidDataException("Ambiguous workbook settings element.");
                    declaration = element;
                }
                foreach (var field in fields)
                {
                    token.ThrowIfCancellationRequested();
                    var fact = new AnalysisFact("document.workbook-" + field.Id, "Workbook", field.Label);
                    if (declaration?.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.LocalName == field.Attribute &&
                        attribute.Name.NamespaceName.Length != 0) == true)
                        throw new InvalidDataException("Qualified lookalike workbook setting.");
                    var value = ((string?)declaration?.Attribute(field.Attribute))?.Trim(' ', '\t', '\r', '\n');
                    if (value is null) parsed.Add(fact with { Availability = FactAvailability.NotEncoded });
                    else if (field.Attribute == "calcMode")
                    {
                        var mode = value switch
                        {
                            "auto" => "Automatic",
                            "manual" => "Manual",
                            "autoNoTable" => "Automatic except data tables",
                            _ => throw new InvalidDataException("Unsupported calculation mode.")
                        };
                        parsed.Add(fact with { Text = mode });
                    }
                    else
                    {
                        var boolean = value switch
                        {
                            "1" or "true" => true,
                            "0" or "false" => false,
                            _ => throw new InvalidDataException("Unsupported Boolean workbook setting.")
                        };
                        parsed.Add(fact with { Boolean = boolean });
                    }
                }
                // Commit a complete group only: an invalid later flag must not
                // leave a partially trusted mode or a misleading default value.
                facts.AddRange(parsed);
            }
            catch (InvalidDataException)
            {
                foreach (var field in fields)
                    facts.Add(new("document.workbook-" + field.Id, "Workbook", field.Label, Availability: FactAvailability.Unavailable));
                warnings.Add($"Workbook {description} declarations are unavailable: the settings are ambiguous or outside the supported XML scope.");
            }
        }
    }
}
