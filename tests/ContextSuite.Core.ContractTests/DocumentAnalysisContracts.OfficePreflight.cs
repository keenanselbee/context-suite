using ContextSuite.Core.Analysis;

internal static partial class DocumentAnalysisContracts
{
    private static async Task OfficeSourcePreflightContractsAsync(Action<bool, string> check)
    {
        foreach (var format in new[] { "docx", "xlsx", "pptx" })
        foreach (var strict in new[] { false, true })
        {
            var bytes = OpenXml(format, strict);
            using var input = new MemoryStream(bytes, writable: false);
            input.Position = input.Length;
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync("misleading.doc", input, default);
            check(result.FormatId == format && result.Refusal is null && input.CanRead && input.ToArray().SequenceEqual(bytes),
                $"Office preflight: identifies {format}/strict={strict} from content without writing or closing the source");
        }
        foreach (var (name, bytes) in new (string, byte[])[]
        {
            ("empty", []), ("ZIP prefix", OpenXml("docx")[..64]),
            ("text", "Not a document"u8.ToArray()), ("ordinary ZIP", Zip(("readme.txt", "text"))),
            ("OpenDocument", OpenDocument("odt")), ("encrypted OpenDocument", OpenDocument("ods", encrypted: true)),
            ("compound header", [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1])
        })
        {
            using var input = new MemoryStream(bytes, writable: false);
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync("looks-valid.docx", input, default);
            check(result.FormatId is null && result.Refusal is not null && result.Analysis.FileBytes == bytes.Length,
                "Office preflight: refuses " + name + " with basic analysis retained");
        }
        foreach (var (format, contentType) in new[]
        {
            ("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"),
            ("docx", "application/vnd.ms-word.document.macroEnabled.main+xml"),
            ("docx", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml"),
            ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml"),
            ("xlsx", "application/vnd.ms-excel.sheet.macroEnabled.main+xml"),
            ("xlsx", "application/vnd.ms-excel.template.macroEnabled.main+xml"),
            ("pptx", "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml"),
            ("pptx", "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml"),
            ("pptx", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"),
            ("pptx", "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml"),
            ("pptx", "application/vnd.ms-powerpoint.template.macroEnabled.main+xml")
        })
        {
            var parts = OpenXmlParts(format);
            var types = System.Xml.Linq.XElement.Parse(parts[0].Text);
            types.Elements().Single().SetAttributeValue("ContentType", contentType);
            parts[0] = (parts[0].Name, types.ToString());
            using var input = new MemoryStream(Zip(parts), writable: false);
            var result = await OfficeSourcePreflight.InspectOpenXmlAsync("misleading." + format, input, default);
            check(result.FormatId is null && result.Refusal is not null && result.Analysis.Identity.FormatId == format,
                "Office preflight: distinguishes exact variant from Analyze family: " + contentType);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var canceledInput = new MemoryStream(OpenXml("docx"), writable: false);
        try
        {
            await OfficeSourcePreflight.InspectOpenXmlAsync("fixture.docx", canceledInput, canceled.Token);
            check(false, "Office preflight swallowed cancellation");
        }
        catch (OperationCanceledException)
        {
            check(canceledInput.Position == 0, "Office preflight: cancellation propagates before source reads");
        }
    }
}
