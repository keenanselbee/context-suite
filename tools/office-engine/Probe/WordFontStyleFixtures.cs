using System.IO.Compression;
using System.Text;

// Authored, passive final-text controls. No external links or executable content.
internal static class WordFontStyleFixtures
{
    internal const string Text = "Context Suite font style check. Wide W, narrow i, digits 0123456789.";
    internal const string Missing = "ContextSuiteAbsentFont9361";
    internal static readonly (string Key, string Family, string Control)[] Cases =
    [
        ("arial-control", "Arial", "arial-control"),
        ("courier-control", "Courier New", "courier-control"),
        ("times-control", "Times New Roman", "times-control"),
        ("missing-control", Missing, "missing-control"),
        ("defaults", "Arial", "arial-control"),
        ("paragraph-chain", "Courier New", "courier-control"),
        ("character-chain", "Arial", "arial-control"),
        ("direct-literal", "Arial", "arial-control"),
        ("direct-theme", "Times New Roman", "times-control"),
        ("same-element-theme", "Courier New", "courier-control"),
        ("paragraph-theme", "Courier New", "courier-control"),
        ("unused-missing", "Arial", "arial-control"),
        ("deleted-missing", "Arial", "arial-control"),
        ("old-formatting", "Arial", "arial-control"),
        ("missing-chain", Missing, "missing-control")
    ];

    internal static string Name(string key) => "Word font styles " + key + ".docx";

    internal static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string relations = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        foreach (var item in Cases)
        {
            var defaultFonts = Fonts("Times New Roman");
            var paragraph = "";
            var run = Fonts(item.Family);
            var styles = "";
            var preceding = "";
            switch (item.Key)
            {
                case "defaults":
                    defaultFonts = Fonts("Arial"); run = ""; break;
                case "paragraph-chain":
                    styles = Style("Base", "paragraph", Fonts("Courier New")) + Style("Body", "paragraph", "", "Base", true);
                    run = ""; break;
                case "character-chain":
                    styles = Style("Body", "paragraph", Fonts("Courier New"), isDefault: true) +
                        Style("CharBase", "character", Fonts("Arial")) + Style("Char", "character", "", "CharBase");
                    run = "<w:rStyle w:val='Char'/>"; break;
                case "direct-literal":
                    styles = Style("Body", "paragraph", ThemeFonts("majorHAnsi"), isDefault: true);
                    run = Fonts("Arial"); break;
                case "direct-theme":
                    styles = Style("Body", "paragraph", Fonts("Arial"), isDefault: true);
                    run = ThemeFonts("minorHAnsi"); break;
                case "same-element-theme":
                    run = "<w:rFonts w:ascii='Arial' w:hAnsi='Arial' w:asciiTheme='majorHAnsi' w:hAnsiTheme='majorHAnsi'/>"; break;
                case "paragraph-theme":
                    styles = Style("Selected", "paragraph", ThemeFonts("majorHAnsi"));
                    paragraph = "<w:pStyle w:val='Selected'/>"; run = ""; break;
                case "unused-missing":
                    styles = Style("Unused", "paragraph", Fonts(Missing)) + Style("UnusedChar", "character", Fonts(Missing)); break;
                case "deleted-missing":
                    preceding = $"<w:del w:id='1' w:author='Fixture Author'><w:r><w:rPr>{Fonts(Missing)}</w:rPr><w:delText>DELETED TEXT</w:delText></w:r></w:del>"; break;
                case "old-formatting":
                    run += $"<w:rPrChange w:id='2' w:author='Fixture Author'><w:rPr>{Fonts(Missing)}</w:rPr></w:rPrChange>"; break;
                case "missing-chain":
                    styles = Style("Base", "paragraph", Fonts(Missing)) + Style("Body", "paragraph", "", "Base", true);
                    run = ""; break;
            }
            var parts = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = """
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/><Override PartName="/word/theme.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/></Types>
                    """,
                ["_rels/.rels"] = $"<Relationships xmlns='{relations}'><Relationship Id='main' Type='{office}officeDocument' Target='word/document.xml'/></Relationships>",
                ["word/_rels/document.xml.rels"] = $"<Relationships xmlns='{relations}'><Relationship Id='styles' Type='{office}styles' Target='styles.xml'/><Relationship Id='settings' Type='{office}settings' Target='settings.xml'/><Relationship Id='theme' Type='{office}theme' Target='theme.xml'/></Relationships>",
                ["word/settings.xml"] = $"<w:settings xmlns:w='{w}'><w:revisionView w:markup='true' w:insDel='true'/></w:settings>",
                ["word/styles.xml"] = $"<w:styles xmlns:w='{w}'><w:docDefaults><w:rPrDefault><w:rPr>{defaultFonts}<w:sz w:val='24'/></w:rPr></w:rPrDefault></w:docDefaults>{styles}</w:styles>",
                ["word/theme.xml"] = Theme(),
                ["word/document.xml"] = $"""
                    <w:document xmlns:w="{w}"><w:body>
                    <w:p><w:pPr>{paragraph}<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>{preceding}<w:r><w:rPr>{run}<w:sz w:val="24"/></w:rPr><w:t>{Text}</w:t></w:r></w:p>
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                    </w:body></w:document>
                    """
            };
            if (item.Key is "deleted-missing" or "old-formatting")
            {
                parts["[Content_Types].xml"] = parts["[Content_Types].xml"].Replace("</Types>",
                    "<Override PartName='/word/fontTable.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml'/></Types>");
                parts["word/_rels/document.xml.rels"] = parts["word/_rels/document.xml.rels"].Replace("</Relationships>",
                    $"<Relationship Id='fonts' Type='{office}fontTable' Target='fontTable.xml'/></Relationships>");
                parts["word/fontTable.xml"] = $"<w:fonts xmlns:w='{w}'><w:font w:name='Arial'><w:family w:val='swiss'/><w:pitch w:val='variable'/></w:font><w:font w:name='{Missing}'/></w:fonts>";
            }
            using var file = new FileStream(Path.Combine(directory, Name(item.Key)), FileMode.CreateNew);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            foreach (var part in parts)
            {
                using var output = new StreamWriter(archive.CreateEntry(part.Key).Open(), new UTF8Encoding(false));
                output.Write(part.Value);
            }
        }
        return Cases.Select(item => (Name(item.Key), "writer_pdf_Export", 1)).ToArray();
    }

    private static string Fonts(string family) => $"<w:rFonts w:ascii='{family}' w:hAnsi='{family}'/>";
    private static string ThemeFonts(string value) => $"<w:rFonts w:asciiTheme='{value}' w:hAnsiTheme='{value}'/>";
    private static string Style(string id, string kind, string properties, string? parent = null, bool isDefault = false) =>
        $"<w:style w:type='{kind}' w:styleId='{id}'{(isDefault ? " w:default='1'" : "")}><w:name w:val='{id}'/>{(parent is null ? "" : $"<w:basedOn w:val='{parent}'/>")}<w:rPr>{properties}</w:rPr></w:style>";

    private static string Theme()
    {
        var colors = string.Concat(new[] { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" }
            .Select(name => $"<a:{name}><a:srgbClr val='000000'/></a:{name}>"));
        var fills = string.Concat(Enumerable.Repeat("<a:solidFill><a:schemeClr val='phClr'/></a:solidFill>", 3));
        var lines = string.Concat(Enumerable.Repeat("<a:ln w='9525'><a:solidFill><a:schemeClr val='phClr'/></a:solidFill><a:prstDash val='solid'/></a:ln>", 3));
        var effects = string.Concat(Enumerable.Repeat("<a:effectStyle><a:effectLst/></a:effectStyle>", 3));
        return $"""
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Authored">
            <a:themeElements><a:clrScheme name="Authored">{colors}</a:clrScheme>
            <a:fontScheme name="Authored"><a:majorFont><a:latin typeface="Courier New"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Times New Roman"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme>
            <a:fmtScheme name="Authored"><a:fillStyleLst>{fills}</a:fillStyleLst><a:lnStyleLst>{lines}</a:lnStyleLst><a:effectStyleLst>{effects}</a:effectStyleLst><a:bgFillStyleLst>{fills}</a:bgFillStyleLst></a:fmtScheme></a:themeElements>
            </a:theme>
            """;
    }
}
