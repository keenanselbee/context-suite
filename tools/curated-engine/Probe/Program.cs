using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;

if (args.Length != 2 || !Path.IsPathFullyQualified(args[0]))
    throw new ArgumentException("Expected absolute evidence output and candidate SHA256.");
using var image = new MagickImage(MagickColors.Red, 2, 2);
var formats = new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Bmp, MagickFormat.Tga }
    .Select(format => MagickFormatInfo.Create(format) ?? throw new InvalidOperationException($"Missing {format}"))
    .Select(info => new { format = info.Format.ToString(), info.SupportsReading, info.SupportsWriting }).ToArray();
if (formats.Any(info => !info.SupportsReading || !info.SupportsWriting))
    throw new InvalidOperationException("Required built-in coder unavailable.");
var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
    .Where(module => module.ModuleName.Equals("Magick.Native-Q16-x64.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
if (modules.Length != 1) throw new InvalidOperationException("Expected exactly one loaded native module.");
var path = modules[0].FileName;
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
if (!hash.Equals(args[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Loaded an unexpected native binary.");
var result = new { status = "passed-coder-and-load-probe-not-application-support", nativePath = path, sha256 = hash, delegates = MagickNET.Delegates, formats };
File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified candidate module hash and PNG/JPEG/WebP/BMP/TGA coder availability: {path}");
