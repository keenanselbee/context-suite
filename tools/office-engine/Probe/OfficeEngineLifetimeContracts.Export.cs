using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class OfficeEngineLifetimeContracts
{
    private sealed record PartialPdf(string Path, long PreviousBytes, long Bytes, string PrefixSha256);
    private sealed record ExportObservation(Identity Launcher, Identity Engine, PartialPdf Pdf);

    internal static async Task ExportAsync(string prepared, string qpdf, string root)
    {
        prepared = Path.GetFullPath(prepared); root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Use fresh export-lifetime evidence.");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(prepared, "unpacked", "program", "soffice.com");
        var scratch = Path.GetDirectoryName(Path.GetDirectoryName(prepared)!)!;
        var fixtures = Path.Combine(root, "fixtures"); Directory.CreateDirectory(fixtures);
        OfficeFixtures.Create(fixtures); var document = OfficeExportFixture.Create(fixtures);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(document))); var modified = File.GetLastWriteTimeUtc(document);
        var reports = new List<object>();
        foreach (var mode in new[] { "control", "cancel", "owner-crash" })
        {
            var folder = Path.Combine(root, mode); Directory.CreateDirectory(folder);
            var profile = Path.Combine(scratch, "office-profile-" + Guid.NewGuid().ToString("N") + "e");
            var settings = Path.Combine(profile, "user", "registrymodifications.xcu"); Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            var environment = EnvironmentFor(profile); OfficeProfileSettings.Apply(settings);
            await OfficeEvaluationProcess.RunAsync(executable, Arguments(profile).Append("--terminate_after_init"), folder, environment);
            OfficeProfileSettings.Apply(settings);
            await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(new { Profile = profile }));
            if (mode == "control")
            {
                var result = await OfficeEvaluationProcess.RunAsync(executable, ExportArguments(profile, folder, document), folder, environment);
                var pdf = Path.Combine(folder, "Export interruption.pdf"); await CheckPdf(qpdf, pdf, OfficeExportFixture.Pages, folder);
                if (new FileInfo(pdf).Length < 32 * 1024 * 1024) throw new InvalidDataException("Control PDF lacks the expected large image payload.");
                reports.Add(new { Mode = mode, result, PdfBytes = new FileInfo(pdf).Length });
            }
            else
            {
                if (mode == "cancel") await CancelExport(executable, profile, folder, document, environment);
                else await CrashExport(executable, profile, folder, document);
                using (File.Open(document, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                using (File.Open(settings, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                OfficeProfileSettings.Apply(settings);
                var recoveryFolder = Path.Combine(folder, "recovery"); Directory.CreateDirectory(recoveryFolder);
                var recovery = await OfficeEvaluationProcess.RunAsync(executable, ExportArguments(profile, recoveryFolder, Path.Combine(fixtures, "Word \u00fc.docx")),
                    recoveryFolder, environment);
                await CheckPdf(qpdf, Path.Combine(recoveryFolder, "Word \u00fc.pdf"), 2, recoveryFolder);
                var stopped = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(folder, "stopped.json")));
                reports.Add(new { Mode = mode, Profile = profile, Stop = stopped, Recovery = recovery });
            }
            OfficeProfileSettings.Verify(settings);
            if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(document))) != hash || File.GetLastWriteTimeUtc(document) != modified)
                throw new IOException("Passive export source changed.");
            await File.WriteAllTextAsync(Path.Combine(root, "export-lifetime.json"), JsonSerializer.Serialize(new { SourceSha256 = hash, Reports = reports }));
            Console.WriteLine("PASS: actual Office PDF export " + mode + ", source preservation and independent PDF check.");
        }
    }

    private static async Task CancelExport(string executable, string profile, string folder, string document, IReadOnlyDictionary<string, string> environment)
    {
        using var cancellation = new CancellationTokenSource(); Process[] retained = []; ExportObservation? observation = null;
        var cleanup = new Stopwatch();
        try
        {
            try
            {
                await OfficeEvaluationProcess.RunAsync(executable, ExportArguments(profile, folder, document), folder, environment, cancellation.Token,
                    observe: async (pid, belongs, token) =>
                    {
                        retained = await Observe(pid, executable, belongs, token);
                        var partial = await GrowingPdf(folder, profile, document, retained, () => Observe(pid, executable, belongs, token), token);
                        observation = new(Identify(retained[0]), Identify(retained[1]), partial);
                        cleanup.Start(); cancellation.Cancel();
                    });
                throw new Exception("Export cancellation returned success.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (observation is null) throw new IOException("Cancellation lacked a growing PDF observation.");
            foreach (var process in retained) await Exited(process);
            await File.WriteAllTextAsync(Path.Combine(folder, "stopped.json"), JsonSerializer.Serialize(new
            { Observation = observation, CleanupMilliseconds = cleanup.ElapsedMilliseconds, BothHandlesSignaled = retained.All(process => process.HasExited) }));
        }
        finally { foreach (var process in retained) process.Dispose(); }
    }

    private static async Task CrashExport(string executable, string profile, string folder, string document)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--engine-export-owner", executable, profile, folder, document }) start.ArgumentList.Add(argument);
        using var owner = Process.Start(start) ?? throw new IOException("Cannot start disposable export owner."); Process[] retained = [];
        try
        {
            var ready = Path.Combine(folder, "ready.json"); var timer = Stopwatch.StartNew();
            while (!File.Exists(ready) && !owner.HasExited && timer.Elapsed < TimeSpan.FromSeconds(55)) await Task.Delay(5);
            var observation = JsonSerializer.Deserialize<ExportObservation>(await File.ReadAllTextAsync(ready))!;
            retained = [Open(observation.Launcher, executable)];
            retained = [retained[0], Open(observation.Engine, Path.Combine(Path.GetDirectoryName(executable)!, "soffice.bin"))];
            if (ReadPartial(observation.Pdf.Path) is null) throw new IOException("PDF completed or disappeared before owner interruption.");
            var identity = Identify(owner); timer.Restart(); owner.Kill(); await Exited(owner);
            foreach (var process in retained) await Exited(process);
            await File.WriteAllTextAsync(Path.Combine(folder, "stopped.json"), JsonSerializer.Serialize(new
            { Owner = identity, Observation = observation, CleanupMilliseconds = timer.ElapsedMilliseconds, BothHandlesSignaled = retained.All(process => process.HasExited) }));
        }
        finally
        {
            if (!owner.HasExited) owner.Kill(); await Exited(owner);
            foreach (var process in retained) process.Dispose();
        }
    }

    internal static async Task ExportOwnerAsync(string executable, string profile, string folder, string document)
    {
        Process[] retained = [];
        try
        {
            await OfficeEvaluationProcess.RunAsync(executable, ExportArguments(profile, folder, document), folder, EnvironmentFor(profile),
                observe: async (pid, belongs, token) =>
                {
                    retained = await Observe(pid, executable, belongs, token); var partial = await GrowingPdf(folder, profile, document, retained, () => Observe(pid, executable, belongs, token), token);
                    var observation = new ExportObservation(Identify(retained[0]), Identify(retained[1]), partial);
                    var pending = Path.Combine(folder, "ready.pending"); await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(observation), token);
                    File.Move(pending, Path.Combine(folder, "ready.json"));
                });
            throw new IOException("Export owner completed before interruption.");
        }
        finally { foreach (var process in retained) process.Dispose(); }
    }

    private static async Task<PartialPdf> GrowingPdf(string folder, string profile, string document, Process[] processes, Func<Task<Process[]>> observe, CancellationToken token)
    {
        var previous = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var observations = new Dictionary<string, (long First, long Last, bool ReadablePdf)>(StringComparer.OrdinalIgnoreCase);
        var started = DateTime.UtcNow.AddSeconds(-1);
        var startupExits = new List<object>();
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (processes[0].HasExited) throw new IOException("Launcher exited before a growing PDF was observed.");
                if (processes[1].HasExited)
                {
                    startupExits.Add(new { processes[1].Id, processes[1].ExitCode });
                    if (startupExits.Count > 4) throw new IOException("Too many engine restarts during export observation.");
                    var replacement = await observe();
                    if (replacement[1].Id == processes[1].Id) { foreach (var process in replacement) process.Dispose(); throw new IOException("No live replacement engine."); }
                    foreach (var process in processes) process.Dispose();
                    replacement.CopyTo(processes, 0);
                }
                foreach (var directory in new[] { folder, profile, Path.GetDirectoryName(document)! })
                {
                    string[] files;
                    try { files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(16385).ToArray(); }
                    catch (DirectoryNotFoundException) { continue; }
                    if (files.Length > 16384) throw new IOException("Owned export observation exceeds its file-count limit.");
                    foreach (var path in files)
                    {
                        try
                        {
                            var file = new FileInfo(path); if (!file.Exists || file.LastWriteTimeUtc < started && file.CreationTimeUtc < started) continue;
                            var partial = ReadPartial(path); var bytes = file.Length;
                            observations[path] = (observations.TryGetValue(path, out var seen) ? seen.First : bytes, bytes, partial is not null);
                            if (partial is null) continue;
                            if (previous.TryGetValue(path, out var length) && partial.Bytes > length) return partial with { PreviousBytes = length };
                            previous[path] = partial.Bytes;
                        }
                        catch (FileNotFoundException) { }
                    }
                }
                await Task.Delay(5, token);
            }
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "observed-files.json"), JsonSerializer.Serialize(observations.Select(pair =>
                new { Path = pair.Key, FirstBytes = pair.Value.First, LastBytes = pair.Value.Last, pair.Value.ReadablePdf }).ToArray()));
            await File.WriteAllTextAsync(Path.Combine(folder, "observation-processes.json"), JsonSerializer.Serialize(processes.Select(process =>
                new { process.Id, process.HasExited, ExitCode = process.HasExited ? process.ExitCode : (int?)null }).ToArray()));
            await File.WriteAllTextAsync(Path.Combine(folder, "startup-exits.json"), JsonSerializer.Serialize(startupExits));
        }
    }

    private static PartialPdf? ReadPartial(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = file.Length; if (length < 65536) return null;
            var prefix = new byte[64]; file.ReadExactly(prefix); if (!prefix.AsSpan().StartsWith("%PDF-"u8)) return null;
            file.Position = length - 256; var tail = new byte[256]; file.ReadExactly(tail);
            if (tail.AsSpan().IndexOf("%%EOF"u8) >= 0) return null;
            return new(path, 0, length, Convert.ToHexString(SHA256.HashData(prefix)));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static IEnumerable<string> ExportArguments(string profile, string folder, string document) => Arguments(profile).Concat(new[]
    {
        "--convert-to", "pdf:writer_pdf_Export:{\"UseLosslessCompression\":{\"type\":\"boolean\",\"value\":\"true\"},\"ReduceImageResolution\":{\"type\":\"boolean\",\"value\":\"false\"}}",
        "--outdir", folder, document
    });

    private static async Task CheckPdf(string qpdf, string pdf, int pages, string folder)
    {
        var checkedPdf = await OfficeEvaluationProcess.RunAsync(qpdf, ["--check", pdf], folder);
        var count = await OfficeEvaluationProcess.RunAsync(qpdf, ["--show-npages", pdf], folder);
        if (count.Output.Trim() != pages.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new InvalidDataException("Export page count differs.");
        await File.WriteAllTextAsync(Path.Combine(folder, "pdf-check.json"), JsonSerializer.Serialize(new { Pdf = pdf, Pages = pages, Check = checkedPdf, Count = count }));
    }
}
