using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextSuite.Private.IO;

internal static class LocalLibraryContracts
{
    internal static void Run(string root)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use new absolute evidence.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        var defaults = new Dictionary<string, string> { ["bridge.dll"] = new('A', 64), ["codec.dll"] = new('B', 64) };
        var original = JsonSerializer.Serialize(defaults);
        var valid = JsonSerializer.Serialize(new { formatVersion = 1, anchorName = "bridge.dll", anchorSha256 = defaults["bridge.dll"],
            libraries = new Dictionary<string, string> { ["codec.dll"] = new('c', 64), ["support.dll"] = new('D', 64) } });
        var selected = Parse(valid);
        Check(selected["bridge.dll"] == defaults["bridge.dll"] && selected["codec.dll"] == new string('C', 64) &&
            selected.ContainsKey("support.dll"), "Explicit replacements and additional DLLs accepted; anchor unchanged");
        Check(JsonSerializer.Serialize(defaults) == original, "Default identities unchanged");
        Reject("empty", "");
        Reject("oversized", new string(' ', 65537));
        Reject("array root", "[]");
        Reject("null root", "null");
        Reject("malformed", "{");
        Reject("duplicate root", valid.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal));
        Reject("duplicate library", valid.Replace("\"support.dll\"", "\"CODEC.DLL\"", StringComparison.Ordinal));
        Reject("comment", valid.Insert(1, "/*comment*/"));
        Reject("trailing comma", valid.Insert(valid.Length - 1, ","));
        foreach (var field in new[] { "formatVersion", "anchorName", "anchorSha256", "libraries" })
        {
            var node = JsonNode.Parse(valid)!.AsObject(); node.Remove(field);
            Reject("missing " + field, node.ToJsonString());
            node = JsonNode.Parse(valid)!.AsObject(); node[field] = null;
            Reject("null " + field, node.ToJsonString());
        }
        foreach (var change in new (string Field, JsonNode Value)[]
        {
            ("formatVersion", JsonValue.Create(2)!), ("formatVersion", JsonValue.Create("1")!),
            ("formatVersion", JsonValue.Create(1.1)!), ("anchorName", JsonValue.Create("other.dll")!),
            ("anchorSha256", JsonValue.Create(new string('B', 64))!), ("libraries", new JsonArray()),
            ("unexpected", JsonValue.Create(true)!)
        })
        {
            var node = JsonNode.Parse(valid)!.AsObject(); node[change.Field] = change.Value;
            Reject("changed " + change.Field + " " + change.Value, node.ToJsonString());
        }
        foreach (var name in new[] { "../codec.dll", "sub/codec.dll", "sub\\codec.dll", "codec.dll:stream", "bridge.dll", "codec.exe",
            ".codec.dll", "codec..dll", "codec .dll", "codec.dll ", new string('a', 129) + ".dll" })
        {
            var node = JsonNode.Parse(valid)!; node["libraries"]![name] = new string('E', 64);
            Reject("unsafe name " + name, node.ToJsonString());
        }
        foreach (var hash in new[] { "", new string('C', 63), new string('C', 65), new string('Z', 64) })
        {
            var node = JsonNode.Parse(valid)!; node["libraries"]!["codec.dll"] = hash;
            Reject("invalid hash " + hash.Length, node.ToJsonString());
        }
        var missing = JsonNode.Parse(valid)!; missing["libraries"]!.AsObject().Remove("codec.dll");
        Reject("missing required DLL", missing.ToJsonString());
        var excess = JsonNode.Parse(valid)!;
        for (var index = 0; index < 32; index++) excess["libraries"]![$"extra{index}.dll"] = new string('E', 64);
        Reject("too many DLLs", excess.ToJsonString());

        var path = Path.Combine(root, LocalLibraryManifest.FileName);
        var leases = new List<FileStream>();
        using (var directories = new RuntimeDirectoryLeases())
        {
            try
            {
                Check(ReferenceEquals(LocalLibraryManifest.Read(root, "bridge.dll", defaults, directories, leases), defaults),
                    "Absent manifest retains defaults");
                File.WriteAllText(path, valid, new UTF8Encoding(false));
                Check(LocalLibraryManifest.Read(root, "bridge.dll", defaults, directories, leases)["codec.dll"] == new string('C', 64),
                    "Manifest read through verified handle");
                var refused = false;
                try { using var write = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
                catch (IOException) { refused = true; }
                Check(refused, "Manifest cannot be changed while leased");
                refused = false;
                try { File.Delete(path); }
                catch (IOException) { refused = true; }
                Check(refused, "Manifest cannot be deleted while leased");
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
        }
        File.WriteAllText(path, "invalid");
        Check(File.ReadAllText(path) == "invalid", "Manifest writable after lifetime ends");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} local library manifest checks.");

        IReadOnlyDictionary<string, string> Parse(string value) => LocalLibraryManifest.Parse(Encoding.UTF8.GetBytes(value), "bridge.dll", defaults);
        void Reject(string name, string value)
        {
            try { Parse(value); }
            catch (InvalidDataException) { checks.Add("Refused " + name); return; }
            throw new InvalidDataException("Accepted " + name);
        }
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidDataException(name); checks.Add(name); }
    }
}
