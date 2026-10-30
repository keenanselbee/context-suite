using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Private.Images;
using ImageMagick;

if (args is [var documentRoot, "--document-packages"])
{
    var checks = 0;
    OfficePackageOptimizationContracts.RunDocuments(documentRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} OpenDocument and EPUB optimization checks.");
    return 0;
}

if (args is [var tgaOptimizationRoot, "--tga-optimization"])
{
    var checks = 0;
    TgaOptimizationContracts.Run(tgaOptimizationRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} TGA optimization checks.");
    return 0;
}

if (args is [var pdfPresetRoot, "--pdf-presets", var pdfPresetEngine])
{
    var checks = 0;
    await PdfOptimizationPresetContracts.RunAsync(pdfPresetRoot, pdfPresetEngine, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} PDF optimization preset checks.");
    return 0;
}

if (args is [var officeImageRoot, "--office-images"])
{
    var checks = 0;
    OfficePackageOptimizationContracts.RunImages(officeImageRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} Office image optimization checks.");
    return 0;
}

if (args is [var presetImageRoot, "--image-presets"])
{
    var checks = 0;
    ImageOptimizationPresetContracts.Run(presetImageRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} image optimization preset checks.");
    return 0;
}

if (args is ["--office-package-optimization"])
{
    var checks = 0;
    OfficePackageOptimizationContracts.Run((passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} Office package optimization checks.");
    return 0;
}

if (args is [var losslessImageRoot, "--lossless-images"])
{
    var checks = 0;
    LosslessImageOptimizationContracts.Run(losslessImageRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} lossless image optimization checks.");
    return 0;
}

if (args is [var videoRoot, "--video-audio", var videoEngine, var videoFixtures])
{
    var checks = 0;
    await VideoAudioContracts.RunAsync(videoEngine, videoFixtures, videoRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} video audio adapter checks.");
    return 0;
}

if (args is [var gifAdapterRoot, "--gif-adapter"])
{
    var checks = 0;
    GifAdapterContracts.Run(gifAdapterRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} GIF adapter checks.");
    return 0;
}

if (args is [_, "--gif-input"])
{
    var checks = 0;
    GifInputContracts.Run((passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} GIF input checks.");
    return 0;
}

if (args is [var icoInputRoot, "--ico-input"])
{
    var checks = 0;
    IcoInputContracts.Run(icoInputRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} ICO input checks.");
    return 0;
}

if (args is [var jfifRoot, "--jfif"])
{
    var checks = 0;
    JfifContracts.Run(jfifRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} JFIF adapter checks.");
    return 0;
}

if (args is [var svgLoaderRoot, "--svg-loader", var svgLoaderScenario])
{
    await SvgRuntimeContracts.LoaderAsync(svgLoaderRoot, svgLoaderScenario);
    return 0;
}

if (args is [var svgBoundsRoot, "--svg-bounds"])
{
    await SvgRuntimeContracts.BoundsAsync(svgBoundsRoot);
    return 0;
}

if (args is [var svgRoot, "--svg"])
{
    var checks = 0;
    await SvgContracts.RunAsync(svgRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} SVG adapter checks.");
    return 0;
}

if (args is [var avifLoaderRoot, "--avif-loader", var avifLoaderScenario])
{
    AvifLoaderContracts.Run(avifLoaderRoot, avifLoaderScenario);
    return 0;
}

if (args is [var benchmarkRoot, "--avif-benchmark", var benchmarkWidth, var benchmarkHeight, var benchmarkPattern])
{
    AvifBenchmark.Run(benchmarkRoot, uint.Parse(benchmarkWidth, System.Globalization.CultureInfo.InvariantCulture),
        uint.Parse(benchmarkHeight, System.Globalization.CultureInfo.InvariantCulture), benchmarkPattern);
    return 0;
}

if (args is [var avifRoot, "--avif"])
{
    Directory.CreateDirectory(avifRoot);
    var checks = 0;
    AvifContracts.Run(avifRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} AVIF adapter checks.");
    return 0;
}

if (args is [var tiffRoot, "--tiff", var tiffFixtures, var tiffValidator])
{
    Directory.CreateDirectory(tiffRoot);
    var checks = 0;
    await TiffContracts.RunAsync(tiffRoot, tiffFixtures, tiffValidator, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} TIFF adapter checks.");
    return 0;
}

if (args is [var icoRoot, "--ico"])
{
    Directory.CreateDirectory(icoRoot);
    var checks = 0;
    IcoContracts.Run(icoRoot, (passed, message) =>
    { if (!passed) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} ICO adapter checks.");
    return 0;
}

if (args is [var manifestRoot, "--local-libraries"])
{
    LocalLibraryContracts.Run(manifestRoot);
    return 0;
}

if (args is [var colorRoot, "--heif-color", var colorFixtures])
{
    HeifColorContracts.Run(colorRoot, colorFixtures);
    return 0;
}

if (args is [var loaderRoot, "--heif-loader", var loaderFixture, var loaderScenario])
{
    HeifLoaderContracts.Run(loaderRoot, loaderFixture, loaderScenario);
    return 0;
}

if (args.Length >= 3 && args[1] == "--heif-conversion")
{
    var boundaryIndex = Array.IndexOf(args, "--grid-refusals");
    if (boundaryIndex >= 0 && (boundaryIndex < 3 || boundaryIndex != args.Length - 2))
        throw new ArgumentException("Place --grid-refusals and its bridge evidence directory after the fixture directories.");
    HeifConversionContracts.Run(args[0], boundaryIndex < 0 ? args[2..] : args[2..boundaryIndex],
        boundaryIndex < 0 ? null : args[^1]);
    return 0;
}

if (args.Length == 3 && args[1] is "--palette-comparison" or "--palette-refinement" or "--palette-gradients")
{
    await PaletteComparison.RunAsync(args[0], args[2], args[1] == "--palette-refinement", args[1] == "--palette-gradients");
    return 0;
}

if (args.Length == 3 && args[1] == "--optimization-corpus")
{
    await OptimizationCorpus.RunAsync(args[0], args[2]);
    return 0;
}

if (args.Length == 6 && args[1] == "--preset-research")
{
    await QuantizationEvaluation.RunAsync(args[0], args[2] == "-" ? null : args[2], true,
        bool.Parse(args[3]), args[4] == "-" ? null : args[4], args[5] == "-" ? null : args[5]);
    return 0;
}

if (args.Length is 2 or 3 && args[1] is "--quantization-evaluation" or "--preset-research")
{
    await QuantizationEvaluation.RunAsync(args[0], args.Length == 3 ? args[2] : null, args[1] == "--preset-research");
    return 0;
}

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Expected an absolute scratch directory.");
var root = Path.Combine(args[0], "engine-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
ImageEngineRuntime.Initialize(Path.Combine(root, "scratch"));
Console.WriteLine(MagickNET.Version);
Console.WriteLine("Delegates compiled into native binary (not enabled external processes): " + MagickNET.Delegates);
var source = Path.Combine(root, "source.png");
// Independent, authored 3x2 PNG encoder fixture; no image-engine-generated golden input.
byte[] rgba = [255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,255,255, 0,0,0,255, 255,0,255,255];
WritePng(source, rgba);
var original = SHA256.HashData(File.ReadAllBytes(source));
var inputs = new Dictionary<MagickFormat, string> { [MagickFormat.Png] = source };
foreach (var format in new[] { MagickFormat.Jpeg, MagickFormat.WebP })
{
    using var image = new MagickImage(source);
    var input = Path.Combine(root, "source." + format.ToString().ToLowerInvariant());
    image.Quality = 90;
    image.Write(input, format);
    inputs.Add(format, input);
}
var passed = 0;
using (var identity = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContextSuite.Engine.json"))))
{
    var module = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
        .Single(m => m.ModuleName.Equals("Magick.Native-Q16-x64.dll", StringComparison.OrdinalIgnoreCase));
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))) == identity.RootElement.GetProperty("nativeSha256").GetString(),
        "native runtime uses the approved curated DLL, not a stock NuGet fallback");
}
foreach (var input in inputs)
foreach (var target in inputs.Keys.Where(format => format != input.Key))
{
    var output = Path.Combine(root, input.Key + "-to-" + target + ".output");
    using (var image = new MagickImage(input.Value))
    {
        image.Quality = 90;
        image.Write(output, target);
    }
    using var reopened = new MagickImage(output);
    var bytes = File.ReadAllBytes(output);
    var signature = target switch
    {
        MagickFormat.Png => bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }),
        MagickFormat.Jpeg => bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        MagickFormat.WebP => Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        _ => false,
    };
    Check(signature && reopened.Format == target && reopened.Width == 3 && reopened.Height == 2,
        $"native {input.Key} to {target}: signature, decode, dimensions");
}
using (var image = new MagickImage(source))
    Check(image.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(rgba), "independent PNG fixture has exact expected pixels");
Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original), "source remains unchanged");
foreach (var pseudo in new[] { "xc:red", "logo:", "https://example.invalid/not-an-image" })
{
    var rejected = false;
    try { using var image = new MagickImage(pseudo); }
    catch (MagickException) { rejected = true; }
    Check(rejected, "restricted policy rejects " + pseudo);
}
AdapterContracts.Run(root, inputs, Check);
ImageMatrixContracts.Run(root, Check);
BitmapContracts.Run(root, Check);
await PngOptimizationContracts.RunAsync(root, Check);
await PngPrecisionContracts.RunAsync(root, Check);
await PngPaletteContracts.RunAsync(Check);
Check(ResourceLimits.Memory is > 0 and <= 512UL * 1024 * 1024 && ResourceLimits.Disk is > 0 and <= 1024UL * 1024 * 1024,
    "native resources: finite memory and disk ceilings are active");
Check(ResourceLimits.Thread is > 0 and <= 2 && ResourceLimits.Width <= 16384 && ResourceLimits.Height <= 16384 && ResourceLimits.ListLength <= 2,
    "native resources: thread, dimension and frame ceilings are active");
Check(ResourceLimits.MaxProfileSize == 4 * 1024 * 1024, "native resources: profile ceiling is active");
var memoryLimit = ResourceLimits.Memory;
var diskLimit = ResourceLimits.Disk;
try
{
    // Test-only deterministic exhaustion, after the ordinary production-policy matrix.
    ResourceLimits.Memory = 1;
    ResourceLimits.Disk = 1;
    var limited = false;
    try { using var image = new MagickImage(source); }
    catch (MagickException) { limited = true; }
    Check(limited && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(original),
        "native resources: exhausted cache budget fails without changing source");
}
finally { ResourceLimits.Memory = memoryLimit; ResourceLimits.Disk = diskLimit; }
using (var recovered = new MagickImage(source))
    Check(recovered.Width == 3 && recovered.Height == 2, "native resources: later decode works after isolated resource fault");
File.WriteAllText(Path.Combine(root, "result.txt"), $"Passed {passed} engine/adapter contracts.\n{MagickNET.Version}\n");
Console.WriteLine($"Passed {passed} engine/adapter contracts. Evidence: {root}");
return 0;

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}

static void WritePng(string path, byte[] rgba)
{
    using var stream = File.Create(path);
    stream.Write(new byte[] { 137,80,78,71,13,10,26,10 });
    byte[] header = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(header, 3);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 2);
    header[8] = 8; header[9] = 6;
    Chunk(stream, "IHDR", header);
    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        for (var y = 0; y < 2; y++) { zlib.WriteByte(0); zlib.Write(rgba, y * 12, 12); }
    Chunk(stream, "IDAT", compressed.ToArray());
    Chunk(stream, "IEND", []);
}

static void Chunk(Stream stream, string name, byte[] data)
{
    var type = Encoding.ASCII.GetBytes(name);
    Span<byte> value = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
    stream.Write(value); stream.Write(type); stream.Write(data);
    uint crc = uint.MaxValue;
    foreach (var item in type.Concat(data))
    {
        crc ^= item;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
    }
    BinaryPrimitives.WriteUInt32BigEndian(value, ~crc);
    stream.Write(value);
}
