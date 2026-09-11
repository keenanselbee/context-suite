using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

if (args.Length is not (3 or 4)) throw new ArgumentException("Expected probe, generated qpdf fixture directory, new output directory and optional generated optimized candidate.");
var executable = Path.GetFullPath(args[0]);
var fixtures = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var checks = new List<object>();
var observations = new List<object>();
var inputs = Directory.GetFiles(fixtures, "*.pdf").ToDictionary(path => path, Hash);
if (args.Length == 4) inputs.Add(Path.GetFullPath(args[3]), Hash(args[3]));
var failures = 0;
void Check(string name, bool passed) { checks.Add(new { name, passed }); if (!passed) failures++; }
string FileAt(string name) => Path.Combine(fixtures, name);
string ResultAt(string name) => Path.Combine(output, name);

async Task<(int Exit, JsonElement Json)> Run(params string[] arguments)
{
    using var process = new Process { StartInfo = new(executable) { UseShellExecute = false,
        CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = output } };
    foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var watch = Stopwatch.StartNew();
    process.Start();
    async Task<string> Read(StreamReader reader)
    {
        var buffer = new char[65537];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count), deadline.Token);
            if (read == 0) return new string(buffer, 0, count);
            count += read;
        }
        throw new InvalidDataException("Evaluation diagnostic limit exceeded.");
    }
    var stdout = Read(process.StandardOutput);
    var stderr = Read(process.StandardError);
    // Kill promptly if either bounded reader fails, including on timeout.
    async Task Guard(Task<string> read)
    {
        try { await read; }
        catch { if (!process.HasExited) process.Kill(true); throw; }
    }
    try
    {
        await Task.WhenAll(process.WaitForExitAsync(deadline.Token), Guard(stdout), Guard(stderr));
        var diagnostic = await stderr;
        observations.Add(new { mode = arguments[0], input = Path.GetFileName(arguments[1]),
            process.ExitCode, elapsedMs = watch.ElapsedMilliseconds, diagnostic });
        using var json = JsonDocument.Parse(await stdout);
        return (process.ExitCode, json.RootElement.Clone());
    }
    catch
    {
        if (!process.HasExited) process.Kill(true);
        await process.WaitForExitAsync();
        throw;
    }
}

async Task<JsonElement> Render(string path, string name, int dpi = 150, string backing = "white", string widgets = "widgets")
{
    var result = await Run("render", path, ResultAt(name), dpi.ToString(), backing, widgets);
    if (result.Exit != 0) throw new InvalidDataException("Rendering failed.");
    var pages = result.Json.GetProperty("pages");
    for (var index = 0; index < pages.GetArrayLength(); index++)
    {
        var page = pages[index];
        var bytes = File.ReadAllBytes(ResultAt($"{name}-{index + 1}.bgra"));
        var bitmap = BitmapSource.Create(page.GetProperty("width").GetInt32(), page.GetProperty("height").GetInt32(),
            dpi, dpi, PixelFormats.Bgra32, null, bytes, page.GetProperty("stride").GetInt32());
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(ResultAt($"{name}-{index + 1}.png"));
        encoder.Save(stream);
    }
    observations.Add(new { render = name, pages });
    return pages;
}

var source = await Render(FileAt("authored original ü.pdf"), "source");
var optimized = await Render(FileAt("optimized copy ü.pdf"), "optimized");
Check("Two rendered pages retain geometry", source.GetArrayLength() == 2 && source.ToString() == optimized.ToString());
for (var page = 1; page <= 2; page++)
    Check($"Optimized page {page} has identical rendered pixels", Hash(ResultAt($"source-{page}.bgra")) == Hash(ResultAt($"optimized-{page}.bgra")));
if (args.Length == 4)
{
    var candidate = await Render(args[3], "candidate");
    Check("Adapter candidate retains page geometry", source.ToString() == candidate.ToString());
    for (var page = 1; page <= source.GetArrayLength(); page++)
        Check($"Adapter candidate page {page} retains rendered pixels", Hash(ResultAt($"source-{page}.bgra")) == Hash(ResultAt($"candidate-{page}.bgra")));
}
await Render(FileAt("authored original ü.pdf"), "without-widgets", widgets: "none");
Check("Form widget drawing changes visible pixels", Hash(ResultAt("source-1.bgra")) != Hash(ResultAt("without-widgets-1.bgra")));
await Render(FileAt("authored original ü.pdf"), "transparent", backing: "transparent");
Check("Transparent backing preserves unpainted page alpha", File.ReadAllBytes(ResultAt("transparent-1.bgra"))
    .Where((_, index) => index % 4 == 3).Any(alpha => alpha == 0));
foreach (var name in new[] { "signature-field-canary.pdf", "unattached-signature-canary.pdf", "encrypted.pdf", "owner-protected.pdf", "broken-xref.pdf" })
{
    var result = await Run("inspect", FileAt(name));
    observations.Add(new { inspect = name, result.Exit, facts = result.Json });
    if (name == "encrypted.pdf") Check("Password-required PDF is not opened", result.Exit == 2 && result.Json.GetProperty("error").GetInt32() == 4);
    if (name == "owner-protected.pdf") Check("Owner-protected PDF can be inspected without altering it", result.Exit == 0);
}

var pixels = new byte[64 * 48 * 4];
for (var y = 0; y < 48; y++)
for (var x = 0; x < 64; x++)
{
    var offset = (y * 64 + x) * 4;
    pixels[offset] = (byte)(y * 5); pixels[offset + 1] = (byte)(x * 3); pixels[offset + 2] = (byte)(255 - y * 4);
    pixels[offset + 3] = new byte[] { 0, 64, 128, 255 }[x / 16];
}
File.WriteAllBytes(ResultAt("image.bgra"), pixels);
var created = await Run("image-pdf", ResultAt("image.bgra"), ResultAt("image.pdf"));
Check("Authored BGRA image can be saved as PDF", created.Exit == 0);
await Render(ResultAt("image.pdf"), "image-transparent", 72, "transparent");
await Render(ResultAt("image.pdf"), "image-white", 72);
var roundTrip = File.ReadAllBytes(ResultAt("image-transparent-1.bgra"));
var white = File.ReadAllBytes(ResultAt("image-white-1.bgra"));
var maximumColorError = 0; var maximumAlphaError = 0; var maximumCompositeError = 0;
for (var index = 0; index < pixels.Length; index += 4)
{
    maximumAlphaError = Math.Max(maximumAlphaError, Math.Abs(pixels[index + 3] - roundTrip[index + 3]));
    for (var channel = 0; channel < 3; channel++)
    {
        if (pixels[index + 3] != 0) maximumColorError = Math.Max(maximumColorError, Math.Abs(pixels[index + channel] - roundTrip[index + channel]));
        var expected = (pixels[index + channel] * pixels[index + 3] + 255 * (255 - pixels[index + 3])) / 255;
        maximumCompositeError = Math.Max(maximumCompositeError, Math.Abs(expected - white[index + channel]));
    }
}
observations.Add(new { maximumAlphaError, maximumColorError, maximumCompositeError });
Check("Image alpha round trip is exact", maximumAlphaError == 0);
Check("Visible image channels round trip within one level", maximumColorError <= 1);
Check("White compositing agrees within one level", maximumCompositeError <= 1);
Check("All generated qpdf inputs remain unchanged", inputs.All(pair => pair.Value == Hash(pair.Key)));
File.WriteAllText(ResultAt("report.json"), JsonSerializer.Serialize(new { checks, observations, inputs,
    limitation = "Generated evaluation fixtures only; no production sandbox, font/ICC/signature or broad fidelity acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PDFium evaluation: {checks.Count - failures}/{checks.Count} checks passed. Evidence: {output}");
return failures == 0 ? 0 : 1;

static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
