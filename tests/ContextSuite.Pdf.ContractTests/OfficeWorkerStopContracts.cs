using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

internal static class OfficeWorkerStopContracts
{
    internal static async Task DiagnoseAsync(string workerPath, string source, string format, string evidence, bool validate = false)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Use a fresh owned Office diagnostic directory.");
        Directory.CreateDirectory(root);
        await using var context = CreateContext(root, workerPath, source, format);
        try
        {
            var candidate = await ContextSuite.Private.Office.OfficeFileAdapter.ExportAsync(
                Path.Combine(Path.GetDirectoryName(workerPath)!, "office-engine"), context.Work, Guid.NewGuid(), CancellationToken.None);
            File.WriteAllText(Path.Combine(root, "candidate.json"), JsonSerializer.Serialize(candidate));
            if (validate)
            {
                var scratch = Path.Combine(root, "validation"); Directory.CreateDirectory(scratch);
                using var structure = new ContextSuite.Private.Pdf.PdfProbeAdapter(Path.Combine(Path.GetDirectoryName(workerPath)!, "pdf-engine"), scratch);
                using var pages = new ContextSuite.Private.Pdf.PdfRasterAdapter(Path.Combine(Path.GetDirectoryName(workerPath)!, "pdf-renderer"), scratch);
                var reservation = Path.Combine(root, $".context-suite-{context.Work.ItemId:N}.tmp");
                using (new FileStream(reservation, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                var result = await new ContextSuite.Private.Office.OfficePdfFileAdapter(structure, pages)
                    .ValidateAsync(new(context.Work, candidate, reservation), default);
                File.WriteAllText(Path.Combine(root, "validation.json"), JsonSerializer.Serialize(result));
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(root, "failure.txt"), error.ToString());
            throw;
        }
        finally { context.VerifySources(); }
    }

    internal static async Task RunAsync(string workerPath, string largeFixtures, string ordinaryFixtures, string evidence,
        string selectedMode = "all", bool workbookCopy = false, bool unfinishedWorkbook = false)
    {
        if (selectedMode is not ("all" or "cancel" or "worker-loss" or "deadline"))
            throw new ArgumentException("Unknown Office interruption mode.", nameof(selectedMode));
        if (unfinishedWorkbook && !workbookCopy) throw new ArgumentException("Unfinished write requires workbook scope.");
        string[] modes = selectedMode == "all" ? ["cancel", "worker-loss", "deadline"] : [selectedMode];
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new ArgumentException("Use a fresh owned Office interruption evidence directory.");
        Directory.CreateDirectory(root);
        var allChecks = new List<string>();
        foreach (var mode in modes)
        {
            var folder = Path.Combine(root, mode); Directory.CreateDirectory(folder);
            var stops = new List<object>(); var reports = new List<object>();
            var families = workbookCopy ? new[] { ("xlsx", "Excel \u00fc.xlsx") } : new[] { ("docx", "Word \u00fc.docx"), ("xlsx", "Excel \u00fc.xlsx"), ("pptx", "PowerPoint \u00fc.pptx") };
            foreach (var (format, name) in families)
            {
                var pauseWorkbook = workbookCopy || format == "xlsx";
                var captureIncompleteWorkbook = pauseWorkbook && (!workbookCopy || unfinishedWorkbook);
                var clock = new DeadlineClock();
                await using var client = new WorkerClient(workerPath, Path.Combine(folder, "worker-scratch"), clock);
                OfficeExportWork stoppedWork;
                await using (var context = CreateContext(folder, workerPath, Path.Combine(largeFixtures, name), format))
                {
                    stoppedWork = context.Work;
                    using var cancel = new CancellationTokenSource();
                    var started = DateTime.UtcNow;
                    var operation = client.ExportOfficeAsync(context.Work, cancel.Token);
                    Process? native = null;
                    OfficeWorkbookWritePause? writePause = null;
                    var readyForFollowing = false;
                    try
                    {
                        var observation = await ObserveGrowthAsync(operation, context.Work, context.Owner.Sid, workerPath, started, pauseWorkbook,
                            captureIncompleteWorkbook ? value => writePause = value : null);
                        native = observation.Process;
                        if (captureIncompleteWorkbook)
                            Check(writePause is not null && writePause.MatchesIncompleteFile(context.Work.WorkbookPath),
                                mode + "/xlsx: debugger detached with writer suspended at an incomplete ZIP");
                        using var worker = Process.GetProcessById(client.ProcessId ?? throw new IOException("Missing live worker."));
                        _ = worker.Handle;
                        Check(!worker.HasExited && !native.HasExited && !operation.IsCompleted,
                            mode + "/" + format + ": stop targets retained live worker and host handles");
                        Check(clock.Current?.Due == TimeSpan.FromSeconds(180), mode + "/" + format + ": application deadline is 180 seconds");
                        if (pauseWorkbook)
                            Check(File.Exists(context.Work.CandidatePath) && new FileInfo(context.Work.CandidatePath).Length > 0,
                                mode + "/xlsx: workbook growth follows the PDF export");
                        var stopwatch = Stopwatch.StartNew();
                        if (mode == "cancel") cancel.Cancel();
                        else if (mode == "deadline") clock.Current!.Expire();
                        else worker.Kill(); // Worker only: its job must stop the native host.
                        var refused = false;
                        try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); }
                        catch (OperationCanceledException) when (mode == "cancel") { refused = true; }
                        catch (MediaWorkerException error) when (mode != "cancel" && error.Failure ==
                            (mode == "deadline" ? ImageFailure.TimedOut : ImageFailure.WorkerTerminated)) { refused = true; }
                        Check(refused, mode + "/" + format + ": interruption returns no accepted candidate");
                        var hostExitedAtReturn = native.HasExited;
                        await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        Check(worker.HasExited && native.HasExited && hostExitedAtReturn,
                            mode + "/" + format + ": worker and exact native host terminate before the client returns");
                        if (captureIncompleteWorkbook)
                            Check(writePause!.MatchesIncompleteFile(context.Work.WorkbookPath),
                                mode + "/xlsx: normal lifetime shutdown leaves the same incomplete ZIP");
                        Check(client.ProcessId is null, mode + "/" + format + ": application clears its stopped worker");
                        Check(ProfileState(context.Work.ProfileName, context.Owner.Sid) == (true, true),
                            mode + "/" + format + ": surviving application retains profile ownership");
                        context.VerifySources();
                        Check(true, mode + "/" + format + ": copied source and original remain unchanged");
                        stops.Add(new { Format = format, Work = context.Work, Phase = writePause is not null && pauseWorkbook ? "unfinished-workbook-copy" : workbookCopy ? "workbook-copy" : "pdf", observation.FirstBytes, observation.SecondBytes,
                            WritePause = writePause is null ? null : new { writePause.Bytes, writePause.Sha256, writePause.WriteBreakpoints, writePause.ThreadCount },
                            WorkerId = worker.Id, WorkerCreated = worker.StartTime.ToUniversalTime(), HostId = native.Id,
                            HostCreated = native.StartTime.ToUniversalTime(), HostExitedAtClientReturn = hostExitedAtReturn,
                            StopMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                            PartialBytes = File.Exists(context.Work.CandidatePath) ? new FileInfo(context.Work.CandidatePath).Length : 0,
                            WorkbookBytes = File.Exists(context.Work.WorkbookPath) ? new FileInfo(context.Work.WorkbookPath).Length : 0 });
                        // Retain the stop observation even if later grant cleanup fails.
                        File.WriteAllText(Path.Combine(folder, "progress.json"), JsonSerializer.Serialize(new { Stops = stops, Reports = reports }));
                        readyForFollowing = true;
                    }
                    finally
                    {
                        cancel.Cancel();
                        try { await operation; } catch (Exception error) when (error is OperationCanceledException or MediaWorkerException) { }
                        finally
                        {
                            // A successful stop already retired the worker. Keep
                            // the client alive for the following request.
                            try { if (!readyForFollowing) await client.DisposeAsync(); }
                            finally
                            {
                                writePause?.Dispose();
                                if (native is not null)
                                {
                                    try { await native.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                                    finally { native.Dispose(); }
                                }
                            }
                        }
                    }
                }
                // The application owner deliberately holds source/path leases
                // until grants are revoked. Exclusive access is meaningful only
                // after both process termination and owner disposal.
                using (var exclusive = new FileStream(stoppedWork.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (File.Exists(stoppedWork.CandidatePath))
                    using (var exclusive = new FileStream(stoppedWork.CandidatePath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                Check(true, mode + "/" + format + ": input and any partial PDF are released after process and profile cleanup");
                if (pauseWorkbook)
                {
                    using var exclusive = new FileStream(stoppedWork.WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    Check(exclusive.Length > 0, mode + "/xlsx: interrupted workbook handle is released after cleanup");
                }
                await using (var following = CreateContext(folder, workerPath, Path.Combine(ordinaryFixtures, name), format))
                {
                    try
                    {
                        var candidate = await client.ExportOfficeAsync(following.Work, CancellationToken.None);
                        Check(candidate.Completion.OutputSha256 == Hash(following.Work.CandidatePath) &&
                            (!pauseWorkbook || candidate.Completion.WorkbookBytes > 0 && candidate.Completion.WorkbookSha256 == Hash(following.Work.WorkbookPath)),
                            mode + "/" + format + ": same client completes a fresh following export");
                        following.VerifySources();
                        reports.Add(new { Work = following.Work, Candidate = candidate, Output = following.Work.CandidatePath });
                    }
                    finally { await client.DisposeAsync(); }
                }
                Check(true, mode + "/" + format + ": interrupted and following profiles are removed");
                Console.WriteLine("Passed active Office stop and following export: " + mode + "/" + format);
                File.WriteAllText(Path.Combine(folder, "progress.json"), JsonSerializer.Serialize(new { Stops = stops, Reports = reports }));
            }
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(new { Passed = true, Mode = mode, WorkbookCopy = workbookCopy, UnfinishedWorkbook = unfinishedWorkbook, Stops = stops, Reports = reports },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Modes = modes, WorkbookCopy = workbookCopy, UnfinishedWorkbook = unfinishedWorkbook, Checks = allChecks },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Office interruption checks passed: " + allChecks.Count);

        void Check(bool value, string name) { if (!value) throw new IOException(name); allChecks.Add(name); }
    }

    internal static async Task<(Process Process, long FirstBytes, long SecondBytes)> ObserveGrowthAsync(
        Task<OfficeExportCandidate> operation, OfficeExportWork work, string sid, string workerPath, DateTime started, bool workbookCopy = false,
        Action<OfficeWorkbookWritePause>? capturePause = null)
    {
        var executable = Path.Combine(Path.GetDirectoryName(workerPath)!, "office-engine", "ContextSuite.OfficeHost.exe");
        Process? native = null;
        var path = workbookCopy ? work.WorkbookPath : work.CandidatePath;
        var observations = new List<object>();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            long first = 0;
            while (!operation.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (native is null)
                    foreach (var process in Process.GetProcessesByName("ContextSuite.OfficeHost"))
                    {
                        var retained = false;
                        try
                        {
                            _ = process.Handle;
                            if (native is null && !process.HasExited && process.StartTime.ToUniversalTime() >= started &&
                                string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase) &&
                                AppContainerSid(process) == sid)
                            { native = process; retained = true; }
                        }
                        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException) { }
                        finally { if (!retained) process.Dispose(); }
                    }
                // Install after runtime initialization, while PDF output precedes
                // workbook serialization. Startup thread context restoration can
                // otherwise replace hardware registers installed too early.
                if (native is not null && !native.HasExited && capturePause is not null &&
                    File.Exists(work.CandidatePath) && new FileInfo(work.CandidatePath).Length > 0)
                {
                    var pause = await Task.Run(() => OfficeWorkbookWritePause.Capture(native, path, operation), deadline.Token);
                    capturePause(pause);
                    var found = native; native = null; return (found, 0, pause.Bytes);
                }
                if (capturePause is null && native is not null && !native.HasExited && File.Exists(path))
                {
                    var bytes = new FileInfo(path).Length;
                    if (workbookCopy)
                    {
                        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var prefix = new byte[4];
                        if (input.Read(prefix) != 4 || !prefix.AsSpan().SequenceEqual(new byte[] { 80, 75, 3, 4 }))
                        { await Task.Delay(5, deadline.Token); continue; }
                    }
                    if (observations.Count < 64) observations.Add(new { Bytes = bytes, ObservedUtc = DateTime.UtcNow });
                    if (first == 0) first = bytes;
                    else if (bytes > first && !operation.IsCompleted)
                    { var found = native; native = null; return (found, first, bytes); }
                }
                await Task.Delay(5, deadline.Token);
            }
            await operation; // Preserve the real execution error when observation loses the operation.
            throw new IOException("Office completed before a growing-" + (workbookCopy ? "workbook" : "PDF") + " stop point was observed.");
        }
        finally
        {
            native?.Dispose();
            if (workbookCopy) File.WriteAllText(Path.Combine(Path.GetDirectoryName(work.DirectoryPath)!, work.ItemId.ToString("N") + "-workbook-observation.json"),
                JsonSerializer.Serialize(new { Path = path, Observations = observations }));
        }
    }

    private static Context CreateContext(string root, string worker, string source, string format)
    {
        var id = Guid.NewGuid(); var directory = Path.Combine(root, "office-" + id.ToString("N"));
        foreach (var name in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(directory, name));
        var work = new OfficeExportWork(id, directory, "ContextSuite.Office.Evaluation." + id.ToString("N"),
            format, format == "xlsx" ? "cached" : "none", new FileInfo(source).Length, Hash(source));
        work.Validate(); File.Copy(source, work.SourcePath);
        File.SetAttributes(work.SourcePath, File.GetAttributes(work.SourcePath) | FileAttributes.ReadOnly);
        var owner = OfficeSandboxOwner.Create(work.ProfileName);
        Context? context = null;
        try
        {
            context = new Context(work, owner, source, root);
            owner.GrantDirectory(Path.Combine(Path.GetDirectoryName(worker)!, "office-engine"), false);
            owner.GrantDirectory(Path.Combine(directory, "input"), false);
            foreach (var name in new[] { "output", "profile", "temp" }) owner.GrantDirectory(Path.Combine(directory, name), true);
            return context;
        }
        catch
        {
            if (context is null) owner.Dispose();
            else context.Dispose();
            throw;
        }
    }

    private sealed class Context : IDisposable, IAsyncDisposable
    {
        internal OfficeExportWork Work { get; }
        internal OfficeSandboxOwner Owner { get; }
        private readonly string _original, _receipt;
        private readonly DateTime _originalTime, _copyTime;
        internal Context(OfficeExportWork work, OfficeSandboxOwner owner, string source, string root)
        {
            Work = work; Owner = owner; _original = source;
            _originalTime = File.GetLastWriteTimeUtc(source); _copyTime = File.GetLastWriteTimeUtc(work.SourcePath);
            _receipt = Path.Combine(root, work.ItemId.ToString("N") + "-profile.json");
            File.WriteAllText(_receipt, JsonSerializer.Serialize(new { work.ProfileName, owner.Sid, Removed = false }));
        }
        internal void VerifySources()
        {
            if (Hash(_original) != Work.SourceSha256 || Hash(Work.SourcePath) != Work.SourceSha256 ||
                File.GetLastWriteTimeUtc(_original) != _originalTime || File.GetLastWriteTimeUtc(Work.SourcePath) != _copyTime)
                throw new IOException("Office interruption changed a source.");
        }
        public void Dispose()
        {
            Owner.Dispose();
            RecordRemoval();
        }
        public async ValueTask DisposeAsync()
        {
            await Owner.DisposeAsync();
            RecordRemoval();
        }
        private void RecordRemoval()
        {
            if (ProfileState(Work.ProfileName, Owner.Sid) != (false, false)) throw new IOException("Owned interruption profile remains.");
            File.WriteAllText(_receipt, JsonSerializer.Serialize(new { Work.ProfileName, Owner.Sid, Removed = true }));
        }
    }

    private sealed class DeadlineClock : TimeProvider
    {
        internal DeadlineTimer? Current { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { Current = new(callback, state, dueTime); return Current; }
    }
    private sealed class DeadlineTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        private int _disposed;
        internal TimeSpan Due { get; private set; } = due;
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return _disposed == 0; }
        internal void Expire() { if (Interlocked.Exchange(ref _disposed, 1) == 0) callback(state); }
        public void Dispose() { Interlocked.Exchange(ref _disposed, 1); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static (bool Folder, bool Mapping) ProfileState(string name, string sid)
    {
        using var mapping = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppContainer\Mappings\" + sid);
        return (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", name)), mapping is not null);
    }
    internal static string? AppContainerSid(Process process)
    {
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            GetTokenInformation(token, 31, IntPtr.Zero, 0, out var bytes);
            if (bytes < IntPtr.Size || bytes > 65536) return null;
            var data = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                if (!GetTokenInformation(token, 31, data, bytes, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var sid = Marshal.ReadIntPtr(data);
                return sid == IntPtr.Zero ? null : new SecurityIdentifier(sid).Value;
            }
            finally { Marshal.FreeHGlobal(data); }
        }
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeFileHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeFileHandle token, int information, IntPtr buffer, uint length, out uint returned);
}
