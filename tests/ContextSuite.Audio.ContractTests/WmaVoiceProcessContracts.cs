using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// Test-only Windows Voice candidate; this does not change provider admission.
internal static class WmaVoiceProcessContracts
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Process FindHelper(string executable)
    {
        var matches = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                { matches.Add(process); continue; }
            }
            catch (InvalidOperationException) { }
            process.Dispose();
        }
        if (matches.Count == 1) return matches[0];
        foreach (var process in matches) process.Dispose();
        throw new InvalidOperationException("Expected one live evaluation helper with the exact owned path.");
    }

    public static async Task OwnerAsync(string executable, string source, string directory)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await SeekableMediaProcess.RunAsync(executable, directory, input, ["--inherited"], async (stream, token) =>
        {
            var first = new byte[28]; await stream.ReadExactlyAsync(first, token);
            using var helper = FindHelper(executable);
            Console.WriteLine(helper.Id); Console.Out.Flush();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, deadline.Token);
    }

    public static async Task RunAsync(string executable, string corpus, string refusals, string evidence)
    {
        executable = Path.GetFullPath(executable); corpus = Path.GetFullPath(corpus);
        refusals = Path.GetFullPath(refusals); evidence = Path.GetFullPath(evidence);
        if (Directory.Exists(evidence)) throw new IOException("Evidence directory already exists.");
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "identity.json")));
        var expectedHash = identity.RootElement.GetProperty("executableSha256").GetString();
        using var helperLease = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Hash(executable) != expectedHash) throw new InvalidDataException("Evaluation helper identity changed.");
        Directory.CreateDirectory(evidence);
        var directory = Path.Combine(evidence, "native-empty"); Directory.CreateDirectory(directory);
        var checks = new List<string>(); var preserved = new Dictionary<string, string>();
        var source = Path.Combine(evidence, "read-only snapshot \u00fc.wma");
        using var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus, "results.json")));
        var voices = rows.RootElement.EnumerateArray().Where(row => row.GetProperty("codec").GetString() == "voice").ToArray();
        if (voices.Length != 36) throw new InvalidDataException("Expected complete Voice corpus.");
        async Task<byte[]> Decode(int limit = 1024 * 1024 + 24, CancellationToken token = default)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await SeekableMediaProcess.RunAsync(executable, directory, input, ["--inherited"], limit, token);
        }
        using var totalDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        foreach (var row in voices)
        {
            var name = row.GetProperty("name").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid fixture name.");
            var original = Path.Combine(corpus, name, "fixture.wma");
            var digest = row.GetProperty("wmaSha256").GetString()!;
            if (Hash(original) != digest) throw new InvalidDataException("Fixture identity changed.");
            preserved.Add(original, digest);
            File.Copy(original, source, true);
            var actual = await Decode(token: totalDeadline.Token);
            var rate = int.Parse(name.Split('-')[1], System.Globalization.CultureInfo.InvariantCulture);
            if (actual.Length < 28 || !actual.AsSpan(0, 8).SequenceEqual("CSVOICE1"u8) ||
                BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(8)) != rate ||
                BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(12)) != 1 ||
                BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(16)) != 32 ||
                BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(20)) != 16)
                throw new InvalidDataException("Incorrect sample transport header.");
            var reference = File.ReadAllBytes(Path.Combine(corpus, name, "windows.f32"));
            if (!actual.AsSpan(24).SequenceEqual(reference) || actual.Length - 24 != row.GetProperty("sourceFrames").GetInt32() * 4)
                throw new InvalidDataException("Inherited decoding changed reference samples or extent.");
            if (Hash(source) != digest) throw new InvalidDataException("Snapshot changed.");
            checks.Add("inherited snapshot/header/exact reference: " + name);
        }
        using var negativeRows = JsonDocument.Parse(File.ReadAllText(Path.Combine(refusals, "results.json")));
        var negatives = negativeRows.RootElement.EnumerateArray()
            .Where(row => !row.GetProperty("name").GetString()!.EndsWith("-positive.wma", StringComparison.Ordinal)).ToArray();
        if (negatives.Length != 8) throw new InvalidDataException("Expected eight malformed fixtures.");
        foreach (var row in negatives)
        {
            var name = row.GetProperty("name").GetString()!;
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Invalid refusal fixture name.");
            var original = Path.Combine(refusals, name); var digest = row.GetProperty("sha256").GetString()!;
            if (Hash(original) != digest) throw new InvalidDataException("Malformed fixture identity changed.");
            preserved.Add(original, digest); File.Copy(original, source, true);
            try { await Decode(token: totalDeadline.Token); throw new Exception("Malformed Voice accepted."); }
            catch (InvalidDataException) { checks.Add("inherited malformed refusal: " + name); }
            if (Hash(source) != digest) throw new InvalidDataException("Refused snapshot changed.");
        }
        File.Copy(Path.Combine(corpus, "voice-22050-1-16-22050", "fixture.wma"), source, true);
        var controlHash = Hash(source);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await Decode(token: cancelled.Token); throw new Exception("Pre-cancellation ignored."); }
            catch (OperationCanceledException) { checks.Add("pre-cancellation"); }
        }
        try { await Decode(24, totalDeadline.Token); throw new Exception("Output limit ignored."); }
        catch (InvalidDataException) { checks.Add("sample-output limit terminates child"); }
        foreach (var timeout in new[] { false, true })
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = SeekableMediaProcess.RunAsync(executable, directory, input, ["--inherited"], async (stream, token) =>
            {
                await stream.ReadExactlyAsync(new byte[28], token);
                ready.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }, cancel.Token);
            await ready.Task.WaitAsync(cancel.Token);
            using var child = FindHelper(executable);
            if (child.HasExited) throw new Exception("Helper stopped before interruption.");
            if (timeout) cancel.CancelAfter(TimeSpan.FromMilliseconds(100)); else cancel.Cancel();
            try { await running; throw new Exception("Active interruption ignored."); }
            catch (OperationCanceledException)
            {
                if (!child.HasExited) throw new Exception("Helper survived cancellation completion.");
                checks.Add(timeout ? "deadline after decoded samples awaits helper exit" : "cancellation after decoded samples awaits helper exit");
            }
        }
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = evidence };
        foreach (var argument in new[] { "--wma-voice-owner", executable, source, directory }) start.ArgumentList.Add(argument);
        using (var owner = Process.Start(start) ?? throw new IOException("Cannot start disposable owner."))
        {
            var errors = owner.StandardError.ReadToEndAsync();
            Process? child = null;
            try
            {
                var line = await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
                if (!int.TryParse(line, out var pid)) throw new Exception("Owner did not observe decoded output: " + await errors);
                child = Process.GetProcessById(pid);
                if (child.HasExited || !string.Equals(child.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Unexpected owner child.");
                owner.Kill(); // Deliberately kill only the owner: the job must stop its helper.
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                checks.Add("abrupt owner death closes job and terminates active helper");
            }
            finally
            {
                if (!owner.HasExited) owner.Kill(true);
                await owner.WaitForExitAsync();
                if (child is not null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
                await File.WriteAllTextAsync(Path.Combine(evidence, "owner-stderr.txt"), await errors);
            }
        }
        var recovered = await Decode(token: totalDeadline.Token);
        if (!recovered.AsSpan(24).SequenceEqual(File.ReadAllBytes(Path.Combine(corpus, "voice-22050-1-16-22050", "windows.f32"))))
            throw new Exception("Recovery changed decoded samples.");
        checks.Add("fresh invocation recovers exact samples after all interruptions");
        if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new Exception("Handle mode created native files.");
        checks.Add("native working directory remains empty");
        if (Hash(executable) != expectedHash || Hash(source) != controlHash || preserved.Any(pair => Hash(pair.Key) != pair.Value))
            throw new Exception("Helper or sources changed.");
        checks.Add("helper, originals and snapshot hashes preserved");
        await File.WriteAllTextAsync(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { helperSha256 = expectedHash, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Windows Voice process checks. Evidence: {evidence}");
    }
}
