using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContextSuite.Core.Analysis;

if (args.Length != 2) throw new ArgumentException("Expected qpdf executable and new scratch directory.");
var executable = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
var checks = new List<object>();
var runs = new List<object>();
var failures = new List<string>();
var processEnvironment = Environment.OSVersion.VersionString;
var input = Path.Combine(root, "authored original ü.pdf");
var output = Path.Combine(root, "optimized copy ü.pdf");
await File.WriteAllBytesAsync(input, PdfFixture.Create());
var originalHash = Hash(input);
var originalTime = File.GetLastWriteTimeUtc(input);
await RequireSuccess("version", "--version");
await RequireSuccess("check-source", "--suppress-recovery", "--check", input);
var before = await Probe("source", input);
Check(before.PageCount == 2 && !before.IsEncrypted && before.HasForms == true && before.FormFieldCount == 1 &&
    before.AttachmentCount == 1 && before.OutlineCount == 1 && before.NeedsFormAppearances == false,
    "generated PDF reports expected pages, form, attachment and bookmark");
var graphBefore = await Snapshot("source", input);
await RequireSuccess("optimize", "--suppress-recovery", "--object-streams=generate", "--compress-streams=y",
    "--decode-level=generalized", "--recompress-flate", "--compression-level=9", input, output);
await RequireSuccess("check-output", "--suppress-recovery", "--check", output);
var after = await Probe("output", output);
Check(before == after, "typed PDF inventory is unchanged");
var graphAfter = await Snapshot("output", output);
Check(graphBefore == graphAfter, "root and info object graphs including decoded streams are unchanged under qpdf inspection");
Check(new FileInfo(output).Length < new FileInfo(input).Length, "structural recompression produces a smaller copy");
Check(Hash(input) == originalHash && File.GetLastWriteTimeUtc(input) == originalTime, "source bytes and write timestamp are unchanged");
// Exercise the same recipe on an existing object-stream/Flate input.
var second = Path.Combine(root, "second-copy.pdf");
await RequireSuccess("optimize-again", "--suppress-recovery", "--object-streams=generate", "--compress-streams=y",
    "--decode-level=generalized", "--recompress-flate", "--compression-level=9", output, second);
Check(await Snapshot("second", second) == graphAfter, "already compressed PDF retains its object graph");

var encrypted = Path.Combine(root, "encrypted.pdf");
await RequireSuccess("create-encrypted", input, "--encrypt", "TEST-ONLY", "TEST-OWNER", "256", "--", encrypted);
var locked = await Run("probe-locked", ProbeArguments(encrypted));
Check(locked.ExitCode == 2, "password-required input fails without a supplied password");
var encryptedStatus = await Run("encrypted-status", ["--is-encrypted", encrypted]);
Check(encryptedStatus.ExitCode == 0, "encryption can be identified without bypassing the password");
var openEncrypted = Path.Combine(root, "owner-protected.pdf");
await RequireSuccess("create-owner-protected", input, "--encrypt", "", "TEST-OWNER", "256", "--", openEncrypted);
Check((await Probe("owner-protected", openEncrypted)).IsEncrypted, "empty-user-password encryption remains visible in typed facts");

var signed = Path.Combine(root, "signature-field-canary.pdf");
await File.WriteAllBytesAsync(signed, PdfFixture.Create(signatureField: true));
Check((await Probe("signature-canary", signed)).ReportedSignatureFieldCount == 1,
    "signature field is detectable without claiming the placeholder signature is valid");
var unattached = Path.Combine(root, "unattached-signature-canary.pdf");
await File.WriteAllBytesAsync(unattached, PdfFixture.Create(signatureField: true, signatureWidget: false));
Check((await Probe("unattached-signature", unattached)).ReportedSignatureFieldCount == 0,
    "qpdf summary omits the unattached signature field: zero reported fields must never authorize rewriting");
var malformed = Path.Combine(root, "broken-xref.pdf");
var damaged = Encoding.UTF8.GetString(PdfFixture.Create());
damaged = Regex.Replace(damaged, @"startxref\n[0-9]+", "startxref\n1");
await File.WriteAllTextAsync(malformed, damaged, new UTF8Encoding(false));
Check((await Run("broken-xref", ["--suppress-recovery", "--check", malformed])).ExitCode == 2,
    "cross-reference damage is rejected with heuristic recovery disabled");
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var active = Path.Combine(root, "javascript-canary.pdf");
await File.WriteAllBytesAsync(active, PdfFixture.Create(port));
await Probe("javascript-canary", active);
Check(!listener.Pending(), "generated JavaScript URL is not requested during qpdf inspection");
listener.Stop();

var report = new
{
    status = failures.Count == 0 ? "narrow generated-fixture evaluation passed" : "failed",
    engine = new { path = executable, sha256 = Hash(executable), version = "12.4.1" },
    environment = processEnvironment,
    source = new { sha256 = originalHash, bytes = new FileInfo(input).Length },
    output = new { sha256 = Hash(output), bytes = new FileInfo(output).Length },
    before, after, checks, runs, failures,
    limitations = new[] { "Not independent rendered-page validation; both graph snapshots use qpdf.",
        "No application/worker integration, production sandbox, publication, or release preset acceptance.",
        "Signature fixtures are invalid placeholders; a field without a page widget is omitted by qpdf summary. Zero reported signatures does not mean unsigned.",
        "Generated test PDFs do not establish general document, PDF/A, font, color-profile or accessibility conformance." }
};
await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PDF evaluation: {checks.Count - failures.Count}/{checks.Count} checks passed; {new FileInfo(input).Length} -> {new FileInfo(output).Length} bytes.");
if (failures.Count != 0) throw new InvalidDataException(string.Join("; ", failures));

void Check(bool value, string name)
{
    checks.Add(new { name, passed = value });
    if (!value) failures.Add(name);
    Console.WriteLine((value ? "PASS: " : "FAIL: ") + name);
}

async Task RequireSuccess(string label, params string[] arguments)
{
    var result = await Run(label, arguments);
    if (result.ExitCode != 0) throw new InvalidDataException(label + ": " + result.Error);
}

string[] ProbeArguments(string path) => ["--suppress-recovery", "--json=2", "--json-key=pages", "--json-key=encrypt",
    "--json-key=acroform", "--json-key=attachments", "--json-key=outlines", path];

async Task<PdfProbeFacts> Probe(string label, string path)
{
    var result = await Run("probe-" + label, ProbeArguments(path));
    if (result.ExitCode != 0) throw new InvalidDataException(label + ": " + result.Error);
    await File.WriteAllTextAsync(Path.Combine(root, label + ".probe.json"), result.Output);
    return PdfProbeParser.Parse(Encoding.UTF8.GetBytes(result.Output));
}

async Task<string> Snapshot(string label, string path)
{
    var result = await Run("graph-" + label, ["--suppress-recovery", "--json-output=2", "--decode-level=generalized", "--json-stream-data=inline", path]);
    if (result.ExitCode != 0) throw new InvalidDataException(label + ": " + result.Error);
    await File.WriteAllTextAsync(Path.Combine(root, label + ".graph.json"), result.Output);
    var parsed = JsonNode.Parse(result.Output)!;
    var objects = parsed["qpdf"]![1]!.AsObject();
    var trailer = objects["trailer"]!["value"]!;
    var ids = new Dictionary<string, int>(StringComparer.Ordinal);
    var canonical = Canonical(new JsonObject { ["root"] = trailer["/Root"]!.DeepClone(), ["info"] = trailer["/Info"]!.DeepClone() });
    var serialized = canonical!.ToJsonString();
    await File.WriteAllTextAsync(Path.Combine(root, label + ".canonical.json"), serialized);
    return serialized;

    JsonNode? Canonical(JsonNode? node, bool isStreamDictionary = false)
    {
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var value) && Regex.IsMatch(value, @"^\d+ \d+ R$"))
        {
            if (ids.TryGetValue(value, out var existing)) return JsonValue.Create("reference:" + existing);
            var index = ids.Count;
            ids.Add(value, index);
            return new JsonObject { ["index"] = index, ["object"] = Canonical(objects["obj:" + value] ?? throw new InvalidDataException("Missing graph object.")) };
        }
        if (node is JsonObject dictionary)
        {
            var normalized = new JsonObject();
            foreach (var property in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                // Stream Length is storage representation. Decoded data and every
                // other dictionary value remain compared, including filter state.
                if (isStreamDictionary && property.Key == "/Length") continue;
                normalized[property.Key] = Canonical(property.Value, property.Key == "dict" && dictionary.ContainsKey("data"));
            }
            return normalized;
        }
        if (node is JsonArray array) return new JsonArray(array.Select(item => Canonical(item)).ToArray());
        return node?.DeepClone();
    }
}

async Task<ProcessResult> Run(string label, string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    // Do not inherit qpdf developer knobs that can alter compression or allocation.
    foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("QPDF_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    using var process = new Process { StartInfo = start };
    var timer = Stopwatch.StartNew();
    if (!process.Start()) throw new IOException("Cannot start evaluation process.");
    using var registration = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
    var stdout = ReadBounded(process.StandardOutput.BaseStream, 4 * 1024 * 1024);
    var stderr = ReadBounded(process.StandardError.BaseStream, 64 * 1024);
    try
    {
        await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
        if (Directory.EnumerateFiles(root, "*.pdf").Any(path => new FileInfo(path).Length > 8 * 1024 * 1024))
            throw new InvalidDataException("Evaluation output file limit exceeded.");
        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
        runs.Add(new { label, arguments, result.ExitCode, elapsedMs = timer.ElapsedMilliseconds, stdoutBytes = Encoding.UTF8.GetByteCount(result.Output), result.Error });
        return result;
    }
    catch
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        await process.WaitForExitAsync();
        throw;
    }

    async Task<string> ReadBounded(Stream stream, int limit)
    {
        try
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) != 0)
            {
                if (output.Length + count > limit) throw new InvalidDataException("Evaluation diagnostic output limit exceeded.");
                output.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch { deadline.Cancel(); throw; }
    }
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record ProcessResult(int ExitCode, string Output, string Error);
