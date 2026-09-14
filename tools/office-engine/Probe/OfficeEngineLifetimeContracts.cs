using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

internal static partial class OfficeEngineLifetimeContracts
{
    private sealed record Identity(int Id, long Created, string Path);
    private sealed record Observation(Identity Launcher, Identity Engine, int ControlMilliseconds);

    internal static async Task RunAsync(string prepared, string root)
    {
        prepared = Path.GetFullPath(prepared); root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Use fresh engine-lifetime evidence.");
        Directory.CreateDirectory(root);
        var executable = Path.Combine(prepared, "unpacked", "program", "soffice.com");
        var scratch = Path.GetDirectoryName(Path.GetDirectoryName(prepared)!)!;
        var reports = new List<object>();
        foreach (var mode in new[] { "cancel", "owner-crash" })
        {
            var folder = Path.Combine(root, mode); Directory.CreateDirectory(folder);
            var profile = Path.Combine(scratch, "office-profile-" + Guid.NewGuid().ToString("N") + "l");
            var settings = Path.Combine(profile, "user", "registrymodifications.xcu");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            var environment = EnvironmentFor(profile);
            OfficeProfileSettings.Apply(settings);
            var initialized = await OfficeEvaluationProcess.RunAsync(executable, Arguments(profile).Append("--terminate_after_init"), folder, environment);
            OfficeProfileSettings.Apply(settings); OfficeProfileSettings.Verify(settings);
            await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(new { Profile = profile, Initialization = initialized }));
            if (mode == "cancel") await Cancel(executable, profile, folder, environment);
            else await Crash(executable, profile, folder);
            // Crash evidence stays intact. Verify actual release and startup of
            // the same profile, not a fresh-profile replacement hiding damage.
            using (File.Open(settings, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            OfficeProfileSettings.Apply(settings);
            var recovery = await OfficeEvaluationProcess.RunAsync(executable, Arguments(profile).Append("--terminate_after_init"), folder, environment);
            OfficeProfileSettings.Verify(settings);
            if (recovery.ActiveProcessesAfterCleanup != 0) throw new IOException("Recovered engine job was not empty.");
            var stop = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(folder, "stopped.json")));
            reports.Add(new { Mode = mode, Profile = profile, Stop = stop, Recovery = recovery });
            await File.WriteAllTextAsync(Path.Combine(root, "engine-lifetime.json"), JsonSerializer.Serialize(new { reports }));
            Console.WriteLine("PASS: actual Office " + mode + ", retained launcher/engine exits and same-profile restart.");
        }
    }

    private static async Task Cancel(string executable, string profile, string folder, IReadOnlyDictionary<string, string> environment)
    {
        using var cancellation = new CancellationTokenSource();
        Process[] retained = []; Observation? observation = null; var cleanup = new Stopwatch();
        try
        {
            try
            {
                await OfficeEvaluationProcess.RunAsync(executable, Arguments(profile), folder, environment, cancellation.Token,
                    observe: async (pid, belongs, token) =>
                    {
                        retained = await Observe(pid, executable, belongs, token);
                        observation = await Control(retained, token);
                        cleanup.Start(); cancellation.Cancel();
                    });
                throw new Exception("Actual Office cancellation returned success.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (observation is null || retained.Length != 2) throw new IOException("Cancellation lacked live owned-engine evidence.");
            foreach (var process in retained) await Exited(process);
            await File.WriteAllTextAsync(Path.Combine(folder, "stopped.json"), JsonSerializer.Serialize(new
            {
                Observation = observation, CleanupMilliseconds = cleanup.ElapsedMilliseconds,
                LauncherExit = retained[0].ExitCode, EngineExit = retained[1].ExitCode,
                BothHandlesSignaled = retained.All(process => process.HasExited)
            }));
        }
        finally { foreach (var process in retained) process.Dispose(); }
    }

    private static async Task Crash(string executable, string profile, string folder)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--engine-owner", executable, profile, folder }) start.ArgumentList.Add(argument);
        using var owner = Process.Start(start) ?? throw new IOException("Cannot start disposable engine owner.");
        Process[] retained = [];
        try
        {
            var ready = Path.Combine(folder, "ready.json"); var timer = Stopwatch.StartNew();
            while (!File.Exists(ready) && !owner.HasExited && timer.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(20);
            var observation = JsonSerializer.Deserialize<Observation>(await File.ReadAllTextAsync(ready))!;
            retained = [Open(observation.Launcher, executable)];
            retained = [retained[0], Open(observation.Engine, Path.Combine(Path.GetDirectoryName(executable)!, "soffice.bin"))];
            await Task.Delay(300);
            if (owner.HasExited || retained.Any(process => process.HasExited)) throw new IOException("Owner crash lacks a live control interval.");
            var ownerIdentity = Identify(owner); timer.Restart();
            owner.Kill(); // Do not kill the tree: job closure must stop both engine handles.
            await Exited(owner);
            foreach (var process in retained) await Exited(process);
            await File.WriteAllTextAsync(Path.Combine(folder, "stopped.json"), JsonSerializer.Serialize(new
            {
                Owner = ownerIdentity, Observation = observation, CleanupMilliseconds = timer.ElapsedMilliseconds,
                OwnerExit = owner.ExitCode, LauncherExit = retained[0].ExitCode, EngineExit = retained[1].ExitCode,
                BothHandlesSignaled = retained.All(process => process.HasExited)
            }));
        }
        finally
        {
            if (!owner.HasExited) owner.Kill();
            await Exited(owner);
            foreach (var process in retained) process.Dispose();
        }
    }

    internal static async Task OwnerAsync(string executable, string profile, string folder)
    {
        Process[] retained = [];
        try
        {
            await OfficeEvaluationProcess.RunAsync(executable, Arguments(profile), folder, EnvironmentFor(profile),
                observe: async (pid, belongs, token) =>
                {
                    retained = await Observe(pid, executable, belongs, token);
                    var observation = await Control(retained, token);
                    var temporary = Path.Combine(folder, "ready.pending");
                    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(observation), token);
                    File.Move(temporary, Path.Combine(folder, "ready.json"));
                });
            throw new IOException("Engine owner exited before it was deliberately stopped.");
        }
        finally { foreach (var process in retained) process.Dispose(); }
    }

    private static async Task<Process[]> Observe(int pid, string executable, Func<Process, bool> belongs, CancellationToken token)
    {
        var launcher = Process.GetProcessById(pid);
        try
        {
            if (!belongs(launcher) || !string.Equals(launcher.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Launcher is not the requested owned engine.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                foreach (var candidate in Process.GetProcessesByName("soffice.bin"))
                {
                    var keep = false;
                    try
                    {
                        if (!candidate.HasExited && belongs(candidate) && string.Equals(candidate.MainModule?.FileName,
                            Path.Combine(Path.GetDirectoryName(executable)!, "soffice.bin"), StringComparison.OrdinalIgnoreCase))
                        { keep = true; return [launcher, candidate]; }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                    finally { if (!keep) candidate.Dispose(); }
                }
                await Task.Delay(20, deadline.Token);
            }
        }
        catch { launcher.Dispose(); throw; }
    }

    private static async Task<Observation> Control(Process[] processes, CancellationToken token)
    {
        var observation = new Observation(Identify(processes[0]), Identify(processes[1]), 300);
        await Task.Delay(observation.ControlMilliseconds, token);
        if (processes.Any(process => process.HasExited)) throw new IOException("Engine did not remain live during the control interval.");
        return observation;
    }
    private static Identity Identify(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks,
        process.MainModule?.FileName ?? throw new IOException("Engine executable identity is not available."));
    private static Process Open(Identity identity, string expected)
    {
        var process = Process.GetProcessById(identity.Id);
        try
        {
            _ = process.Handle;
            if (Identify(process) != identity || !identity.Path.Equals(expected, StringComparison.OrdinalIgnoreCase) || process.HasExited)
                throw new IOException("Retained engine identity changed.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    private static async Task Exited(Process process)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await process.WaitForExitAsync(deadline.Token);
    }
    private static IEnumerable<string> Arguments(string profile) =>
        ["-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
            "--headless", "--nologo", "--nodefault", "--norestore", "--unaccept=all"];
    private static Dictionary<string, string> EnvironmentFor(string profile)
    {
        var environment = new Dictionary<string, string>();
        foreach (var variable in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
        {
            var path = Path.Combine(profile, variable); Directory.CreateDirectory(path); environment[variable] = path;
        }
        environment["SAL_DISABLE_OPENCL"] = "1"; environment["SAL_LOG"] = "+WARN"; return environment;
    }
}
