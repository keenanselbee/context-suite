using ContextSuite.Core.Analysis;

internal static partial class DocumentAnalysisContracts
{
    private static async Task ExcelChartContractsAsync(Action<bool, string> check)
    {
        const string types = "http://schemas.openxmlformats.org/package/2006/content-types";
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var ordinary = OpenXmlParts("xlsx");
        foreach (var kind in new[] { "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", "application/vnd.ms-office.chartex+xml" })
        foreach (var declaration in new[] { $"<Override PartName=\"/elsewhere/figure.xml\" ContentType=\"{kind}\"/>",
            $"<Default Extension=\"figure\" ContentType=\"{kind}\"/>" })
        {
            var parts = ordinary.ToArray();
            parts[0] = (parts[0].Name, parts[0].Text.Replace("</Types>", declaration + "</Types>"));
            await Inspect(Zip(parts), true, true, "chart content declaration, including unreferenced parts",
                unsupported: kind == "application/vnd.ms-office.chartex+xml");
        }
        foreach (var type in new[] { "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
            "http://purl.oclc.org/ooxml/officeDocument/relationships/chart", "http://schemas.microsoft.com/office/2014/relationships/chartEx" })
        foreach (var mode in new[] { "", " TargetMode=\"External\"" })
            await Inspect(Zip([.. ordinary, ("odd/_rels/drawing.xml.rels",
                $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"c\" Type=\"{type}\" Target=\"figure.xml\"{mode}/></Relationships>")]),
                true, true, "chart relationship without matching content declaration: " + type + mode,
                unsupported: type.EndsWith("chartEx", StringComparison.Ordinal) || mode.Length != 0);
        await Inspect(Zip([.. ordinary, ("xl/charts/just-a-name.xml", "<note>chart</note>")]),
            true, false, "chart-looking filename alone does not identify a chart");
        var declared = ordinary.ToArray();
        declared[0] = (declared[0].Name, declared[0].Text.Replace("</Types>",
            "<Override PartName=\"/chart.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/></Types>"));
        await Inspect(Zip([.. declared, ("later.rels", "<Relationships")]), false, false,
            "ordinary chart declaration does not hide later malformed relationships");
        await Inspect(Zip([.. declared, ("later.rels", $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"c\" Type=\"http://schemas.microsoft.com/office/2014/relationships/chartEx\" Target=\"chart.xml\"/></Relationships>")]),
            true, true, "ordinary chart declaration does not hide a later unsupported chart", unsupported: true);
        foreach (var mode in new[] { "Internal", "External", "external", "Unknown", "" })
            await Inspect(Zip([.. declared, ("later.rels", $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"c\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" Target=\"chart.xml\" TargetMode=\"{mode}\"/></Relationships>")]),
                true, true, "chart target mode is explicit and case-sensitive: " + mode, unsupported: mode != "Internal");
        foreach (var malformed in new[] { "<Relationships", $"<other xmlns=\"{relationships}\"/>",
            $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"a\" Type=\"image\"/></Relationships>",
            $"<Relationships xmlns=\"{relationships}\"><Relationship Id=\"a\" Type=\"image\" Target=\"a\"/><Relationship Id=\"a\" Type=\"image\" Target=\"b\"/></Relationships>",
            $"<!DOCTYPE Relationships [<!ENTITY x 'chart'>]><Relationships xmlns=\"{relationships}\">&x;</Relationships>",
            $"<Relationships xmlns=\"{relationships}\">" + string.Concat(Enumerable.Repeat("<n>", 10)) + string.Concat(Enumerable.Repeat("</n>", 10)) + "</Relationships>",
            $"<Relationships xmlns=\"{relationships}\">" + new string(' ', 256 * 1024) + "</Relationships>" })
            await Inspect(Zip([.. ordinary, ("xl/_rels/drawing.xml.rels", malformed)]), false, false, "incomplete relationship inspection refuses conversion");
        await Inspect(Zip([.. ordinary, .. Enumerable.Range(0, 128).Select(index => ($"p{index}.rels", $"<Relationships xmlns=\"{relationships}\"/>"))]),
            false, false, "relationship count limit cannot become a chart-free result");
        await Inspect(Zip([.. ordinary, .. Enumerable.Range(0, 6).Select(index => ($"p{index}.rels",
            $"<Relationships xmlns=\"{relationships}\">" + new string(' ', 200000) + "</Relationships>"))]),
            false, false, "aggregate expansion limit cannot become a chart-free result");
        foreach (var malformed in new[] { $"<Types xmlns=\"{types}\"><Unknown/></Types>",
            ordinary[0].Text.Replace("</Types>", "<Default Extension=\"xml\" ContentType=\"text/xml\"/><Default Extension=\"XML\" ContentType=\"text/xml\"/></Types>"),
            ordinary[0].Text.Replace("/content/main.xml", "/../main.xml") })
        {
            var parts = ordinary.ToArray(); parts[0] = (parts[0].Name, malformed);
            using var input = new MemoryStream(Zip(parts));
            check(await ExcelChartInspection.ReadAsync(input, default) is { Complete: false, HasCharts: false },
                "Excel charts: malformed declarations cannot become a chart-free result");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var canceledInput = new MemoryStream(OpenXml("xlsx")); canceledInput.Position = 3;
        try { await ExcelChartInspection.ReadAsync(canceledInput, canceled.Token); check(false, "Chart inspection swallowed cancellation"); }
        catch (OperationCanceledException) { check(canceledInput.Position == 3, "Excel charts: cancellation propagates without moving the source"); }

        async Task Inspect(byte[] bytes, bool complete, bool charts, string label, bool unsupported = false)
        {
            using var input = new MemoryStream(bytes, writable: false); input.Position = 3;
            var result = await ExcelChartInspection.ReadAsync(input, default);
            check(result.Complete == complete && result.HasCharts == charts && result.HasUnsupportedCharts == unsupported &&
                input.Position == 3 && input.ToArray().SequenceEqual(bytes), "Excel charts: " + label);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("input.xlsx", input, default);
            check(preflight.Analysis.Identity.FormatId == "xlsx" && (complete && !unsupported ? preflight.Refusal is null :
                preflight.Refusal is not null && preflight.Refusal.Contains("charts") && preflight.Refusal.Contains("Analyze remains available")),
                "Excel charts: preflight retains analysis and applies refusal: " + label);
        }
    }
}
