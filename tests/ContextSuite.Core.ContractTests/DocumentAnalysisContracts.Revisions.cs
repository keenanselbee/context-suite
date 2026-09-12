using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static partial class DocumentAnalysisContracts
{
    private static async Task WordRevisionContractsAsync(string scratch, Action<bool, string> check)
    {
        const string prefix = "document.word-revisions-";
        const string compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        foreach (var strict in new[] { false, true })
        {
            var empty = await AnalyzeAsync(Package("<p/>", strict));
            check(Counts(empty).Length == 4 && Counts(empty).All(fact => fact.Integer == 0 && fact.Availability == FactAvailability.Explicit) &&
                empty.Facts.Single(fact => fact.Id == prefix + "scope").Text!.Contains("Zero does not mean the whole document is revision-free"),
                "word revisions: zero is scoped to four main-part markers, namespace strict=" + strict);
            var mixed = await AnalyzeAsync(Package("<p><ins><r><t>private inserted text</t></r><ins/></ins><del/><moveFrom/><moveTo/><moveTo/></p>", strict));
            check(mixed.Identity.FormatId == "docx" && mixed.Identity.Confidence == IdentificationConfidence.Likely &&
                Counts(mixed).Select(fact => fact.Integer).SequenceEqual(new long?[] { 2, 1, 1, 2 }) &&
                Counts(mixed).All(fact => fact.Availability == FactAvailability.Explicit && fact.Text is null && fact.Boolean is null),
                "word revisions: typed counts of nested declarations are not conflated with edits, namespace strict=" + strict);
            check(mixed.Facts.All(fact => fact.Text?.Contains("private inserted text") != true) && mixed.Warnings.All(value => !value.Contains("private inserted text")),
                "word revisions: text and personal revision attributes are not exposed, namespace strict=" + strict);
            var ns = strict ? "http://purl.oclc.org/ooxml/wordprocessingml/main" : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var foreign = await AnalyzeAsync(Package($"<p><ins/><other:del xmlns:other=\"urn:other\"/><moveFromRangeStart/><rPrChange/><INS/><other:moveTo xmlns:other=\"urn:other\"/></p>", strict));
            check(Counts(foreign).Select(fact => fact.Integer).SequenceEqual(new long?[] { 1, 0, 0, 0 }),
                "word revisions: foreign lookalikes, case variants, ranges and formatting markers do not inflate supported counts, namespace strict=" + strict);
            var namespaced = await AnalyzeAsync(Package($"<p><r:del xmlns:r=\"{ns}\"/><r:moveFrom xmlns:r=\"{ns}\"/></p>", strict));
            check(Counts(namespaced).Select(fact => fact.Integer).SequenceEqual(new long?[] { 0, 1, 1, 0 }),
                "word revisions: namespace identity is independent of prefixes, namespace strict=" + strict);
        }
        foreach (var markup in new[]
        {
            $"<mc:AlternateContent xmlns:mc=\"{compatibility}\"><mc:Choice Requires=\"x\" xmlns:x=\"urn:x\"><ins/></mc:Choice><mc:Fallback><del/></mc:Fallback></mc:AlternateContent>",
            $"<p xmlns:mc=\"{compatibility}\" xmlns:x=\"urn:x\" mc:ProcessContent=\"x:item\"><ins/></p>",
            $"<p xmlns:mc=\"{compatibility}\" xmlns:x=\"urn:x\" mc:MustUnderstand=\"x\"><ins/></p>"
        })
        {
            var result = await AnalyzeAsync(Package("<p><ins/></p>" + markup));
            check(result.Identity.FormatId == "docx" && Counts(result).Length == 4 && Counts(result).All(fact =>
                fact.Availability == FactAvailability.Unavailable && fact.Integer is null) &&
                result.Warnings.Any(value => value.StartsWith("Word revision counts are unavailable")),
                "word revisions: unresolved compatibility processing discards partial counts while retaining identity");
        }
        var ignorable = await AnalyzeAsync(Package("<p><ins/></p>", attributes: $"xmlns:mc=\"{compatibility}\" xmlns:x=\"urn:x\" mc:Ignorable=\"x\""));
        check(Counts(ignorable)[0].Integer == 1, "word revisions: Ignorable alone does not prevent literal marker counts");
        var parts = OpenXmlParts("docx");
        var extra = Zip([.. parts, ("word/header1.xml", "<ins>not inspected</ins>"),
            ("word/settings.xml", "<revisionView insDel=\"true\"/>"), ("word/comments.xml", "<del>not inspected</del>")]);
        var outside = await AnalyzeAsync(extra);
        check(Counts(outside).All(fact => fact.Integer == 0) && outside.Facts.Single(fact => fact.Id == prefix + "scope").Text!.Contains("Headers, footers"),
            "word revisions: unselected header/comment/settings parts cannot become a whole-document revision scan");
        foreach (var family in new[] { "xlsx", "pptx" })
            check(Counts(await AnalyzeAsync(OpenXml(family))).Length == 0, "word revisions: no Word revision counts invented for " + family);
        check(Counts(await AnalyzeAsync(OpenDocument("odt"))).Length == 0, "word revisions: OpenDocument does not receive unperformed counts");
        foreach (var bad in new[] { "<p><ins></p>", "<p>" + new string('x', 270000) + "<ins/></p>" })
        {
            var result = await AnalyzeAsync(Package(bad));
            check(result.Identity.FormatId == "zip" && Counts(result).Length == 0, "word revisions: malformed or over-budget main XML retains fallback without counts");
        }
        var large = await AnalyzeAsync(Package("<p>" + string.Concat(Enumerable.Repeat("<ins/>", 10000)) + "</p>"));
        check(Counts(large)[0].Integer == 10000, "word revisions: bounded large declaration list is counted without per-marker facts");
        var root = Path.Combine(scratch, "word-revisions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var bytes = Package("<p><del/><ins/></p>");
        var path = Path.Combine(root, "renamed.png");
        await File.WriteAllBytesAsync(path, bytes);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var resultRead = await FileAnalysisReader.ReadAsync(path, CancellationToken.None);
        var original = await File.ReadAllBytesAsync(path);
        check(resultRead.Identity.FormatId == "docx" && Counts(resultRead).Select(fact => fact.Integer).SequenceEqual(new long?[] { 1, 1, 0, 0 }) &&
            SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(original)) && timestamp == File.GetLastWriteTimeUtc(path),
            "word revisions: real file reader reports declarations under a misleading name and preserves source bytes/time");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await AnalyzeAsync(bytes, cancellationToken: canceled.Token); check(false, "Word revision cancellation was swallowed."); }
        catch (OperationCanceledException) { check(true, "word revisions: cancellation propagates without partial facts"); }

        static AnalysisFact[] Counts(FileAnalysis result) => result.Facts.Where(fact => fact.Id.StartsWith(prefix) && fact.Id != prefix + "scope").ToArray();
        static byte[] Package(string body, bool strict = false, string attributes = "") =>
            Zip(OpenXmlParts("docx", strict).Select(part => part.Name == "content/main.xml" ?
                (part.Name, part.Text.Replace("<document ", "<document " + attributes + " ").Replace("<p/>", body)) : part).ToArray());
    }
}
