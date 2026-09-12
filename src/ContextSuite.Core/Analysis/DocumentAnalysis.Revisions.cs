using System.Collections.Immutable;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public static partial class DocumentAnalysis
{
    private static void AddWordRevisionDeclarations(XElement document, ImmutableArray<AnalysisFact>.Builder facts,
        ImmutableArray<string>.Builder warnings, CancellationToken token)
    {
        const string compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var counts = new int[4];
        var unavailable = false;
        foreach (var element in document.DescendantsAndSelf())
        {
            token.ThrowIfCancellationRequested();
            if (element.Name.NamespaceName == compatibility || element.Attributes().Any(attribute =>
                attribute.Name.NamespaceName == compatibility && attribute.Name.LocalName != "Ignorable"))
                unavailable = true;
            if (element.Name.Namespace != document.Name.Namespace) continue;
            var index = element.Name.LocalName switch { "ins" => 0, "del" => 1, "moveFrom" => 2, "moveTo" => 3, _ => -1 };
            if (index >= 0) counts[index]++;
        }
        var fields = new[] { ("insertions", "Insertion markers"), ("deletions", "Deletion markers"),
            ("move-sources", "Move-source markers"), ("move-destinations", "Move-destination markers") };
        for (var index = 0; index < fields.Length; index++)
            facts.Add(new("document.word-revisions-" + fields[index].Item1, "Word revisions", fields[index].Item2,
                Integer: unavailable ? null : counts[index], Availability: unavailable ? FactAvailability.Unavailable : FactAvailability.Explicit));
        if (unavailable)
            warnings.Add("Word revision counts are unavailable: the main XML requires unsupported compatibility processing.");
        facts.Add(new("document.word-revisions-scope", "Word revisions", "Revision inspection scope",
            Text: "Counts of ins, del, moveFrom and moveTo elements in the main document XML only; not a count of edits or validation of revision structure. " +
                "Headers, footers, notes, comments, formatting changes and display settings were not scanned for revisions. Zero does not mean the whole document is revision-free."));
    }
}
