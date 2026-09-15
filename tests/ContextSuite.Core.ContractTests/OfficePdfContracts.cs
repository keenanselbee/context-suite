using System.Text;
using System.Text.Json.Nodes;
using ContextSuite.Core.Office;
using ContextSuite.Core.Transport;
using System.Text.Json;

internal static class OfficePdfContracts
{
    private const string Fixture = """
        {"qpdf":[{"jsonversion":2,"pdfversion":"1.7","pushedinheritedpageresources":false,"calledgetallpages":false,"maxobjectid":3},
        {"obj:1 0 R":{"value":{"/Type":"/Catalog","/Pages":"2 0 R","/OpenAction":["3 0 R","/XYZ",null,null,0]}},
         "obj:2 0 R":{"value":{"/Type":"/Pages","/Kids":["3 0 R"],"/Count":1}},
         "obj:3 0 R":{"value":{"/Type":"/Page","/Parent":"2 0 R","/MediaBox":[0,0,612,792]}},
         "trailer":{"value":{"/Root":"1 0 R","/Size":4}}}]}
        """;

    public static void Run(Action<bool, string> check)
    {
        OfficePdfPolicy.CheckObjects(Encoding.UTF8.GetBytes(Fixture));
        check(true, "Office PDF: ordinary page destination is accepted without permitting automatic actions");
        Accept("""{"/Type":"/StructElem","/S":"/P","/A":{"/O":"/Layout","/BBox":[0,0,1,1]}}""", "tagged layout attributes are not mistaken for actions");
        Accept("""{"/Type":"/Annot","/Subtype":"/Link","/A":{"/S":"/URI","/URI":"u:https://example.invalid/"}}""", "ordinary URI links remain data");
        Accept("""{"/Type":"/Annot","/Subtype":"/Link","/A":{"/S":"/GoToR","/F":"u:related.pdf","/D":[0,"/Fit"]}}""", "manual related-PDF links remain data");
        foreach (var key in new[] { "/AcroForm", "/XFA", "/JavaScript", "/J#53", "/AA", "/EmbeddedFiles", "/EF", "/RichMediaContent", "/RichMediaSettings", "/Collection" })
            Reject(node => Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = new JsonObject { [key] = new JsonObject() } }, "unexpected " + key);
        foreach (var type in new[] { "/EmbeddedFile", "/Widget", "/FileAttachment", "/3D", "/RichMedia", "/Screen" })
            Reject(node => Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = new JsonObject { ["/Subtype"] = type } }, "unexpected object " + type);
        foreach (var action in new[] { "/JavaScript", "/Launch", "/SubmitForm", "/GoToE", "/Rendition", "/Sound", "/Hide" })
            Reject(node => Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = new JsonObject { ["/S"] = action } }, "unreachable action " + action);
        Reject(node => Objects(node)["obj:4 0 R"] = JsonNode.Parse("""{"value":{"/#54ype":"/Annot","/Subtype":"/Link","/#41":{"/S":"/Unknown"}}}"""), "escaped annotation action cannot bypass policy");
        Reject(node => Objects(node)["obj:4 0 R"] = JsonNode.Parse("""{"value":{"/#54ype":"/Action","/#53":"/Unknown"}}"""), "escaped action type cannot bypass policy");
        Reject(node => Objects(node)["obj:4 0 R"] = JsonNode.Parse("""{"value":{"/Type":"/Action","/S":"/URI","/URI":"u:https://example.invalid/","/Next":{"/S":"/GoTo"}}}"""), "unreviewed action chains are refused");
        Reject(node => Objects(node)["obj:1 0 R"]!["value"]!["/OpenAction"] = JsonNode.Parse("""{"/S":"/URI","/URI":"u:https://example.invalid/"}"""), "automatic URL action is refused");
        Reject(node => Objects(node)["obj:1 0 R"]!["value"]!["/OpenAction"] = JsonNode.Parse("""["3 0 R","/XYZ",null,null,"u:bad"]"""), "opening geometry must be numeric");
        Reject(node => node["qpdf"]![0]!["pdfversion"] = "2.0", "fixed PDF version is enforced");
        Reject(node => Objects(node)["obj:4 0 R"] = JsonNode.Parse("""{"value":{"/ByteRange":[0,0,0,0]}}"""), "signature information is refused by full inventory");
        Reject(node => Objects(node)["obj:4 0 R"] = JsonNode.Parse("""{"stream":{"dict":{"/F":"u:external.dat"}}}"""), "external streams are refused before decoding");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { OfficePdfPolicy.CheckObjects(Encoding.UTF8.GetBytes(Fixture), cancelled.Token); check(false, "Office PDF cancellation"); }
        catch (OperationCanceledException) { check(true, "Office PDF: cancellation precedes object inspection"); }
        check(OfficePdfPolicy.ReadPageCount("1\n"u8) == 1 && OfficePdfPolicy.ReadPageCount("4096\n"u8) == 4096, "Office PDF: explicit page-count boundaries");
        foreach (var value in new[] { "0", "4097", "-1", "+1", "1.0", "1\n2", "" })
        {
            try { OfficePdfPolicy.ReadPageCount(Encoding.ASCII.GetBytes(value)); check(false, "Office PDF invalid count"); }
            catch (InvalidDataException) { check(true, "Office PDF: invalid count refused: " + value); }
        }
        var id = Guid.NewGuid();
        var work = new OfficeExportWork(id, Path.Combine(Path.GetTempPath(), "office-" + id.ToString("N")),
            "ContextSuite.Office." + id.ToString("N"), "docx", "none", 8, new string('A', 64));
        var candidate = new OfficeExportCandidate(id, new(Guid.NewGuid(), "docx", "none", 8, 16, work.SourceSha256, new string('B', 64)), work.Policy);
        var request = new OfficePdfWork(work, candidate, Path.Combine(Path.GetTempPath(), ".context-suite-" + id.ToString("N") + ".tmp"));
        request.Validate(); check(true, "Office PDF: request binds completed export and external reservation");
        var command = new WorkerCommand(1, Guid.NewGuid(), "office-pdf-validate", OfficePdf: request);
        var restored = JsonSerializer.Deserialize<WorkerCommand>(JsonSerializer.Serialize(command))!;
        restored.Validate(); check(restored == command, "Office PDF: typed worker request round trip");
        foreach (var invalid in new[] { command with { OfficePdf = null }, command with { Command = "office-export" },
            command with { Command = "capabilities" }, command with { OfficeWork = work }, command with { AudioBytes = [1] },
            command with { PdfBytes = [1] }, command with { PdfFile = new(id, request.TemporaryPath) },
            command with { AudioTarget = ContextSuite.Core.Audio.AudioFormat.Flac } })
        {
            try { invalid.Validate(); check(false, "Office PDF mixed worker command"); }
            catch (InvalidDataException) { check(true, "Office PDF: missing or mixed worker payload refused"); }
        }
        var result = new OfficePdfResult(new(id, candidate.Completion.OutputSha256, true), work.SourceBytes, work.SourceSha256,
            candidate.Completion.OutputBytes, 1, request.Policy, "pinned readers");
        var reply = new WorkerReply(1, command.RequestId, [], OfficePdfResult: result);
        var restoredReply = JsonSerializer.Deserialize<WorkerReply>(JsonSerializer.Serialize(reply))!;
        restoredReply.OfficePdfResult!.Validate(request);
        check(restoredReply.OfficePdfResult == result, "Office PDF: typed validation reply round trip");
        foreach (var invalid in new[] { result with { Validation = null! }, result with { Validation = result.Validation with { ItemId = Guid.NewGuid() } },
            result with { Validation = result.Validation with { MatchesPlan = false } }, result with { Validation = result.Validation with { Sha256 = new string('0', 64) } },
            result with { SourceBytes = 7 }, result with { SourceSha256 = new string('0', 64) }, result with { OutputBytes = 1 },
            result with { PageCount = 0 }, result with { PageCount = OfficePdfPolicy.MaximumPages + 1 }, result with { Policy = "other" },
            result with { EngineIdentity = "" }, result with { EngineIdentity = new string('x', 257) }, result with { EngineIdentity = "engine\nextra" } })
        {
            try { invalid.Validate(request); check(false, "Office PDF invalid validation reply"); }
            catch (InvalidDataException) { check(true, "Office PDF: mismatched validation reply refused"); }
        }
        foreach (var invalid in new[] { request with { Policy = "unknown" }, request with { Candidate = candidate with { ItemId = Guid.NewGuid() } },
            request with { TemporaryPath = Path.Combine(work.DirectoryPath, Path.GetFileName(request.TemporaryPath)) },
            request with { TemporaryPath = Path.Combine(Path.GetTempPath(), "unreserved.tmp") } })
        {
            try { invalid.Validate(); check(false, "Office PDF invalid work"); }
            catch (InvalidDataException) { check(true, "Office PDF: mismatched policy, identity or reservation refused"); }
        }

        void Accept(string value, string label)
        {
            var node = JsonNode.Parse(Fixture)!; Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = JsonNode.Parse(value) };
            OfficePdfPolicy.CheckObjects(Encoding.UTF8.GetBytes(node.ToJsonString())); check(true, "Office PDF: " + label);
        }
        void Reject(Action<JsonNode> change, string label)
        {
            var node = JsonNode.Parse(Fixture)!; change(node);
            try { OfficePdfPolicy.CheckObjects(Encoding.UTF8.GetBytes(node.ToJsonString())); check(false, "Office PDF refusal: " + label); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "Office PDF: " + label); }
        }
    }
    private static JsonObject Objects(JsonNode node) => node["qpdf"]![1]!.AsObject();
}
