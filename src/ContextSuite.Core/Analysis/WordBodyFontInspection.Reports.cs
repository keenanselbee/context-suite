using System.Collections.Immutable;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public sealed partial record WordBodyFontInspection
{
    // Call only on source evidence retained under the application's original lease.
    // Unknown reports remain intact; this never changes the renderer's raw report.
    public ImmutableArray<string> KeepActiveReports(ImmutableArray<string> reports) =>
        !Available || TextRuns == 0 || ResolvedRuns != TextRuns || !CoverageIssues.IsEmpty || InactiveRevisionFamilies.IsDefaultOrEmpty
            ? reports : reports.Where(family => !InactiveRevisionFamilies.Contains(family, StringComparer.OrdinalIgnoreCase)).ToImmutableArray();

    private static ImmutableArray<string> InactiveFamilies(XElement document, XElement body, XElement? styles, XElement? theme,
        WordBodyFontInspection result)
    {
        if (!result.Available || result.TextRuns == 0 || result.ResolvedRuns != result.TextRuns || !result.CoverageIssues.IsEmpty ||
            document.Elements().Any(element => element != body))
            return [];
        // A closed main-story profile avoids inferring absence from an unknown
        // Word element. Ordinary body evidence outside this profile remains useful.
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "body", "p", "pPr", "pStyle", "spacing", "ind", "jc", "r", "rPr", "rStyle", "rFonts", "sz", "szCs",
            "b", "bCs", "i", "iCs", "color", "u", "strike", "dStrike", "caps", "smallCaps", "kern", "position",
            "vertAlign", "highlight", "lang", "cs", "rtl", "t", "sectPr", "pgSz", "pgMar", "ins", "moveTo"
        };
        if (body.DescendantsAndSelf().Any(element => !Discarded(element) &&
            (element.Name.NamespaceName != Word || !known.Contains(element.Name.LocalName))))
            return [];
        var inactive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeDeclarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { body, styles, theme }.OfType<XElement>())
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name == W + "rFonts")
                foreach (var name in new[] { "ascii", "hAnsi", "eastAsia", "cs" })
                {
                    var value = (string?)element.Attribute(W + name);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (root == body && Discarded(element) && value.Length <= 128 && !value.Any(char.IsControl))
                        inactive.Add(value);
                    else if (!Discarded(element)) activeDeclarations.Add(value);
                }
            else if (element.Name.NamespaceName == Drawing && element.Attribute("typeface") is { } typeface)
                activeDeclarations.Add(typeface.Value);
        }
        // Even unused live style declarations, non-text run properties and theme
        // alternatives disqualify a family. Only explicit revision-only names qualify.
        inactive.ExceptWith(activeDeclarations);
        inactive.ExceptWith(result.Families);
        return inactive.Count > 64 ? [] : inactive.Order(StringComparer.Ordinal).ToImmutableArray();
    }
}
