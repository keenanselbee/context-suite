using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;

internal static class HeifLoaderContracts
{
    internal static void Run(string root, string fixture, string scenario)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root)) throw new ArgumentException("Use a fresh absolute evidence directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        var directory = Path.Combine(AppContext.BaseDirectory, "heif");
        var names = new[] { "ContextSuite.Heif.Native.dll", "heif.dll", "libde265.dll" };
        var source = File.ReadAllBytes(fixture);
        string? pixelSha256 = null;
        nint foreign = 0;
        try
        {
            if (scenario == "preloaded") foreign = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "foreign", "libde265.dll"));
            if (scenario is "valid" or "local")
            {
                var result = HeifCodec.Decode(source, default);
                pixelSha256 = Convert.ToHexString(SHA256.HashData(result.Pixels));
                Check(result.Info.Abi == 3 && result.Pixels.Length == result.Info.Width * result.Info.Height * 8, "Valid decoder works");
                using var process = Process.GetCurrentProcess();
                foreach (var name in names)
                {
                    var path = Path.Combine(directory, name);
                    var module = process.Modules.Cast<ProcessModule>().Single(module => string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase));
                    Check(string.Equals(module.FileName, path, StringComparison.OrdinalIgnoreCase), "Loaded module has verified path: " + name);
                    var refused = false;
                    try { using var write = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
                    catch (IOException) { refused = true; }
                    Check(refused, "Loaded file cannot be opened for writing: " + name);
                }
                foreach (var path in new[] { directory, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) })
                {
                    // Both paths are this harness's disposable host/engine directories.
                    var moved = path + "-moved";
                    var refused = false;
                    try { Directory.Move(path, moved); Directory.Move(moved, path); }
                    catch (IOException) { refused = true; }
                    Check(refused, "Validated runtime directory cannot be renamed: " + Path.GetFileName(path));
                }
                if (scenario == "local")
                {
                    var refused = false;
                    try { using var write = File.OpenWrite(Path.Combine(directory, LocalLibraryManifest.FileName)); }
                    catch (IOException) { refused = true; }
                    Check(refused, "Local library manifest remains locked during native decode lifetime");
                }
            }
            else
            {
                try { HeifCodec.Decode(source, default); throw new InvalidOperationException("Unsafe engine accepted: " + scenario); }
                catch (ImageFailureException error) { Check(error.Failure == ImageFailure.EngineUnavailable, "Typed engine refusal: " + scenario); }
                using var process = Process.GetCurrentProcess();
                Check(!process.Modules.Cast<ProcessModule>().Any(module => names.Take(scenario == "preloaded" ? 2 : 3)
                    .Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase)), "No new HEIF engine loaded after refusal");
            }
            Check(SHA256.HashData(File.ReadAllBytes(fixture)).SequenceEqual(SHA256.HashData(source)), "Source unchanged");
            File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Scenario = scenario, PixelSha256 = pixelSha256, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Passed {checks.Count} HEIF loader checks: {scenario}.");
        }
        finally { if (foreign != 0) NativeLibrary.Free(foreign); }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); }
    }
}
