using System.Text;
using System.Text.Json.Nodes;
using ContextSuite.Core.Analysis;

internal static class PdfProbeContracts
{
    public static void Run(Action<bool, string> check)
    {
        const string fixture = """
            {"version":2,"pages":[{"pageposfrom1":1},{"pageposfrom1":2}],
             "encrypt":{"encrypted":false},
             "acroform":{"hasacroform":true,"needappearances":false,"fields":[{"fieldtype":"/Tx"},{"fieldtype":"/Sig"}]},
             "attachments":{"authored.txt":{}},"outlines":[{"kids":[{"kids":[]}]}]}
            """;
        var facts = PdfProbeParser.Parse(Encoding.UTF8.GetBytes(fixture));
        check(facts.PageCount == 2 && facts.FormFieldCount == 2 && facts.ReportedSignatureFieldCount == 1 &&
            facts.AttachmentCount == 1 && facts.OutlineCount == 2 && !facts.IsEncrypted,
            "PDF probe: typed declarations count pages, fields, attachments and nested outlines");
        var empty = JsonNode.Parse(fixture)!;
        empty["pages"] = new JsonArray(); empty["outlines"] = new JsonArray(); empty["attachments"] = new JsonObject();
        empty["acroform"]!["hasacroform"] = false; empty["acroform"]!["fields"] = new JsonArray();
        empty["encrypt"]!["encrypted"] = true;
        var emptyFacts = PdfProbeParser.Parse(Encoding.UTF8.GetBytes(empty.ToJsonString()));
        check(emptyFacts.PageCount == 0 && emptyFacts.IsEncrypted && emptyFacts.HasForms == false && emptyFacts.ReportedSignatureFieldCount == 0,
            "PDF probe: empty inventories and encryption are distinct from unavailable results");
        var invalid = new List<string> { "{}", "[]", "null", "{", fixture.Replace("\"version\":2", "\"version\":3"),
            fixture.Replace("\"version\":2", "\"version\":2,\"version\":2"),
            fixture.Replace("\"encrypted\":false", "\"encrypted\":null"),
            fixture.Replace("\"pageposfrom1\":2", "\"pageposfrom1\":1"),
            fixture.Replace("\"pageposfrom1\":2", "\"pageposfrom1\":2.5"),
            fixture.Replace("\"hasacroform\":true", "\"hasacroform\":false"),
            fixture.Replace("\"fieldtype\":\"/Sig\"", "\"fieldtype\":null"),
            fixture.Replace("\"fieldtype\":\"/Sig\"", "\"fieldtype\":\"" + new string('x', 129) + "\"") };
        foreach (var key in new[] { "pages", "encrypt", "acroform", "attachments", "outlines" })
        {
            var missing = JsonNode.Parse(fixture)!.AsObject(); missing.Remove(key); invalid.Add(missing.ToJsonString());
        }
        var excess = JsonNode.Parse(fixture)!;
        excess["pages"] = new JsonArray(Enumerable.Range(1, 4097).Select(index => (JsonNode)new JsonObject { ["pageposfrom1"] = index }).ToArray());
        invalid.Add(excess.ToJsonString());
        var deep = JsonNode.Parse(fixture)!;
        deep["outlines"] = JsonNode.Parse(string.Concat(Enumerable.Repeat("[{\"kids\":", 20)) + "[]" + string.Concat(Enumerable.Repeat("}]", 20)));
        invalid.Add(deep.ToJsonString());
        foreach (var input in invalid)
        {
            try { PdfProbeParser.Parse(Encoding.UTF8.GetBytes(input)); check(false, "PDF probe: malformed, contradictory or oversized declarations rejected"); }
            catch (InvalidDataException) { check(true, "PDF probe: malformed, contradictory or oversized declarations rejected"); }
        }
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[PdfProbeParser.MaximumBytes + 1] })
        {
            try { PdfProbeParser.Parse(bytes); check(false, "PDF probe: payload byte limit"); }
            catch (InvalidDataException) { check(true, "PDF probe: payload byte limit"); }
        }
        var locked = new PdfProbeFacts(null, true, null, null, null, null, null, null);
        locked.Validate();
        check(true, "PDF facts: locked encryption retains unavailable content facts");
        foreach (var invalidFacts in new[] { locked with { IsEncrypted = false }, facts with { PageCount = -1 },
            facts with { ReportedSignatureFieldCount = 3 }, facts with { AttachmentCount = 4097 }, facts with { NeedsFormAppearances = null } })
        {
            try { invalidFacts.Validate(); check(false, "PDF facts: contradictory worker facts rejected"); }
            catch (InvalidDataException) { check(true, "PDF facts: contradictory worker facts rejected"); }
        }
    }
}
