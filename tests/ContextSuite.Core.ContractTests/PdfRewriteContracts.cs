using System.Text;
using System.Text.Json.Nodes;
using ContextSuite.Core.Pdf;

internal static class PdfRewriteContracts
{
    private const string Fixture = """
        {"qpdf":[{"jsonversion":2,"pdfversion":"1.7","pushedinheritedpageresources":false,"calledgetallpages":false,"maxobjectid":3},
        {"obj:1 0 R":{"value":{"/Type":"/Catalog","/Pages":"2 0 R"}},
         "obj:2 0 R":{"value":{"/Type":"/Pages","/Parent":"1 0 R","/Contents":"3 0 R"}},
         "obj:3 0 R":{"stream":{"dict":{},"data":"VGV4dA=="}},"trailer":{"value":{"/Root":"1 0 R","/Size":4}}}]}
        """;
    public static void Run(Action<bool, string> check)
    {
        var bytes = Encoding.UTF8.GetBytes(Fixture);
        var before = PdfRewriteInventory.Read(bytes);
        check(before.ReachableObjects == 3 && before.SemanticSha256.Length == 64 && before.PdfVersion == "1.7", "PDF rewrite: bounded cyclic graph and stream inventory");
        var renumbered = Fixture.Replace("1 0 R", "11 0 R").Replace("2 0 R", "12 0 R").Replace("3 0 R", "13 0 R");
        PdfRewriteInventory.RequirePreserved(before, PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(renumbered)));
        check(true, "PDF rewrite: object renumbering retains semantic identity");
        var withOrphan = JsonNode.Parse(Fixture)!; Objects(withOrphan)["obj:9 0 R"] = new JsonObject { ["value"] = "u:Unreferenced information" };
        var orphanFacts = PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(withOrphan.ToJsonString()));
        try { PdfRewriteInventory.RequirePreserved(orphanFacts, before); check(false, "PDF rewrite: lost unreferenced information"); }
        catch (InvalidDataException) { check(true, "PDF rewrite: dropping ordinary unreferenced information rejects output"); }
        var renamedOrphan = withOrphan.ToJsonString().Replace("obj:9 0 R", "obj:19 0 R");
        PdfRewriteInventory.RequirePreserved(orphanFacts, PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(renamedOrphan)));
        check(true, "PDF rewrite: unreferenced object renumbering retains content");
        var extraStorage = JsonNode.Parse(Fixture)!;
        Objects(extraStorage)["obj:9 0 R"] = new JsonObject { ["stream"] = new JsonObject
            { ["dict"] = new JsonObject { ["/Type"] = "/XRef", ["/CustomInformation"] = "u:Retain me" }, ["data"] = "" } };
        try { PdfRewriteInventory.RequirePreserved(PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(extraStorage.ToJsonString())), before); check(false, "PDF rewrite: custom storage metadata"); }
        catch (InvalidDataException) { check(true, "PDF rewrite: unfamiliar storage metadata cannot disappear as a technical exception"); }
        var duplicate = Fixture.Replace("\"/Type\":\"/Catalog\"", "\"/Type\":\"/Catalog\",\"/#54ype\":\"/Catalog\"");
        try { PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(duplicate)); check(false, "PDF rewrite: duplicate escaped keys"); }
        catch (InvalidDataException) { check(true, "PDF rewrite: escaped duplicate keys are rejected"); }
        foreach (var property in new[] { "/FT", "/Type", "/#46T" })
            Reject(node => Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = new JsonObject { [property] = "/S#69g" } }, "unreachable signature " + property);
        foreach (var property in new[] { "/ByteRange", "/SigFlags", "/Perms", "/DSS", "/Encrypt" })
            Reject(node => Objects(node)["obj:4 0 R"] = new JsonObject { ["value"] = new JsonObject { [property] = 0 } }, "unreachable protected information " + property);
        Reject(node => Objects(node)["trailer"]!["value"]!["/Prev"] = 123, "uninspected revision history");
        Reject(node => Objects(node)["trailer"]!["value"]!["/#50rev"] = 123, "escaped revision history");
        Reject(node => Objects(node)["obj:3 0 R"]!["stream"]!["dict"]!["/#46"] = "u:external.bin", "external stream");
        Reject(node => Objects(node)["obj:3 0 R"]!["stream"]!.AsObject().Remove("data"), "missing stream bytes");
        Reject(node => Objects(node)["obj:3 0 R"]!["stream"]!["datafile"] = "outside", "external JSON stream file");
        Reject(node => Objects(node)["obj:3 0 R"]!["stream"]!["data"] = "bad!", "malformed base64");
        Reject(node => Objects(node)["obj:2 0 R"]!["value"]!["/Contents"] = "99 0 R", "unresolved reference");
        Reject(node => node["qpdf"]![0]!["jsonversion"] = 1, "old JSON schema");
        Reject(node => node["qpdf"]![0]!["calledgetallpages"] = true, "preprocessed graph");
        Reject(node => node["qpdf"]![0]!["pdfversion"] = "9.0", "unknown PDF version");
        Reject(node => Objects(node)["trailer"]!["value"]!["/ID"] = new JsonArray("b:only-one"), "incomplete identifiers");
        Reject(node => {
            for (var index = 4; index <= PdfRewriteInventory.MaximumObjects + 1; index++)
                Objects(node)["obj:" + index + " 0 R"] = new JsonObject { ["value"] = 0 };
        }, "object count budget");
        Reject(node => {
            for (var index = 4; index <= 80; index++)
                Objects(node)["obj:" + index + " 0 R"] = new JsonObject { ["value"] = new JsonObject { ["/Next"] = (index == 80 ? "1" : (index + 1).ToString()) + " 0 R" } };
            Objects(node)["obj:1 0 R"]!["value"]!["/Extra"] = "4 0 R";
        }, "reference chain depth budget");
        var noData = JsonNode.Parse(Fixture)!; Objects(noData)["obj:3 0 R"]!["stream"]!.AsObject().Remove("data");
        PdfRewriteInventory.CheckAdmission(Encoding.UTF8.GetBytes(noData.ToJsonString()));
        check(true, "PDF rewrite: admission precedes stream extraction but does not produce validated facts");
        var changed = JsonNode.Parse(Fixture)!; Objects(changed)["obj:3 0 R"]!["stream"]!["data"] = "RGlmZmVyZW50";
        try { PdfRewriteInventory.RequirePreserved(before, PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(changed.ToJsonString()))); check(false, "PDF rewrite: changed content"); }
        catch (InvalidDataException) { check(true, "PDF rewrite: changed decoded content rejects output"); }
        foreach (var after in new[] { before with { PdfVersion = "2.0" }, before with { OriginalDocumentId = "b:changed" } })
        {
            try { PdfRewriteInventory.RequirePreserved(before with { OriginalDocumentId = "b:original" }, after); check(false, "PDF rewrite: changed version/identity"); }
            catch (InvalidDataException) { check(true, "PDF rewrite: changed version or original identity rejects output"); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { PdfRewriteInventory.Read(bytes, cancelled.Token); check(false, "PDF rewrite: cancellation"); }
        catch (OperationCanceledException) { check(true, "PDF rewrite: cancellation before inspection"); }
        void Reject(Action<JsonNode> mutate, string name)
        {
            var node = JsonNode.Parse(Fixture)!; mutate(node);
            try { PdfRewriteInventory.Read(Encoding.UTF8.GetBytes(node.ToJsonString())); check(false, "PDF rewrite rejects " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "PDF rewrite rejects " + name); }
        }
    }
    private static JsonObject Objects(JsonNode node) => node["qpdf"]![1]!.AsObject();
}
