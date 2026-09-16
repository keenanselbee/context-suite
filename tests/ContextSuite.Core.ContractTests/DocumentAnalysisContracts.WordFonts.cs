using ContextSuite.Core.Analysis;
using System.Text;

internal static partial class DocumentAnalysisContracts
{
    private static async Task WordBodyFontsAsync(Action<bool, string> check)
    {
        const string word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        const string type = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
        const string theme = """
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:themeElements><a:fontScheme name="Authored">
                <a:majorFont><a:latin typeface="Major Family"/><a:ea typeface="Unused East Asia"/></a:majorFont>
                <a:minorFont><a:latin typeface="Minor Family"/></a:minorFont>
              </a:fontScheme></a:themeElements>
            </a:theme>
            """;
        const string defaults = "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii='Default Family' w:hAnsi='Latin Family'/></w:rPr></w:rPrDefault></w:docDefaults>";
        static string Run(string properties = "", string text = "Visible text") => $"<w:r><w:rPr>{properties}</w:rPr><w:t>{text}</w:t></w:r>";
        static string Paragraph(string runs, string properties = "") => $"<w:p><w:pPr>{properties}</w:pPr>{runs}</w:p>";
        static string Style(string id, string kind, string properties, string parent = "", string extra = "") =>
            $"<w:style w:styleId='{id}' w:type='{kind}' {extra}>{(parent.Length == 0 ? "" : $"<w:basedOn w:val='{parent}'/>")}<w:rPr>{properties}</w:rPr></w:style>";
        (string Name, string Text)[] Parts(string body, string styles = defaults, string themeXml = theme) =>
        [
            ("[Content_Types].xml", $"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/word/main.xml' ContentType='{type}document.main+xml'/><Override PartName='/style-set.xml' ContentType='{type}styles+xml'/><Override PartName='/themes/main.xml' ContentType='application/vnd.openxmlformats-officedocument.theme+xml'/></Types>"),
            ("_rels/.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='main' Type='{rel}officeDocument' Target='word/main.xml'/></Relationships>"),
            ("word/main.xml", $"<w:document xmlns:w='{word}'><w:body>{body}</w:body></w:document>"),
            ("word/_rels/main.xml.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='styles' Type='{rel}styles' Target='../style-set.xml'/><Relationship Id='theme' Type='{rel}theme' Target='../themes/main.xml'/></Relationships>"),
            ("style-set.xml", $"<w:styles xmlns:w='{word}'>{styles}</w:styles>"),
            ("themes/main.xml", themeXml)
        ];
        async Task<WordBodyFontInspection> Inspect((string Name, string Text)[] parts)
        {
            var bytes = Zip(parts);
            using var input = new MemoryStream(bytes, false);
            input.Position = 7;
            var result = await WordBodyFontInspection.ReadAsync(input, default);
            check(input.Position == 7 && input.CanRead && input.ToArray().SequenceEqual(bytes),
                "Word used fonts: read lease position, bytes and ownership preserved");
            return result;
        }
        async Task Expect(string label, string body, string styles, params string[] expected)
        {
            var result = await Inspect(Parts(body, styles));
            check(result.Available && result.TextRuns > 0 && result.TextRuns == result.ResolvedRuns &&
                result.CoverageIssues.IsEmpty && result.Families.SequenceEqual(expected.Order(StringComparer.Ordinal)),
                "Word used fonts: " + label);
        }
        await Expect("document defaults", Paragraph(Run()), defaults, "Default Family");
        await Expect("Latin text chooses only its used slots", Paragraph(Run(text: "Cafe &#xE9;")), defaults, "Default Family", "Latin Family");
        var styleSet = defaults +
            Style("Base", "paragraph", "<w:rFonts w:asciiTheme='majorHAnsi'/>") +
            Style("Body", "paragraph", "<w:rFonts w:ascii='Paragraph Family'/>", "Base", "w:default='1'") +
            Style("CharacterBase", "character", "<w:rFonts w:ascii='Character Family'/>") +
            Style("Character", "character", "<w:b/>", "CharacterBase") +
            Style("Unused", "paragraph", "<w:rFonts w:ascii='Missing Unused Family'/>", "Unused");
        await Expect("default paragraph replaces inherited theme and ignores unused cyclic style", Paragraph(Run()), styleSet, "Paragraph Family");
        await Expect("explicit paragraph style resolves theme", Paragraph(Run(), "<w:pStyle w:val='Base'/>"), styleSet, "Major Family");
        await Expect("character style chain overrides paragraph style", Paragraph(Run("<w:rStyle w:val='Character'/>")), styleSet, "Character Family");
        await Expect("direct literal overrides inherited theme", Paragraph(Run("<w:rFonts w:ascii='Direct Family'/>"), "<w:pStyle w:val='Base'/>"), styleSet, "Direct Family");
        await Expect("direct theme overrides inherited literal", Paragraph(Run("<w:rFonts w:asciiTheme='minorHAnsi'/>")), styleSet, "Minor Family");
        await Expect("theme wins on the same property element", Paragraph(Run("<w:rFonts w:ascii='Unused Literal' w:asciiTheme='majorAscii'/>")), defaults, "Major Family");
        await Expect("paragraph mark properties do not style its text", Paragraph(Run(), "<w:rPr><w:rFonts w:ascii='Mark Only'/></w:rPr>"), defaults, "Default Family");
        await Expect("old paragraph mark revisions do not alter current text", Paragraph(Run(),
            "<w:pPrChange><w:pPr><w:rPr><w:del/></w:rPr></w:pPr></w:pPrChange>"), defaults, "Default Family");
        await Expect("deleted fields do not make current text unresolved", Paragraph(
            "<w:del><w:r><w:fldChar w:fldCharType='begin'/></w:r></w:del>" + Run()), defaults, "Default Family");
        await Expect("unapplied styles and unused font table are not usage", Paragraph(Run()), defaults +
            Style("Unused", "character", "<w:rFonts w:ascii='Missing Font'/>"), "Default Family");
        await Expect("complex-script override uses cs slot", Paragraph(Run("<w:cs/><w:rFonts w:cs='Complex Family'/>")), defaults, "Complex Family");
        await Expect("direct false clears inherited rtl", Paragraph(Run("<w:rtl w:val='false'/>")), defaults +
            Style("Body", "paragraph", "<w:rtl/><w:rFonts w:cs='Unused Complex'/>", extra: "w:default='true'"), "Default Family");
        await Expect("final text ignores deleted, moved-from and old formatting", Paragraph(
            "<w:del>" + Run("<w:rFonts w:ascii='Deleted'/>") + "</w:del><w:moveFrom>" +
            Run("<w:rFonts w:ascii='Moved From'/>") + "</w:moveFrom><w:ins>" +
            Run("<w:rPrChange><w:rPr><w:rFonts w:ascii='Old Formatting'/></w:rPr></w:rPrChange>") +
            "</w:ins><w:moveTo>" + Run() + "</w:moveTo>"), defaults, "Default Family");

        foreach (var (label, body, styles) in new[]
        {
            ("missing stored defaults", Paragraph(Run()), ""),
            ("missing used style", Paragraph(Run("<w:rStyle w:val='Absent'/>")), defaults),
            ("used style cycle", Paragraph(Run()), defaults + Style("Body", "paragraph", "", "Body", "w:default='1'")),
            ("unknown theme token", Paragraph(Run("<w:rFonts w:asciiTheme='unknown'/>")), defaults),
            ("locale-sensitive text", Paragraph(Run("<w:rFonts w:hint='eastAsia'/>", "&#xE9;")), defaults),
            ("CJK script not inferred", Paragraph(Run(text: "&#x4E00;")), defaults),
            ("hidden text not inferred", Paragraph(Run("<w:vanish/>")), defaults),
            ("table inheritance not inferred", "<w:tbl><w:tr><w:tc>" + Paragraph(Run()) + "</w:tc></w:tr></w:tbl>", defaults),
            ("numbering not inferred", Paragraph(Run(), "<w:numPr/>"), defaults),
            ("field result not assumed current", Paragraph("<w:r><w:fldChar w:fldCharType='begin'/></w:r>" + Run()), defaults),
            ("deleted paragraph mark not independent", Paragraph(Run(), "<w:rPr><w:del/></w:rPr>") + Paragraph(Run()), defaults),
            ("foreign wrapper not interpreted", "<x:other xmlns:x='urn:test'>" + Paragraph(Run()) + "</x:other>", defaults)
        })
        {
            var result = await Inspect(Parts(body, styles));
            check(result.Available && result.ResolvedRuns == 0 && result.Families.IsEmpty && !result.CoverageIssues.IsEmpty,
                "Word used fonts: explicit incomplete coverage for " + label);
        }
        var partial = await Inspect(Parts(Paragraph(Run()) + "<w:tbl><w:tr><w:tc>" + Paragraph(Run()) + "</w:tc></w:tr></w:tbl>"));
        check(partial.Available && partial.TextRuns == 2 && partial.ResolvedRuns == 1 &&
            partial.Families.SequenceEqual(["Default Family"]) && !partial.CoverageIssues.IsEmpty,
            "Word used fonts: partial evidence retains its coverage gap");
        var absentHeaders = await Inspect(Parts(Paragraph(Run()) + "<w:sectPr><w:headerReference/></w:sectPr>"));
        check(absentHeaders.ResolvedRuns == 1 && absentHeaders.CoverageIssues.Contains("additional-content"),
            "Word used fonts: body evidence never claims headers were scanned");
        foreach (var (index, before, after) in new[]
        {
            (3, "../style-set.xml", "../../escape.xml"),
            (3, "../style-set.xml", "https://example.invalid/fonts.xml"),
            (3, "Id='styles'", "Id='theme'"),
            (3, "Target='../style-set.xml'", "Target='../style-set.xml' TargetMode='External'"),
            (0, type + "styles+xml", type + "fontTable+xml"),
            (2, word, "http://purl.oclc.org/ooxml/wordprocessingml/main"),
            (4, "<w:styles ", "<!DOCTYPE x [<!ENTITY bad SYSTEM 'file:///forbidden'>]><w:styles ")
        })
        {
            var parts = Parts(Paragraph(Run()));
            parts[index] = (parts[index].Name, parts[index].Text.Replace(before, after, StringComparison.Ordinal));
            var result = await Inspect(parts);
            check(!result.Available && result.Families.IsEmpty && result.CoverageIssues.SequenceEqual(["package-unavailable"]),
                "Word used fonts: unsafe, inconsistent or unsupported dependency rejected: " + after);
        }
        var oversized = await Inspect(Parts(Paragraph(Run(text: new string('x', 262144)))));
        check(!oversized.Available && oversized.Families.IsEmpty, "Word used fonts: XML part byte limit");
        var duplicated = await Inspect(Parts(Paragraph(Run()), defaults + Style("same", "paragraph", "") + Style("same", "paragraph", "")));
        check(!duplicated.Available, "Word used fonts: duplicate style identity refused");
        var duplicateProperty = await Inspect(Parts(Paragraph(Run("<w:rFonts w:ascii='One'/><w:rFonts w:ascii='Two'/>"))));
        check(!duplicateProperty.Available, "Word used fonts: ambiguous run font property refused");
        var nestedText = await Inspect(Parts(Paragraph(Run(text: "<w:t>Nested content</w:t>"))));
        check(!nestedText.Available, "Word used fonts: malformed nested text never establishes used fonts");
        var tooManyRuns = await Inspect(Parts(Paragraph(string.Concat(Enumerable.Repeat("<w:r><w:t>x</w:t></w:r>", 8193)))));
        check(!tooManyRuns.Available, "Word used fonts: run limit discards partial evidence");
        var tooManyNames = await Inspect(Parts(Paragraph(string.Concat(Enumerable.Range(0, 65).Select(i => Run($"<w:rFonts w:ascii='Family {i}'/>"))))));
        check(!tooManyNames.Available, "Word used fonts: distinct-family limit discards partial evidence");
        var deep = await Inspect(Parts(string.Concat(Enumerable.Repeat("<w:ins>", 34)) + Paragraph(Run()) +
            string.Concat(Enumerable.Repeat("</w:ins>", 34))));
        check(!deep.Available, "Word used fonts: depth checked before loading the XML tree");
        var noThemeParts = Parts(Paragraph(Run("<w:rFonts w:asciiTheme='majorAscii'/>")));
        noThemeParts[3] = (noThemeParts[3].Name, noThemeParts[3].Text.Replace(
            $"<Relationship Id='theme' Type='{rel}theme' Target='../themes/main.xml'/>", ""));
        var noTheme = await Inspect(noThemeParts);
        check(noTheme.Available && noTheme.ResolvedRuns == 0 && noTheme.Families.IsEmpty,
            "Word used fonts: orphan theme declarations do not supply an active theme");
        var cancelledBytes = Zip(Parts(Paragraph(Run())));
        var styleOffset = cancelledBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes("style-set.xml"));
        // The first occurrence is its name in the ZIP local header; part XML is compressed.
        using var duringRead = new CancellationTokenSource();
        using var interrupted = new CancelOnRead(cancelledBytes, styleOffset - 30, duringRead);
        interrupted.Position = 7;
        try { await WordBodyFontInspection.ReadAsync(interrupted, duringRead.Token); check(false, "Word used fonts: mid-read cancellation swallowed"); }
        catch (OperationCanceledException) { check(interrupted.Position == 7 && interrupted.CanRead, "Word used fonts: mid-read cancellation restores its lease position"); }
        using var source = new MemoryStream(Zip(Parts(Paragraph(Run()))), false);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await WordBodyFontInspection.ReadAsync(source, cancelled.Token); check(false, "Word used fonts: cancellation swallowed"); }
        catch (OperationCanceledException) { check(source.Position == 0, "Word used fonts: cancellation propagates"); }
        var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("document.docx", source, default);
        check(preflight.Refusal is null && preflight.WordFonts is { Available: true, ResolvedRuns: 1 } &&
            preflight.WordFonts.Families.SequenceEqual(["Default Family"]),
            "Word used fonts: Office preflight carries evidence without changing admission");
        var deleted = "<w:del>" + Run("<w:rFonts w:ascii='Deleted Family'/>") + "</w:del>";
        var inactive = await Inspect(Parts(Paragraph(deleted + Run())));
        check(inactive.InactiveRevisionFamilies.SequenceEqual(["Deleted Family"]) &&
            inactive.KeepActiveReports(["deleted family", "Default Family", "Unknown Family"]).SequenceEqual(["Default Family", "Unknown Family"]),
            "Word font review: only the proven inactive revision name is removed, preserving unknown and active reports");
        foreach (var (label, body, styles) in new[]
        {
            ("live font", Paragraph(deleted + Run("<w:rFonts w:ascii='Deleted Family'/>")), defaults),
            ("non-text run declaration", Paragraph(deleted + Run() + "<w:r><w:rPr><w:rFonts w:ascii='Deleted Family'/></w:rPr></w:r>"), defaults),
            ("paragraph mark declaration", Paragraph(deleted + Run(), "<w:rPr><w:rFonts w:ascii='Deleted Family'/></w:rPr>"), defaults),
            ("unused live style declaration", Paragraph(deleted + Run()), defaults + Style("Unused", "character", "<w:rFonts w:ascii='Deleted Family'/>")),
            ("unresolved text", Paragraph(deleted + Run(text: "&#x4E00;")), defaults),
            ("unknown body element", Paragraph(deleted + Run() + "<w:unknown/>"), defaults),
            ("header reference", Paragraph(deleted + Run()) + "<w:sectPr><w:headerReference/></w:sectPr>", defaults),
            ("field", Paragraph(deleted + Run() + "<w:r><w:fldChar w:fldCharType='begin'/></w:r>"), defaults),
            ("no live text", Paragraph(deleted), defaults)
        })
        {
            var result = await Inspect(Parts(body, styles));
            check(result.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
                "Word font review: report retained for " + label);
        }
        var activeTheme = await Inspect(Parts(Paragraph(deleted + Run()), themeXml: theme.Replace("Major Family", "Deleted Family")));
        check(activeTheme.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
            "Word font review: theme alternatives disqualify revision-only evidence");
        var unknownDependency = Parts(Paragraph(deleted + Run()));
        unknownDependency[3] = (unknownDependency[3].Name, unknownDependency[3].Text.Replace("</Relationships>",
            $"<Relationship Id='header' Type='{rel}header' Target='header.xml'/></Relationships>"));
        var dependency = await Inspect(unknownDependency);
        check(dependency.Available && dependency.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
            "Word font review: uninspected story dependencies retain reports");
        (string Name, string Text)[] FontTable(string contents)
        {
            var parts = Parts(Paragraph(deleted + Run()));
            parts[0] = (parts[0].Name, parts[0].Text.Replace("</Types>",
                $"<Override PartName='/fonts/catalog.xml' ContentType='{type}fontTable+xml'/></Types>"));
            parts[3] = (parts[3].Name, parts[3].Text.Replace("</Relationships>",
                $"<Relationship Id='fonts' Type='{rel}fontTable' Target='../fonts/catalog.xml'/></Relationships>"));
            return [.. parts, ("fonts/catalog.xml", $"<w:fonts xmlns:w='{word}'>{contents}</w:fonts>")];
        }
        var metrics = "<w:panose1 w:val='020B0604020202020204'/><w:charset w:val='00'/><w:family w:val='swiss'/>" +
            "<w:pitch w:val='variable'/><w:sig w:usb0='00000001' w:usb1='00000000' w:usb2='00000000' w:usb3='00000000' w:csb0='00000001' w:csb1='00000000'/>";
        var plainTable = $"<w:font w:name='Default Family'>{metrics}</w:font><w:font w:name='Deleted Family'/>";
        var withTable = await Inspect(FontTable(plainTable));
        check(withTable.Available && withTable.Families.SequenceEqual(["Default Family"]) &&
            withTable.KeepActiveReports(["Deleted Family", "Default Family", "Unknown"]).SequenceEqual(["Default Family", "Unknown"]),
            "Word font review: ordinary relationship-selected table metrics do not turn deleted-only declarations into usage");
        foreach (var (label, contents) in new[]
        {
            ("alias", "<w:font w:name='Default Family'><w:altName w:val='Deleted Family,Other'/></w:font>"),
            ("embedded regular", "<w:font w:name='Default Family'><w:embedRegular/></w:font>"),
            ("embedded bold", "<w:font w:name='Default Family'><w:embedBold/></w:font>"),
            ("embedded italic", "<w:font w:name='Default Family'><w:embedItalic/></w:font>"),
            ("embedded bold italic", "<w:font w:name='Default Family'><w:embedBoldItalic/></w:font>"),
            ("unknown property", "<w:font w:name='Default Family'><w:unknown/></w:font>"),
            ("duplicate name", "<w:font w:name='Arial'/><w:font w:name='ARIAL'/>"),
            ("duplicate property", "<w:font w:name='Arial'><w:pitch/><w:pitch/></w:font>"),
            ("foreign property", "<w:font w:name='Arial'><x:pitch xmlns:x='urn:unknown'/></w:font>"),
            ("nested property", "<w:font w:name='Arial'><w:pitch><w:altName/></w:pitch></w:font>"),
            ("unknown attribute", "<w:font w:name='Arial' w:alias='Deleted Family'/>"),
            ("property attribute", "<w:font w:name='Arial'><w:pitch w:alias='Deleted Family'/></w:font>"),
            ("missing name", "<w:font/>"),
            ("long name", $"<w:font w:name='{new string('a', 129)}'/>"),
            ("control in name", "<w:font w:name='Bad&#xA;Name'/>"),
            ("text content", "<w:font w:name='Arial'>Uninterpreted text</w:font>"),
            ("too many names", string.Concat(Enumerable.Range(0, 257).Select(i => $"<w:font w:name='Family {i}'/>")))
        })
        {
            var result = await Inspect(FontTable(contents));
            check(result.Available && result.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
                "Word font review: font-table " + label + " retains reports without losing body evidence");
        }
        var tableDependency = FontTable(plainTable);
        var tableRelationships = $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='font' Type='{rel}font' Target='font.odttf'/></Relationships>";
        var embeddedDependency = await Inspect([.. tableDependency, ("fonts/_rels/catalog.xml.rels", tableRelationships)]);
        check(embeddedDependency.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
            "Word font review: font-table relationships retain reports without reading font programs");
        foreach (var (index, before, after) in new[]
        {
            (0, type + "fontTable+xml", "application/xml"),
            (3, "../fonts/catalog.xml", "../fonts/missing.xml"),
            (3, "Target='../fonts/catalog.xml'", "Target='../fonts/catalog.xml' TargetMode='External'"),
            (6, "<w:fonts ", "<!DOCTYPE fonts [<!ENTITY bad SYSTEM 'file:///forbidden'>]><w:fonts ")
        })
        {
            var parts = FontTable(plainTable);
            parts[index] = (parts[index].Name, parts[index].Text.Replace(before, after, StringComparison.Ordinal));
            var result = await Inspect(parts);
            check(!result.Available && result.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
                "Word font review: invalid selected font-table package evidence retains reports");
        }
        var unknownSettings = Parts(Paragraph(deleted + Run()));
        unknownSettings[0] = (unknownSettings[0].Name, unknownSettings[0].Text.Replace("</Types>",
            $"<Override PartName='/settings.xml' ContentType='{type}settings+xml'/></Types>"));
        unknownSettings[3] = (unknownSettings[3].Name, unknownSettings[3].Text.Replace("</Relationships>",
            $"<Relationship Id='settings' Type='{rel}settings' Target='../settings.xml'/></Relationships>"));
        foreach (var settings in new[] { "<w:themeFontLang/>", "<w:compat/>" })
        {
            var result = await Inspect([.. unknownSettings, ("settings.xml", $"<w:settings xmlns:w='{word}'>{settings}</w:settings>")]);
            check(result.Available && result.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
                "Word font review: uninspected settings retain reports");
        }
        var malformedSettings = await Inspect([.. unknownSettings, ("settings.xml", "<not-settings/>")]);
        check(malformedSettings.KeepActiveReports(["Deleted Family"]).SequenceEqual(["Deleted Family"]),
            "Word font review: malformed settings never silence a report");
    }
}
