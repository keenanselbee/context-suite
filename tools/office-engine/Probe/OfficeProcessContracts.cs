using System.Diagnostics;
using System.Text.Json;

internal static class OfficeProcessContracts
{
    private sealed record Identity(int Id, long Created, string Path);

    internal static async Task RunAsync(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Use fresh process-contract scratch.");
        Directory.CreateDirectory(root);
        var executable = Environment.ProcessPath!;
        var checks = new List<string>(); var reports = new List<object>();
        string[] literal = ["", "space value", "quote\"value", "backslash\\", "\u00fc", "line\nbreak"];
        var control = await OfficeEvaluationProcess.RunAsync(executable, new[] { "--process-child", "echo", root }.Concat(literal), root,
            new Dictionary<string, string> { ["CONTEXTSUITE_PROCESS_CONTROL"] = "local \u00fc value" });
        using (var parsed = JsonDocument.Parse(control.Output))
        {
            var data = parsed.RootElement;
            Check(data.GetProperty("Arguments").EnumerateArray().Select(item => item.GetString()).SequenceEqual(literal) &&
                data.GetProperty("Environment").GetString() == "local \u00fc value" && data.GetProperty("Input").GetString() == "" &&
                control.Error == "stderr control", "literal arguments, environment, empty stdin and separate diagnostics");
        }
        Check(control.TotalProcesses >= 1 && control.ActiveProcessesAfterCleanup == 0, "normal completion leaves an empty owned job"); reports.Add(control);
        await Refuse("exit", typeof(IOException));
        await Refuse("spam", typeof(InvalidDataException));
        await Refuse("unicode-spam", typeof(InvalidDataException));
        await Refuse("sleep", typeof(OperationCanceledException), TimeSpan.FromSeconds(2));
        await Tree("orphan", false);
        await Tree("cancel", true);
        await OwnerCrash();
        var followup = await OfficeEvaluationProcess.RunAsync(executable, ["--process-child", "echo", root], root);
        Check(followup.Output.Contains("Arguments", StringComparison.Ordinal), "fresh successful child after failure and owner crash");
        reports.Add(followup);
        await File.WriteAllTextAsync(Path.Combine(root, "process-contracts.json"), JsonSerializer.Serialize(new { checks, reports }));
        Console.WriteLine($"Passed {checks.Count} Office evaluation process checks. Evidence: {root}");

        void Check(bool pass, string name)
        {
            if (!pass) throw new InvalidDataException("Office process contract failed: " + name);
            checks.Add(name); Console.WriteLine("PASS: " + name);
        }
        async Task Refuse(string mode, Type expected, TimeSpan? timeout = null)
        {
            var folder = Path.Combine(root, mode); Directory.CreateDirectory(folder);
            var timer = Stopwatch.StartNew();
            try
            {
                await OfficeEvaluationProcess.RunAsync(executable, ["--process-child", mode, folder], folder, timeout: timeout);
                throw new Exception("Expected process refusal: " + mode);
            }
            catch (Exception ex) when (expected.IsInstanceOfType(ex))
            {
                Check(timer.Elapsed < TimeSpan.FromSeconds(10), mode + " refuses and cleans up without waiting for the 60-second deadline");
                reports.Add(new { Mode = mode, Error = ex.GetType().Name, Milliseconds = timer.ElapsedMilliseconds });
            }
        }
        async Task Tree(string name, bool cancel)
        {
            var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            using var cancellation = new CancellationTokenSource();
            var running = OfficeEvaluationProcess.RunAsync(executable, ["--process-child", "orphan", folder], folder, token: cancellation.Token);
            using var parent = await Observe(folder, "orphan", executable);
            using var descendant = await Observe(folder, "sleep", executable);
            await Task.Delay(300);
            Check(!parent.HasExited && !descendant.HasExited, name + " observes a live parent and descendant before stopping");
            if (cancel) cancellation.Cancel();
            else await File.WriteAllTextAsync(Path.Combine(folder, "release"), "exit root");
            try
            {
                var result = await running;
                if (cancel) throw new Exception("Cancellation returned success.");
                reports.Add(result);
            }
            catch (OperationCanceledException) when (cancel) { }
            await Exited(parent); await Exited(descendant);
            Check(parent.HasExited && descendant.HasExited, name + " stops both retained process handles");
        }
        async Task OwnerCrash()
        {
            var folder = Path.Combine(root, "owner-crash"); Directory.CreateDirectory(folder);
            using var owner = Process.Start(Start(executable, "--process-owner", folder)) ?? throw new IOException("Cannot start owned contract host.");
            try
            {
                using var parent = await Observe(folder, "orphan", executable);
                using var descendant = await Observe(folder, "sleep", executable);
                await Task.Delay(300);
                Check(!owner.HasExited && !parent.HasExited && !descendant.HasExited, "owner crash control retains three live owned processes");
                owner.Kill(); // Only this created owner; no tree kill can satisfy the child assertions.
                await Exited(owner); await Exited(parent); await Exited(descendant);
                Check(parent.HasExited && descendant.HasExited, "abrupt owner exit closes the job and stops both descendants");
                reports.Add(new { Mode = "owner-crash", Owner = owner.Id, Parent = parent.Id, Descendant = descendant.Id });
            }
            finally
            {
                if (!owner.HasExited) owner.Kill();
                await Exited(owner);
            }
        }
    }

    internal static async Task<int> Child(string mode, string root, string[] arguments)
    {
        if (mode == "echo")
        {
            Console.Write(JsonSerializer.Serialize(new { Arguments = arguments, Environment = Environment.GetEnvironmentVariable("CONTEXTSUITE_PROCESS_CONTROL"), Input = await Console.In.ReadToEndAsync() }));
            Console.Error.Write("stderr control"); return 0;
        }
        if (mode == "exit") { Console.Error.Write("deliberate exit 74"); return 74; }
        if (mode is "spam" or "unicode-spam")
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            Console.Write(mode == "spam" ? new string('x', 100000) : new string('\u00fc', 40000));
            await Task.Delay(60000); return 0;
        }
        using var self = Process.GetCurrentProcess();
        var identity = new Identity(self.Id, self.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!);
        var temporary = Path.Combine(root, mode + ".pending");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(identity));
        File.Move(temporary, Path.Combine(root, mode + ".ready"));
        if (mode == "sleep") { await Task.Delay(60000); return 0; }
        if (mode != "orphan") return 2;
        using var descendant = Process.Start(Start(Environment.ProcessPath!, "--process-child", "sleep", root)) ?? throw new IOException("Cannot create descendant.");
        while (!File.Exists(Path.Combine(root, "release"))) await Task.Delay(20);
        return 0;
    }

    private static ProcessStartInfo Start(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    private static async Task<Process> Observe(string root, string name, string executable)
    {
        var ready = Path.Combine(root, name + ".ready"); var timer = Stopwatch.StartNew();
        while (!File.Exists(ready) && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        var identity = JsonSerializer.Deserialize<Identity>(await File.ReadAllTextAsync(ready))!;
        var process = Process.GetProcessById(identity.Id);
        try
        {
            _ = process.Handle;
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != identity.Created ||
                !identity.Path.Equals(executable, StringComparison.OrdinalIgnoreCase) ||
                !process.MainModule!.FileName.Equals(executable, StringComparison.OrdinalIgnoreCase)) throw new IOException("Owned process identity changed.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    private static async Task Exited(Process process)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(deadline.Token);
    }
}
