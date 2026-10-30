using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class AvifLoaderContracts
{
    internal static void Run(string root, string scenario)
    {
        if (!Path.IsPathFullyQualified(root) || Directory.Exists(root) ||
            scenario is not ("valid" or "missing" or "tampered" or "extra" or "preloaded"))
            throw new ArgumentException("Use a fresh absolute evidence directory and known loader scenario.");
        Directory.CreateDirectory(root);
        const string name = "ContextSuite.Avif.Native.dll";
        var path = Path.Combine(AppContext.BaseDirectory, "avif", name);
        var checks = new List<string>();
        nint foreign = 0;
        try
        {
            if (scenario == "preloaded") foreign = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "foreign", name));
            if (scenario == "valid")
            {
                var encoded = AvifCodec.Encode(1, 1, [40, 130, 210, 123], ColorProfiles.SRGB.ToByteArray(), [], [], default);
                var decoded = HeifCodec.Decode(encoded, default);
                Check(decoded.Info.Width == 1 && decoded.Info.Height == 1 && decoded.Pixels.Length == 8, "Verified encoder and independent decoder work");
                using var process = Process.GetCurrentProcess();
                var module = process.Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));
                Check(string.Equals(module.FileName, path, StringComparison.OrdinalIgnoreCase), "Loaded encoder is the verified adjacent file");
                var refused = false;
                try { using var write = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
                catch (IOException) { refused = true; }
                Check(refused, "Encoder remains protected against writes during its lifetime");
            }
            else
            {
                try { AvifCodec.EnsureAvailable(); throw new InvalidOperationException("Unsafe encoder accepted: " + scenario); }
                catch (ImageFailureException error) { Check(error.Failure == ImageFailure.EngineUnavailable, "Typed engine refusal: " + scenario); }
                using var process = Process.GetCurrentProcess();
                Check(process.Modules.Cast<ProcessModule>().Count(module => module.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase)) ==
                    (scenario == "preloaded" ? 1 : 0), "Refusal loads no new encoder module");
            }
            File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Scenario = scenario, Checks = checks },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Passed {checks.Count} AVIF loader checks: {scenario}.");
        }
        finally { if (foreign != 0) NativeLibrary.Free(foreign); }
        void Check(bool passed, string message)
        { if (!passed) throw new InvalidDataException(message); checks.Add(message); }
    }
}
