using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

internal static class WmaVoiceAvailabilityContracts
{
    public static async Task RunAsync(string helperDirectory, string evidence)
    {
        helperDirectory = Path.GetFullPath(helperDirectory); evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Evidence directory already exists.");
        Directory.CreateDirectory(evidence);
        var directory = Path.Combine(evidence, "native-empty"); Directory.CreateDirectory(directory);
        var source = Path.Combine(evidence, "empty-input.tmp");
        await File.WriteAllBytesAsync(source, []);
        var checks = new List<string>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var decoder = new WindowsVoiceDecoder(helperDirectory))
        {
            await decoder.EnsureAvailableAsync(input, directory, deadline.Token);
            if (input.Length != 0 || input.Position != 0) throw new Exception("Availability touched its empty input.");
            checks.Add("real pinned Windows helper: availability without media decoding or input mutation");
            // A cached success needs neither an input handle nor a working directory.
            input.Dispose();
            await decoder.EnsureAvailableAsync(input, Path.Combine(evidence, "absent"), deadline.Token);
            checks.Add("successful availability cached for this decoder instance");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await decoder.EnsureAvailableAsync(input, directory, cancelled.Token); throw new Exception("Cached query ignored cancellation."); }
            catch (OperationCanceledException) { checks.Add("cached availability respects cancellation"); }
            decoder.Dispose();
            try { await decoder.EnsureAvailableAsync(input, directory, deadline.Token); throw new Exception("Disposed decoder used cached availability."); }
            catch (ObjectDisposedException) { checks.Add("disposed availability query rejected"); }
        }
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var decoder = new WindowsVoiceDecoder(helperDirectory))
        {
            try { await decoder.EnsureAvailableAsync(input, Path.Combine(evidence, "absent"), deadline.Token); throw new Exception("Absent working directory accepted."); }
            catch (System.ComponentModel.Win32Exception) { checks.Add("failed availability launch not cached"); }
            await decoder.EnsureAvailableAsync(input, directory, deadline.Token);
            checks.Add("availability recovers on same instance after launch failure");
        }
        // These child exit codes are injected classification checks, not proof
        // of behavior on a Windows installation with missing media components.
        var fixture = Path.Combine(evidence, "exit-input.tmp");
        await File.WriteAllBytesAsync(fixture, [17, 33, 255]);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host.");
        async Task<byte[]> Exit(int code, Func<int, Exception>? failure)
        {
            using var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await SeekableMediaProcess.RunAsync(executable, directory, input,
                ["--process-fixture", "exit", code.ToString(CultureInfo.InvariantCulture)], 64, deadline.Token, failure);
        }
        foreach (var code in new[] { 3, unchecked((int)0xc0000135), unchecked((int)0xc0000139), unchecked((int)0xc0000142) })
        {
            var called = false;
            try
            {
                await Exit(code, actual =>
                {
                    if (actual != code) throw new Exception("Child exit status changed.");
                    called = true; return WindowsVoiceDecoder.ExitFailure(actual);
                });
                throw new Exception("Unavailable provider exit admitted.");
            }
            catch (NotSupportedException error) when (called && error.Message.Contains("Check Windows media components", StringComparison.Ordinal))
            { checks.Add($"injected child exit 0x{unchecked((uint)code):X8}: actionable provider-unavailable error"); }
        }
        try { await Exit(1, WindowsVoiceDecoder.ExitFailure); throw new Exception("Decoder failure admitted."); }
        catch (InvalidDataException error) when (error.Message.StartsWith("WMA Voice processing failed", StringComparison.Ordinal))
        { checks.Add("ordinary decoder failure remains distinct from missing provider"); }
        try { await Exit(3, null); throw new Exception("Default launcher admitted failure."); }
        catch (InvalidDataException) { checks.Add("unmapped launcher keeps its default failure behavior"); }
        await Exit(0, _ => throw new Exception("Success invoked exit failure mapping."));
        checks.Add("successful child bypasses failure mapping");
        using (var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new MemoryStream())
        {
            try
            {
                await SeekableMediaProcess.RunAsync(executable, directory, input, ["--process-fixture", "exit", "3"],
                    (stream, token) => WindowsVoiceDecoder.CaptureWaveAsync(stream, output, 8000, 32, token),
                    deadline.Token, WindowsVoiceDecoder.ExitFailure);
                throw new Exception("Empty streaming startup failure admitted.");
            }
            catch (NotSupportedException)
            {
                if (output.Length != 0) throw new Exception("Startup failure wrote a transport header.");
                checks.Add("empty streaming output preserves actionable startup failure classification");
            }
        }
        using (var input = new FileStream(fixture, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                await SeekableMediaProcess.RunAsync(executable, directory, input, ["--process-fixture", "flood", "unused"], 16,
                    deadline.Token, _ => throw new Exception("Exit mapping replaced output budget failure."));
                throw new Exception("Output budget ignored.");
            }
            catch (InvalidDataException) { checks.Add("output budget failure remains authoritative with exit mapping"); }
        }
        await ProcessContracts.RunAsync(evidence, checks);
        if (Directory.EnumerateFileSystemEntries(directory).Any() || new FileInfo(source).Length != 0 ||
            !(await File.ReadAllBytesAsync(fixture)).SequenceEqual(new byte[] { 17, 33, 255 }))
            throw new Exception("Availability or exit tests changed inputs or created native artifacts.");
        checks.Add("empty native directory and original input bytes preserved");
        var helper = Path.Combine(helperDirectory, "WmaVoiceMftProbe.exe");
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow, checks, helperSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper))),
            scope = "Real Windows availability and injected child exit classification. Missing Windows media components remain untested."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Windows Voice availability/launcher checks. Evidence: {evidence}");
    }
}
