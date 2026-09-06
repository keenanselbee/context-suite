using System.Diagnostics;
using System.Text;

internal static class ApplicationSmoke
{
    public static async Task RunAsync(string executable, string fixture)
    {
        // Exercise the actual shipping activation entry point, not a test mode.
        // Temporary request files use the same owned local directory as the native shell.
        var existing = Process.GetProcessesByName("ContextSuite.Application");
        try
        {
            if (existing.Length != 0) throw new InvalidOperationException("Close Context Suite before application integration tests.");
        }
        finally { foreach (var process in existing) process.Dispose(); }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ContextSuite", "Prototype", "Activations");
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        var children = new List<Process>();
        var workers = new List<Process>();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            foreach (var operation in new[] { "analyze", "convert", "optimize" })
            {
                var id = Guid.NewGuid();
                var path = Path.Combine(directory, id.ToString("D") + ".request");
                var temporary = Path.ChangeExtension(path, ".tmp");
                paths.Add(path);
                paths.Add(temporary);
                var action = operation switch { "analyze" => "open-details", "convert" => "choose-format", _ => "choose-preset" };
                var content = $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount=3\n" +
                    string.Concat(Enumerable.Repeat($"path={fixture}\n", 3));
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(content), timeout.Token);
                    stream.Flush(true);
                }
                File.Move(temporary, path);
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("--activation-file");
                start.ArgumentList.Add(path);
                children.Add(Process.Start(start) ?? throw new IOException("Application failed to launch."));
                children[^1].ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
                children[^1].BeginErrorReadLine();
                while (File.Exists(path))
                {
                    if (children[^1].HasExited) throw new IOException("Application exited without accepting the request.");
                    try { await Task.Delay(50, timeout.Token); }
                    catch (OperationCanceledException)
                    {
                        children[^1].Kill(true);
                        await children[^1].WaitForExitAsync();
                        throw new IOException($"{operation} activation timed out; see application diagnostics above.");
                    }
                }
                if (children.Count > 1)
                {
                    await children[^1].WaitForExitAsync(timeout.Token);
                    if (children[^1].ExitCode != 0) throw new IOException("Activation forwarding failed.");
                }
            }
            children[0].Refresh();
            if (children[0].HasExited || children[0].MainWindowHandle == IntPtr.Zero)
                throw new IOException("The WPF owner did not retain its main window.");
            var workerPath = Path.Combine(Path.GetDirectoryName(executable)!, "ContextSuite.Worker.exe");
            while (workers.Count == 0)
            {
                foreach (var process in Process.GetProcessesByName("ContextSuite.Worker"))
                {
                    if (string.Equals(process.MainModule?.FileName, workerPath, StringComparison.OrdinalIgnoreCase)) workers.Add(process);
                    else process.Dispose();
                }
                if (workers.Count == 0) await Task.Delay(50, timeout.Token);
            }
            if (workers.Count != 1) throw new IOException("Expected one on-demand worker.");
            // Abrupt parent exit must stop the worker even without a graceful shutdown frame.
            children[0].Kill();
            await children[0].WaitForExitAsync(timeout.Token);
            foreach (var worker in workers) await worker.WaitForExitAsync(timeout.Token);
            if (await File.ReadAllTextAsync(fixture, timeout.Token) !=
                "A deliberately non-media fixture; no transformation is allowed.")
                throw new IOException("The foundation modified a selected source.");
        }
        finally
        {
            foreach (var process in children)
            {
                if (!process.HasExited) process.Kill(true);
                process.Dispose();
            }
            foreach (var worker in workers) worker.Dispose();
            foreach (var path in paths) File.Delete(path);
        }
    }
}
