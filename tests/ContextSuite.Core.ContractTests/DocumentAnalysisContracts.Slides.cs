using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Application;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

internal static partial class DocumentAnalysisContracts
{
    private static async Task SlideVisibilityContractsAsync(string scratch, Action<bool, string> check)
    {
        foreach (var strict in new[] { false, true })
        foreach (var numeric in new[] { false, true })
        {
            var result = await AnalyzeAsync(Package(Parts([numeric ? "1" : "true", numeric ? "0" : "false", null], strict)));
            check(result.Identity.FormatId == "pptx" && Counts(result).Select(fact => fact.Integer).SequenceEqual(new long?[] { 2, 1, 1 }),
                $"slides: strict={strict}, numeric={numeric} distinguishes visible, hidden and default declarations");
            check(Counts(result)[0].Availability == FactAvailability.Derived && Counts(result).Skip(1).All(fact => fact.Availability == FactAvailability.Explicit) &&
                result.Facts.Single(fact => fact.Id == "document.slide-visibility-scope").Text!.Contains("not PDF page counts"),
                "slides: default visibility is derived and scope does not promise rendered pages");
        }
        var explicitResult = await AnalyzeAsync(Package(Parts(["true", "false"])));
        check(Counts(explicitResult).All(fact => fact.Availability == FactAvailability.Explicit) && Counts(explicitResult)[2].Integer == 0,
            "slides: explicit-only visibility does not invent omitted settings");
        var empty = await AnalyzeAsync(Package(Parts([])));
        check(Counts(empty).All(fact => fact.Integer == 0), "slides: empty declared list needs no slide part reads");
        check(Counts(await AnalyzeAsync(Package(Parts(["false", "0"])))).Select(fact => fact.Integer).SequenceEqual(new long?[] { 0, 2, 0 }),
            "slides: all-hidden deck retains zero visible slides without guessing export behavior");
        foreach (var strict in new[] { false, true })
        foreach (var values in new string?[][] { [], ["false", "0"], ["true", "false"], [null, "0"] })
        {
            var source = Package(Parts(values, strict));
            using var input = new MemoryStream(source, writable: false);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("slides.pptx", input, default);
            var emptyDeck = values.All(value => value is "false" or "0");
            check(preflight.FormatId == "pptx" && (emptyDeck
                ? preflight.Refusal?.Contains("no visible slides to export") == true
                : preflight.Refusal is null), "slides: conversion preflight distinguishes known empty and visible decks, strict=" + strict);
            check(input.ToArray().SequenceEqual(source) && input.CanRead, "slides: conversion visibility preflight preserves source");
        }
        await NoVisibleSlidesCommandAsync(scratch, Package(Parts([])), "empty", check);
        await NoVisibleSlidesCommandAsync(scratch, Package(Parts(["false", "0"])), "all-hidden", check);
        var unused = Parts([null]);
        unused["content/slide1.xml"] = unused["content/slide1.xml"].Replace("<sld ", "<sld xmlns:show=\"urn:unused\" ");
        unused["content/unreferenced.xml"] = "<sld show=\"false\"/>";
        check(Counts(await AnalyzeAsync(Package(unused))).Select(fact => fact.Integer).SequenceEqual(new long?[] { 1, 0, 1 }),
            "slides: unused namespace prefix and unreferenced slide do not alter default visibility");
        foreach (var target in new[] { "/content/slide1.xml", "./slide1.xml", "../content/slide1.xml" })
        {
            var parts = Parts(["false"]);
            parts["content/_rels/main.xml.rels"] = parts["content/_rels/main.xml.rels"].Replace("Target=\"slide1.xml\"", $"Target=\"{target}\"");
            check(Counts(await AnalyzeAsync(Package(parts)))[1].Integer == 1, "slides: resolves internal ZIP target without filesystem access: " + target);
        }
        var cases = new List<(string Label, Action<Dictionary<string, string>> Change)>
        {
            ("missing relationship part", p => p.Remove("content/_rels/main.xml.rels")),
            ("missing slide part", p => p.Remove("content/slide1.xml")),
            ("missing relationship id", p => p["content/main.xml"] = p["content/main.xml"].Replace("r:id=\"s1\"", "")),
            ("duplicate numeric ids", p => p["content/main.xml"] = p["content/main.xml"].Replace("id=\"257\"", "id=\"256\"")),
            ("invalid numeric id", p => p["content/main.xml"] = p["content/main.xml"].Replace("id=\"256\"", "id=\"1\"")),
            ("duplicate relationship ids", p => p["content/_rels/main.xml.rels"] = p["content/_rels/main.xml.rels"].Replace("Id=\"s2\"", "Id=\"s1\"")),
            ("repeated slide target", p => p["content/_rels/main.xml.rels"] = p["content/_rels/main.xml.rels"].Replace("slide2.xml", "slide1.xml")),
            ("wrong relationship type", p => p["content/_rels/main.xml.rels"] = p["content/_rels/main.xml.rels"].Replace("relationships/slide", "relationships/image")),
            ("external relationship", p => p["content/_rels/main.xml.rels"] = p["content/_rels/main.xml.rels"].Replace("Target=", "TargetMode=\"External\" Target=")),
            ("wrong slide content type", p => p["[Content_Types].xml"] = p["[Content_Types].xml"].Replace("presentationml.slide+xml", "presentationml.notesSlide+xml")),
            ("duplicate slide override", p => p["[Content_Types].xml"] = p["[Content_Types].xml"].Replace("</Types>", "<Override PartName=\"/content/slide1.xml\" ContentType=\"other\"/></Types>")),
            ("wrong slide root", p => p["content/slide1.xml"] = "<sld/>"),
            ("malformed slide XML", p => p["content/slide1.xml"] = "<sld"),
            ("DTD in slide", p => p["content/slide1.xml"] = "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///never-open'>]><x>&e;</x>"),
            ("oversized slide", p => p["content/slide1.xml"] = "<x>" + new string('x', 300 * 1024) + "</x>"),
            ("namespace lookalike setting", p => p["content/slide1.xml"] = p["content/slide1.xml"].Replace("show=", "xmlns:x=\"urn:other\" x:show=")),
            ("unsupported list child", p => p["content/main.xml"] = p["content/main.xml"].Replace("</sldIdLst>", "<other/></sldIdLst>")),
            ("compatibility slide content", p => p["content/slide1.xml"] = p["content/slide1.xml"].Replace("<cSld/>", "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"/>")),
            ("compatibility presentation", p => p["content/main.xml"] = p["content/main.xml"].Replace("<presentation ", "<presentation xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:ProcessContent=\"x:y\" "))
        };
        foreach (var value in new[] { "True", "yes", "2", "", "\u00a0true\u00a0" })
            cases.Add(("invalid visibility value " + value, p => p["content/slide1.xml"] = p["content/slide1.xml"].Replace("show=\"true\"", $"show=\"{value}\"")));
        foreach (var target in new[] { "../../outside.xml", "https://example.invalid/slide.xml", "C:/slide.xml", "//server/slide.xml", "slide%31.xml", "slide1.xml#part", "slide1.xml?query", "a\\slide.xml", "" })
            cases.Add(("invalid target " + target, p => p["content/_rels/main.xml.rels"] = p["content/_rels/main.xml.rels"].Replace("Target=\"slide1.xml\"", $"Target=\"{target}\"")));
        foreach (var (label, change) in cases)
        {
            var parts = Parts(["true", "false"]);
            change(parts);
            var result = await AnalyzeAsync(Package(parts));
            check(result.Identity.FormatId == "pptx" && result.Facts.Single(fact => fact.Id == "document.slides").Integer == 2 &&
                Counts(result).Length == 3 && Counts(result).All(fact => fact.Availability == FactAvailability.Unavailable && fact.Integer is null) &&
                result.Warnings.Any(warning => warning.StartsWith("Slide visibility is unavailable")),
                "slides: optional failure preserves identity/declared count and discards partial visibility: " + label);
            using var input = new MemoryStream(Package(parts), writable: false);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("uncertain.pptx", input, default);
            check(preflight.Refusal?.Contains("no visible slides") != true,
                "slides: unavailable visibility is not refused as an empty deck: " + label);
        }
        foreach (var count in new[] { 128, 129 })
        {
            var result = await AnalyzeAsync(Package(Parts(Enumerable.Repeat<string?>(null, count).ToArray())));
            check(result.Identity.FormatId == "pptx" && (count == 128 ? Counts(result)[0].Integer == 128 : Counts(result).All(fact => fact.Availability == FactAvailability.Unavailable)),
                "slides: bounded slide selection at " + count);
            using var input = new MemoryStream(Package(Parts(Enumerable.Repeat<string?>(null, count).ToArray())), writable: false);
            var preflight = await OfficeSourcePreflight.InspectOpenXmlAsync("bounded.pptx", input, default);
            check(preflight.Refusal is null, "slides: visible or budget-limited presentation is not mistaken for an empty deck at " + count);
        }
        var aggregate = Parts(Enumerable.Repeat<string?>(null, 5).ToArray());
        for (var index = 1; index <= 5; index++) aggregate[$"content/slide{index}.xml"] = aggregate[$"content/slide{index}.xml"].Replace("<cSld/>", "<cSld>" + new string('x', 220 * 1024) + "</cSld>");
        check(Counts(await AnalyzeAsync(Package(aggregate))).All(fact => fact.Availability == FactAvailability.Unavailable),
            "slides: aggregate expanded-byte budget discards partial visibility counts");
        var whitespace = Parts([" \tfalse\r\n"]);
        check(Counts(await AnalyzeAsync(Package(whitespace)))[1].Integer == 1, "slides: XML Boolean whitespace is accepted");
        var bytes = Package(Parts([null, "false"]));
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancelOnRead(bytes, Find(bytes, "content/slide1.xml"u8) - 30, cancellation);
        try { await DocumentAnalysis.AddPackageAsync(HeaderAnalyzer.Analyze("test.pptx", bytes, bytes.Length), stream, cancellation.Token); check(false, "Slide cancellation swallowed."); }
        catch (OperationCanceledException) { check(true, "slides: cancellation during an optional slide read propagates"); }
        var path = Path.Combine(scratch, "slides-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(path, bytes);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var read = await FileAnalysisReader.ReadAsync(path, CancellationToken.None);
        var after = await File.ReadAllBytesAsync(path);
        check(read.Identity.FormatId == "pptx" && Counts(read).Select(fact => fact.Integer).SequenceEqual(new long?[] { 1, 1, 1 }) &&
            timestamp == File.GetLastWriteTimeUtc(path) && SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(after)),
            "slides: real reader preserves source bytes/time and identifies content under a misleading image name");

        static AnalysisFact[] Counts(FileAnalysis result) => result.Facts.Where(fact => fact.Id is
            "document.slides-visible" or "document.slides-hidden" or "document.slides-default-visibility").ToArray();
        static byte[] Package(Dictionary<string, string> parts) => Zip(parts.Select(part => (part.Key, part.Value)).ToArray());
        static Dictionary<string, string> Parts(string?[] values, bool strict = false)
        {
            var parts = OpenXmlParts("pptx", strict).ToDictionary(part => part.Name, part => part.Text);
            var ns = strict ? "http://purl.oclc.org/ooxml/presentationml/main" : "http://schemas.openxmlformats.org/presentationml/2006/main";
            var rel = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var list = string.Concat(values.Select((_, index) => $"<sldId id=\"{256 + index}\" r:id=\"s{index + 1}\"/>"));
            parts["content/main.xml"] = $"<presentation xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sldIdLst>{list}</sldIdLst></presentation>";
            parts["content/_rels/main.xml.rels"] = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(values.Select((_, index) => $"<Relationship Id=\"s{index + 1}\" Type=\"{rel}/slide\" Target=\"slide{index + 1}.xml\"/>")) + "</Relationships>";
            for (var index = 0; index < values.Length; index++)
            {
                parts[$"content/slide{index + 1}.xml"] = $"<sld xmlns=\"{ns}\"{(values[index] is null ? "" : " show=\"" + values[index] + "\"")}><cSld/></sld>";
                parts["[Content_Types].xml"] = parts["[Content_Types].xml"].Replace("</Types>",
                    $"<Override PartName=\"/content/slide{index + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/></Types>");
            }
            return parts;
        }
    }

    private static async Task NoVisibleSlidesCommandAsync(string scratch, byte[] bytes, string name, Action<bool, string> check)
    {
        var stage = Path.GetFullPath(Path.Combine(scratch, "office-no-slides-" + name + "-" + Guid.NewGuid().ToString("N")));
        var runtime = Path.Combine(stage, "runtime"); Directory.CreateDirectory(runtime);
        var original = Path.Combine(stage, name + ".pptx"); File.WriteAllBytes(original, bytes);
        var written = File.GetLastWriteTimeUtc(original);
        var contexts = Path.Combine(stage, "prepared", "contexts");
        try
        {
            using var prepared = await OfficeContextPreparation.CreateAsync(contexts, runtime, original, "pptx", "none", default, createContextRoot: true);
            check(false, "slides: known empty presentation reached context preparation");
        }
        catch (InvalidDataException error)
        {
            check(error.Message.Contains("no visible slides to export") && !Directory.Exists(Path.Combine(stage, "prepared")),
                "slides: " + name + " refusal precedes context, journal and snapshot creation");
        }
        using (var exclusive = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None))
            check(exclusive.Length == bytes.Length && File.GetLastWriteTimeUtc(original) == written,
                "slides: preparation refusal releases unchanged original lease: " + name);
        foreach (var marker in new[] { "office-engine/ContextSuite.OfficeHost.exe", "office-engine/runtime-files.txt", "pdf-engine/qpdf.exe", "pdf-renderer/ContextSuite.PdfRenderer.exe" })
        {
            var path = Path.Combine(runtime, marker.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "non-executable presence marker");
        }
        var access = new DirectOfficeAccess(); var prompts = 0;
        await using var worker = new WorkerClient(Path.Combine(runtime, "ContextSuite.Worker.exe"), Path.Combine(stage, "scratch"));
        await using (var vm = new MainViewModel(worker, new SuiteSettings { Convert = new(ReplaceOriginals: true) },
            new OutputPublisher(Path.Combine(stage, "publications"), null!), access, Path.Combine(stage, "contexts")))
        {
            vm.OfficeCalculationRequested += _ => { prompts++; return Task.FromResult<string?>("cached"); };
            vm.OfficeFontsRequested += (_, _) => { prompts++; return Task.FromResult(true); };
            vm.Admit(new(Guid.NewGuid(), "convert", "pdf", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows.Single().Result.State == OperationState.Unsupported && vm.Rows.Single().Status.Contains("Add or unhide a slide") &&
                prompts == 0 && access.Admissions == 0 && worker.ProcessId is null,
                "slides: " + name + " command explains no export before prompts, paid admission or worker startup");
            vm.RetryFailed(); await vm.WaitForIdleAsync();
            check(vm.Rows.All(row => row.Result.State == OperationState.Unsupported) && prompts == 0 && access.Admissions == 0 && worker.ProcessId is null,
                "slides: " + name + " retry retains the same no-slide explanation without dispatch");
        }
        check(File.ReadAllBytes(original).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(original) == written &&
            !Directory.Exists(Path.Combine(stage, "contexts")) && !Directory.Exists(Path.Combine(stage, "publications")) &&
            !Directory.EnumerateFiles(stage, "*.pdf", SearchOption.AllDirectories).Any(),
            "slides: " + name + " refusal creates no PDF or reservation and preserves the original");
    }
}
