using System.Text;
using System.Text.Json;
using ContextSuite.Core.Office;

internal static class OfficeHostContracts
{
    public static void Run(Action<bool, string> check)
    {
        OfficeFontContracts.Run(check);
        var id = Guid.NewGuid(); var sourceHash = new string('A', 64); var outputHash = new string('B', 64);
        foreach (var (format, calculation) in new[] { ("docx", "none"), ("xlsx", "cached"), ("xlsx", "recalculate"), ("pptx", "none") })
        {
            var fields = new Dictionary<string, object> { ["version"] = 2, ["requestId"] = id.ToString("N"), ["completed"] = true,
                ["format"] = format, ["policy"] = OfficeHostProtocol.Policy, ["calculation"] = calculation, ["sourceBytes"] = 100,
                ["outputBytes"] = 200, ["sourceSha256"] = sourceHash, ["outputSha256"] = outputHash, ["fontReports"] = Array.Empty<string>() };
            var valid = JsonSerializer.SerializeToUtf8Bytes(fields);
            var result = OfficeHostProtocol.ReadCompletion(valid, 0, id, format, calculation, 100, sourceHash);
            check(result.RequestId == id && result.OutputBytes == 200 && result.OutputSha256 == outputHash && result.MissingFontFamilies.IsEmpty,
                "Office host: bound completed reply for " + format + "/" + calculation);
            Refuse(valid, 83, "owner crash despite success-shaped reply");
            Refuse([], 0, "zero exit without terminal reply");
            Refuse(valid[..^1], 0, "truncated terminal reply");
            foreach (var (key, value) in new (string, object)[] { ("version", 1), ("requestId", Guid.NewGuid().ToString("N")),
                ("completed", false), ("format", "pdf"), ("policy", "other"), ("calculation", "unspecified"),
                ("sourceBytes", 101), ("outputBytes", 0), ("outputBytes", OfficeHostProtocol.MaximumOutputBytes + 1),
                ("sourceSha256", outputHash), ("outputSha256", new string('G', 64)), ("outputBytes", "200") })
            {
                var changed = new Dictionary<string, object>(fields) { [key] = value };
                Refuse(JsonSerializer.SerializeToUtf8Bytes(changed), 0, "mismatched/invalid " + key);
            }
            foreach (var key in fields.Keys)
            {
                var missing = new Dictionary<string, object>(fields); missing.Remove(key);
                Refuse(JsonSerializer.SerializeToUtf8Bytes(missing), 0, "missing " + key);
            }
            Refuse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid).Replace("{", "{\"completed\":false,", StringComparison.Ordinal)), 0, "duplicate completion field");
            Refuse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid).Replace("{", "{\"extra\":1,", StringComparison.Ordinal)), 0, "unknown field");
            Refuse(Encoding.UTF8.GetBytes("diagnostics\n" + Encoding.UTF8.GetString(valid)), 0, "diagnostics mixed into completion channel");
            Refuse(new byte[OfficeHostProtocol.MaximumReplyBytes + 1], 0, "reply byte limit");

            void Refuse(byte[] bytes, int exit, string reason)
            {
                try { OfficeHostProtocol.ReadCompletion(bytes, exit, id, format, calculation, 100, sourceHash); check(false, "Office host accepted " + reason); }
                catch (InvalidDataException) { check(true, "Office host refuses " + format + ": " + reason); }
            }
        }
    }
}
