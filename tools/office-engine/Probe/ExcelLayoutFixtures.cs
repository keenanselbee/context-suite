using System.IO.Compression;
using System.Text;

// Passive, independently authored Open XML. The chart values and caches agree;
// calculation-mode correctness has separate formula/date controls.
internal static class ExcelLayoutFixtures
{
    internal static string[] Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var names = new List<string>();
        foreach (var orientation in new[] { "portrait", "landscape" })
        foreach (var mode in new[] { "cached", "recalculate" })
        {
            var name = $"Excel layout {orientation} {mode}.xlsx";
            using var archive = ZipFile.Open(Path.Combine(directory, name), ZipArchiveMode.Create);
            Add("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                <Override PartName="/xl/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """);
            Add("_rels/.rels", Relationships("rId1", "officeDocument", "xl/workbook.xml"));
            Add("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Layout" sheetId="1" r:id="rId1"/></sheets>
                <definedNames><definedName name="_xlnm.Print_Area" localSheetId="0">'Layout'!$A$1:$G$28</definedName></definedNames>
                <calcPr fullCalcOnLoad="0"/></workbook>
                """);
            Add("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add("xl/styles.xml", """
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="1"><font><sz val="11"/><name val="Arial"/></font></fonts>
                <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf></cellXfs>
                <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);
            Add("xl/worksheets/sheet1.xml", $$"""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheetPr><pageSetUpPr fitToPage="1"/></sheetPr><dimension ref="A1:G28"/>
                <sheetViews><sheetView workbookViewId="0" showGridLines="0"/></sheetViews>
                <sheetFormatPr defaultRowHeight="15"/><cols><col min="1" max="7" width="11" customWidth="1"/></cols>
                <sheetData>
                <row r="1" ht="30" customHeight="1"><c r="A1" s="1" t="inlineStr"><is><t>MERGED HEADING ACROSS SEVEN COLUMNS</t></is></c></row>
                <row r="2" ht="40" customHeight="1"><c r="A2" s="2" t="inlineStr"><is><t xml:space="preserve">WRAPPED FIRST LINE&#10;WRAPPED SECOND LINE</t></is></c></row>
                <row r="4"><c r="A4" t="inlineStr"><is><t>ALPHA</t></is></c><c r="B4"><v>10</v></c></row>
                <row r="5"><c r="A5" t="inlineStr"><is><t>BETA</t></is></c><c r="B5"><v>20</v></c></row>
                <row r="6"><c r="A6" t="inlineStr"><is><t>GAMMA</t></is></c><c r="B6"><v>30</v></c></row>
                <row r="28"><c r="A28" t="inlineStr"><is><t>END_MARKER</t></is></c></row>
                </sheetData><mergeCells count="2"><mergeCell ref="A1:G1"/><mergeCell ref="A2:G2"/></mergeCells>
                <printOptions gridLines="0"/><pageMargins left="0.5" right="0.5" top="0.5" bottom="0.5" header="0" footer="0"/>
                <pageSetup paperSize="1" orientation="{{orientation}}" fitToWidth="1" fitToHeight="1"/>
                <drawing r:id="rId1"/></worksheet>
                """);
            Add("xl/worksheets/_rels/sheet1.xml.rels", Relationships("rId1", "drawing", "../drawings/drawing1.xml"));
            Add("xl/drawings/_rels/drawing1.xml.rels", Relationships("rId1", "chart", "../charts/chart1.xml"));
            Add("xl/drawings/drawing1.xml", """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                <xdr:absoluteAnchor><xdr:pos x="0" y="2011680"/><xdr:ext cx="5486400" cy="2514600"/>
                <xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="1" name="Authored values"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>
                <xdr:xfrm><a:off x="0" y="0"/><a:ext cx="5486400" cy="2514600"/></xdr:xfrm>
                <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart"><c:chart xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="rId1"/></a:graphicData></a:graphic>
                </xdr:graphicFrame><xdr:clientData/></xdr:absoluteAnchor></xdr:wsDr>
                """);
            Add("xl/charts/chart1.xml", """
                <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                <c:lang val="en-US"/><c:chart><c:autoTitleDeleted val="1"/><c:plotArea><c:layout/>
                <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:v>VALUES</c:v></c:tx>
                <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>
                <c:cat><c:strRef><c:f>Layout!$A$4:$A$6</c:f><c:strCache><c:ptCount val="3"/><c:pt idx="0"><c:v>ALPHA</c:v></c:pt><c:pt idx="1"><c:v>BETA</c:v></c:pt><c:pt idx="2"><c:v>GAMMA</c:v></c:pt></c:strCache></c:strRef></c:cat>
                <c:val><c:numRef><c:f>Layout!$B$4:$B$6</c:f><c:numCache><c:formatCode>0</c:formatCode><c:ptCount val="3"/><c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt><c:pt idx="2"><c:v>30</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser>
                <c:gapWidth val="150"/><c:axId val="10"/><c:axId val="20"/></c:barChart>
                <c:catAx><c:axId val="10"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:tickLblPos val="nextTo"/><c:crossAx val="20"/><c:crosses val="autoZero"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/></c:catAx>
                <c:valAx><c:axId val="20"/><c:scaling><c:orientation val="minMax"/><c:max val="40"/><c:min val="0"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:numFmt formatCode="0" sourceLinked="0"/><c:tickLblPos val="nextTo"/><c:crossAx val="10"/><c:crosses val="autoZero"/><c:crossBetween val="between"/><c:majorUnit val="10"/></c:valAx>
                </c:plotArea><c:plotVisOnly val="1"/></c:chart>
                <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"><a:latin typeface="Arial"/></a:defRPr></a:pPr><a:endParaRPr lang="en-US"/></a:p></c:txPr>
                </c:chartSpace>
                """);
            names.Add(name);
            void Add(string part, string xml)
            {
                using var writer = new StreamWriter(archive.CreateEntry(part).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }
        return names.ToArray();
    }

    private static string Relationships(string id, string type, string target) =>
        $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{type}\" Target=\"{target}\"/></Relationships>";
}
