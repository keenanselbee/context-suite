using System.IO.Compression;
using System.Text;

internal static class OfficeFixtures
{
    public static void Create(string directory)
    {
        const string rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string content = "http://schemas.openxmlformats.org/package/2006/content-types";
        string Root(string target) => $"<Relationships xmlns=\"{rels}\"><Relationship Id=\"rId1\" Type=\"{office}/officeDocument\" Target=\"{target}\"/></Relationships>";
        string Types(string extra) => $"<Types xmlns=\"{content}\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>{extra}</Types>";
        string Type(string part, string type) => $"<Override PartName=\"/{part}\" ContentType=\"application/vnd.openxmlformats-officedocument.{type}+xml\"/>";
        Package("Word ü.docx", new()
        {
            ["_rels/.rels"] = Root("word/document.xml"),
            ["[Content_Types].xml"] = Types(Type("word/document.xml", "wordprocessingml.document.main") +
                Type("word/header1.xml", "wordprocessingml.header") + Type("word/header2.xml", "wordprocessingml.header") +
                Type("word/footer1.xml", "wordprocessingml.footer")),
            ["word/_rels/document.xml.rels"] = $"""
                <Relationships xmlns="{rels}"><Relationship Id="headerFirst" Type="{office}/header" Target="header1.xml"/><Relationship Id="headerDefault" Type="{office}/header" Target="header2.xml"/><Relationship Id="footer" Type="{office}/footer" Target="footer1.xml"/></Relationships>
                """,
            ["word/header1.xml"] = """
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>FIRST PAGE HEADER</w:t></w:r></w:p></w:hdr>
                """,
            ["word/header2.xml"] = """
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>RUNNING HEADER</w:t></w:r></w:p></w:hdr>
                """,
            ["word/footer1.xml"] = """
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t xml:space="preserve">Page </w:t></w:r><w:fldSimple w:instr="PAGE" w:dirty="true"><w:r><w:t>99</w:t></w:r></w:fldSimple><w:r><w:t xml:space="preserve"> of </w:t></w:r><w:fldSimple w:instr="NUMPAGES" w:dirty="true"><w:r><w:t>99</w:t></w:r></w:fldSimple></w:p></w:ftr>
                """,
            ["word/document.xml"] = """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>
                <w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:b/><w:sz w:val="32"/></w:rPr><w:t>Context Suite Word — page one</w:t></w:r></w:p>
                <w:p><w:r><w:t>Generated Unicode: café ü. This document has two authored pages.</w:t></w:r></w:p>
                <w:tbl><w:tblPr><w:tblW w:w="6000" w:type="dxa"/><w:tblBorders><w:top w:val="single" w:sz="8"/><w:bottom w:val="single" w:sz="8"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Item</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Value 42</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:p><w:r><w:br w:type="page"/><w:t>Context Suite Word — page two</w:t></w:r></w:p>
                <w:sectPr><w:headerReference w:type="first" r:id="headerFirst"/><w:headerReference w:type="default" r:id="headerDefault"/><w:footerReference w:type="first" r:id="footer"/><w:footerReference w:type="default" r:id="footer"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/><w:titlePg/></w:sectPr>
                </w:body></w:document>
                """
        });
        Package("Excel ü.xlsx", new()
        {
            ["_rels/.rels"] = Root("xl/workbook.xml"),
            ["[Content_Types].xml"] = Types(Type("xl/workbook.xml", "spreadsheetml.sheet.main") + Type("xl/worksheets/sheet1.xml", "spreadsheetml.worksheet") + Type("xl/worksheets/sheet2.xml", "spreadsheetml.worksheet")),
            ["xl/workbook.xml"] = $"""
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="{office}"><sheets><sheet name="Print area" sheetId="1" r:id="rId1"/><sheet name="Hidden" sheetId="2" state="hidden" r:id="rId2"/></sheets><definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'Print area'!$A$1:$B$4</definedName></definedNames><calcPr calcId="191029" fullCalcOnLoad="0"/></workbook>
                """,
            ["xl/_rels/workbook.xml.rels"] = $"<Relationships xmlns=\"{rels}\"><Relationship Id=\"rId1\" Type=\"{office}/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"{office}/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>",
            ["xl/worksheets/sheet1.xml"] = """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetPr><pageSetUpPr fitToPage="1"/></sheetPr><cols><col min="1" max="2" width="32" customWidth="1"/></cols><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Context Suite Excel café</t></is></c></row><row r="2"><c r="A2"><v>2</v></c><c r="B2"><v>3</v></c></row><row r="3"><c r="A3" t="inlineStr"><is><t>Formula result</t></is></c><c r="B3"><f>SUM(A2:B2)</f><v>5</v></c></row><row r="20"><c r="A20" t="inlineStr"><is><t>OUTSIDE PRINT AREA</t></is></c></row></sheetData><printOptions gridLines="1"/><pageMargins left="0.5" right="0.5" top="0.5" bottom="0.5" header="0.2" footer="0.2"/><pageSetup paperSize="1" orientation="portrait" fitToWidth="1" fitToHeight="1"/></worksheet>
                """,
            ["xl/worksheets/sheet2.xml"] = """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>HIDDEN SHEET MUST NOT PRINT</t></is></c></row></sheetData></worksheet>
                """
        });
        var slides = new Dictionary<string, string>
        {
            ["_rels/.rels"] = Root("ppt/presentation.xml"),
            ["[Content_Types].xml"] = Types(Type("ppt/presentation.xml", "presentationml.presentation.main") +
                Type("ppt/slideMasters/slideMaster1.xml", "presentationml.slideMaster") + Type("ppt/slideLayouts/slideLayout1.xml", "presentationml.slideLayout") +
                Type("ppt/theme/theme1.xml", "theme") + string.Concat(Enumerable.Range(1, 3).Select(index => Type($"ppt/slides/slide{index}.xml", "presentationml.slide")))),
            ["ppt/presentation.xml"] = $"""
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="{office}"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId4"/></p:sldMasterIdLst><p:sldIdLst><p:sldId id="256" r:id="rId1"/><p:sldId id="257" r:id="rId2"/><p:sldId id="258" r:id="rId3"/></p:sldIdLst><p:sldSz cx="9144000" cy="5143500"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>
                """,
            ["ppt/_rels/presentation.xml.rels"] = $"<Relationships xmlns=\"{rels}\">" + string.Concat(Enumerable.Range(1, 3).Select(index => $"<Relationship Id=\"rId{index}\" Type=\"{office}/slide\" Target=\"slides/slide{index}.xml\"/>")) + $"<Relationship Id=\"rId4\" Type=\"{office}/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/></Relationships>",
            ["ppt/slideMasters/slideMaster1.xml"] = $"""
                <p:sldMaster xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="{office}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld><p:clrMap accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" bg1="lt1" bg2="lt2" folHlink="folHlink" hlink="hlink" tx1="dk1" tx2="dk2"/><p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>
                """,
            ["ppt/slideMasters/_rels/slideMaster1.xml.rels"] = $"<Relationships xmlns=\"{rels}\"><Relationship Id=\"rId1\" Type=\"{office}/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{office}/theme\" Target=\"../theme/theme1.xml\"/></Relationships>",
            ["ppt/slideLayouts/slideLayout1.xml"] = """
                <p:sldLayout xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" type="blank"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>
                """,
            ["ppt/slideLayouts/_rels/slideLayout1.xml.rels"] = $"<Relationships xmlns=\"{rels}\"><Relationship Id=\"rId1\" Type=\"{office}/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>",
            ["ppt/theme/theme1.xml"] = """
                <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Context Suite fixture"><a:themeElements><a:clrScheme name="Fixture"><a:dk1><a:srgbClr val="000000"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="222222"/></a:dk2><a:lt2><a:srgbClr val="EEEEEE"/></a:lt2><a:accent1><a:srgbClr val="336699"/></a:accent1><a:accent2><a:srgbClr val="993333"/></a:accent2><a:accent3><a:srgbClr val="339933"/></a:accent3><a:accent4><a:srgbClr val="663399"/></a:accent4><a:accent5><a:srgbClr val="339999"/></a:accent5><a:accent6><a:srgbClr val="996633"/></a:accent6><a:hlink><a:srgbClr val="0000FF"/></a:hlink><a:folHlink><a:srgbClr val="800080"/></a:folHlink></a:clrScheme><a:fontScheme name="Arial"><a:majorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme><a:fmtScheme name="Fixture"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>
                """
        };
        for (var index = 1; index <= 3; index++)
        {
            slides[$"ppt/slides/_rels/slide{index}.xml.rels"] = $"<Relationships xmlns=\"{rels}\"><Relationship Id=\"rId1\" Type=\"{office}/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/></Relationships>";
            slides[$"ppt/slides/slide{index}.xml"] = $"""
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" show="{(index == 2 ? 0 : 1)}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr><p:sp><p:nvSpPr><p:cNvPr id="2" name="Title"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="457200" y="457200"/><a:ext cx="8229600" cy="1371600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="DDEEFF"/></a:solidFill></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang="en-US" sz="2800"><a:latin typeface="Arial"/></a:rPr><a:t>{(index == 2 ? "HIDDEN SLIDE MUST NOT PRINT" : $"Context Suite PowerPoint slide {index} café")}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>
                """;
        }
        Package("PowerPoint ü.pptx", slides);

        void Package(string name, Dictionary<string, string> entries)
        {
            using var zip = ZipFile.Open(Path.Combine(directory, name), ZipArchiveMode.Create);
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry.Key, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + entry.Value);
            }
        }
    }
}
