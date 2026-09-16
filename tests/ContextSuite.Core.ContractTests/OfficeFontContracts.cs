using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Office;

internal static class OfficeFontContracts
{
    internal static void Run(Action<bool, string> check)
    {
        var request = Guid.NewGuid(); var hash = new string('A', 64);
        var fields = new Dictionary<string, object?> { ["version"] = 2, ["requestId"] = request.ToString("N"),
            ["completed"] = true, ["format"] = "docx", ["policy"] = OfficeHostProtocol.Policy, ["calculation"] = "none",
            ["sourceBytes"] = 100, ["outputBytes"] = 200, ["sourceSha256"] = hash, ["outputSha256"] = hash };
        var fonts = Read(new[] { Hex("{\"fontsmissing\":[\"Zeta\",\"日本語 \\\"Font\\\"\"]}"), Hex("{\"fontsmissing\":[\"zeta\",\"Alpha\"]}") });
        check(fonts.MissingFontFamilies.SequenceEqual(new[] { "Alpha", "Zeta", "日本語 \"Font\"" }),
            "Office fonts: bounded Unicode names survive and repeated events merge deterministically");
        var restored = JsonSerializer.Deserialize<OfficeHostCompletion>(JsonSerializer.Serialize(fonts))!;
        check(restored.MissingFontFamilies.SequenceEqual(fonts.MissingFontFamilies), "Office fonts: worker JSON preserves typed names");
        check(Read(Array.Empty<string>()).MissingFontFamilies.IsEmpty, "Office fonts: explicit empty callback list stays quiet");
        foreach (var invalid in new object?[] { null, "", new[] { "" }, new[] { "0" }, new[] { "GG" }, new[] { "FF" },
            new object?[] { null }, new[] { 3 }, Enumerable.Repeat(Hex("{\"fontsmissing\":[\"A\"]}"), 5).ToArray() })
            Refuse(() => Read(invalid), "invalid callback framing");
        foreach (var json in new[] { "null", "[]", "{}", "{\"other\":[\"A\"]}", "{\"fontsmissing\":null}",
            "{\"fontsmissing\":[]}", "{\"fontsmissing\":[null]}", "{\"fontsmissing\":[3]}", "{\"fontsmissing\":[\"\"]}",
            "{\"fontsmissing\":[\" \" ]}", "{\"fontsmissing\":[\"A\\nB\"]}", "{\"fontsmissing\":[\"\\ud800\"]}",
            "{\"fontsmissing\":[\"A\",\"a\"]}", "{\"fontsmissing\":[\"A\"],\"extra\":true}",
            "{\"fontsmissing\":[\"A\"],\"fontsmissing\":[\"B\"]}", "{\"fontsmissing\":[[[\"A\"]]]}" })
            Refuse(() => Read(new[] { Hex(json) }), "malformed font report");
        Refuse(() => Read(new[] { Hex("{\"fontsmissing\":[\"" + new string('A', 129) + "\"]}") }), "font name limit");
        var names = Enumerable.Range(0, 65).Select(index => "Font " + index).ToArray();
        Refuse(() => Read(new[] { Hex(JsonSerializer.Serialize(new { fontsmissing = names })) }), "font count limit");
        Refuse(() => Read(new[] { Hex(JsonSerializer.Serialize(new { fontsmissing = names[..64] })),
            Hex(JsonSerializer.Serialize(new { fontsmissing = names[64..] })) }), "merged font count limit");
        var padded = "{\"fontsmissing\":[\"A\"]}".PadRight(OfficeHostProtocol.MaximumFontReportBytes);
        check(Read(new[] { Hex(padded) }).MissingFontFamilies.SequenceEqual(new[] { "A" }), "Office fonts: maximum bounded payload accepted");
        Refuse(() => Read(new[] { Hex(padded), Hex("{}") }), "aggregate decoded byte limit");
        Refuse(() => OfficeHostProtocol.ValidateFontFamilies(default), "missing typed font report");
        foreach (ImmutableArray<string> invalid in new ImmutableArray<string>[] { ["\ud800"], ["A\0B"], ["A", "a"], [null!],
            Enumerable.Range(0, 64).Select(index => index + new string('界', 120)).ToImmutableArray() })
            Refuse(() => OfficeHostProtocol.ValidateFontFamilies(invalid), "invalid typed font names");

        OfficeHostCompletion Read(object? reports)
        {
            fields["fontReports"] = reports;
            return OfficeHostProtocol.ReadCompletion(JsonSerializer.SerializeToUtf8Bytes(fields), 0, request, "docx", "none", 100, hash);
        }
        static string Hex(string json) => Convert.ToHexString(Encoding.UTF8.GetBytes(json));
        void Refuse(Action action, string reason)
        {
            try { action(); check(false, "Office fonts accepted " + reason); }
            catch (InvalidDataException) { check(true, "Office fonts refuse " + reason); }
        }
    }
}
