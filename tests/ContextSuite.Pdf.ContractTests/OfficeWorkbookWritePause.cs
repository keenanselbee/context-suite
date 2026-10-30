using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// Opt-in test instrumentation only. Callers must first identify their exact
// host by executable, creation time and owned AppContainer SID.
internal sealed class OfficeWorkbookWritePause : IDisposable
{
    private readonly List<nint> _threads;
    internal long Bytes { get; }
    internal string Sha256 { get; }
    internal int WriteBreakpoints { get; }
    internal int ThreadCount => _threads.Count;

    private OfficeWorkbookWritePause(List<nint> threads, byte[] data, int hits)
    { _threads = threads; Bytes = data.Length; Sha256 = Convert.ToHexString(SHA256.HashData(data)); WriteBreakpoints = hits; }

    internal bool MatchesIncompleteFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length != Bytes) return false;
        var data = new byte[checked((int)Bytes)]; stream.ReadExactly(data);
        return IncompleteZip(data) && Convert.ToHexString(SHA256.HashData(data)) == Sha256;
    }

    internal static OfficeWorkbookWritePause Capture(Process host, string workbook, Task operation)
    {
        if (!Environment.Is64BitProcess || !Path.GetFullPath(workbook).Contains("\\.codex-temp\\office-worker\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use an owned x64 workbook interruption context.");
        var module = host.Modules.Cast<ProcessModule>().Single(item => string.Equals(item.ModuleName, "kernelbase.dll", StringComparison.OrdinalIgnoreCase));
        var localModule = GetModuleHandle("kernelbase.dll");
        var localWrite = GetProcAddress(localModule, "WriteFile");
        Require(localModule != 0 && localWrite != 0, "Resolve WriteFile");
        var address = module.BaseAddress + (localWrite - localModule);
        var localBytes = new byte[16]; var remoteBytes = new byte[16];
        Marshal.Copy(localWrite, localBytes, 0, localBytes.Length);
        Require(ReadProcessMemory(host.Handle, address, remoteBytes, 16, out var read) && read == 16 && localBytes.SequenceEqual(remoteBytes), "Verify remote WriteFile");
        var threads = new Dictionary<uint, nint>();
        var attached = false; var pending = false; uint eventThread = 0; var hits = 0;
        var events = new List<object>(); var matchingWrites = 0;
        var buffer = Marshal.AllocHGlobal(176);
        try
        {
            Require(DebugActiveProcess((uint)host.Id), "Attach owned Office host"); attached = true;
            Require(DebugSetProcessKillOnExit(false), "Retain application lifetime ownership");
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(100) && !operation.IsCompleted)
            {
                if (!WaitForDebugEvent(buffer, 250))
                { if (Marshal.GetLastWin32Error() == 121) continue; throw new Win32Exception(Marshal.GetLastWin32Error()); }
                var kind = (uint)Marshal.ReadInt32(buffer);
                var pid = (uint)Marshal.ReadInt32(buffer, 4); eventThread = (uint)Marshal.ReadInt32(buffer, 8);
                pending = true;
                if (pid != host.Id) throw new IOException("Unexpected debug process.");
                uint status = 0x10002;
                if (kind is 2 or 3)
                {
                    var thread = OpenThread(0x1a, false, eventThread);
                    Require(thread != 0, "Open owned host thread"); threads.Add(eventThread, thread);
                    using var context = new ThreadContext(thread);
                    if ((context.Read64(112) & 0xff) != 0) throw new IOException("Existing hardware breakpoint refused.");
                    context.Write64(72, (long)address); context.Write64(104, 0); context.Write64(112, 1); context.Save();
                }
                if (kind is 3 or 6)
                { var file = Marshal.ReadIntPtr(buffer, 16); if (file != 0) CloseHandle(file); }
                if (kind == 4)
                { CloseHandle(threads[eventThread]); threads.Remove(eventThread); }
                if (kind == 5)
                { events.Add(new { ExitCode = Marshal.ReadInt32(buffer, 16), Hits = hits }); throw new IOException("Host exited before the incomplete workbook breakpoint."); }
                if (kind == 1)
                {
                    var exception = (uint)Marshal.ReadInt32(buffer, 16);
                    if (exception == 0x80000004)
                    {
                        using var context = new ThreadContext(threads[eventThread]);
                        if (context.Read64(248) != (long)address || (context.Read64(104) & 1) == 0)
                            throw new IOException("Unexpected single-step event.");
                        hits++;
                        var matches = MatchesFile(host.Handle, (nint)context.Read64(128), workbook);
                        if (matches) matchingWrites++;
                        if (events.Count < 32 || matches && matchingWrites < 16)
                            events.Add(new { Thread = eventThread, Write = hits, Matches = matches, Bytes = File.Exists(workbook) ? new FileInfo(workbook).Length : 0 });
                        if (matches && File.Exists(workbook))
                        {
                            using var stream = new FileStream(workbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                            if (stream.Length > 64 * 1024 * 1024) throw new IOException("Workbook observation exceeds its bound.");
                            var data = new byte[checked((int)stream.Length)]; stream.ReadExactly(data);
                            if (IncompleteZip(data))
                            {
                                foreach (var thread in threads.Values)
                                {
                                    var previous = SuspendThread(thread);
                                    Require(previous != uint.MaxValue, "Suspend owned writer");
                                    if (previous != 0) throw new IOException("Unexpected existing thread suspension.");
                                    using var stopped = new ThreadContext(thread);
                                    stopped.Write64(72, 0); stopped.Write64(104, 0); stopped.Write64(112, 0);
                                    stopped.Flags &= ~0x100u; stopped.Save();
                                }
                                Require(ContinueDebugEvent(pid, eventThread, status), "Continue suspended writer"); pending = false;
                                Require(DebugActiveProcessStop(pid), "Detach paused writer"); attached = false;
                                var result = new OfficeWorkbookWritePause(threads.Values.ToList(), data, hits);
                                threads.Clear(); return result;
                            }
                        }
                        context.Write64(104, 0); context.Flags |= 0x10000; context.Save();
                    }
                    else if (exception != 0x80000003) status = 0x80010001;
                    else
                    {
                        foreach (var pair in threads)
                        {
                            using var initial = new ThreadContext(pair.Value);
                            events.Add(new { Thread = pair.Key, InitialDr0 = initial.Read64(72), InitialDr7 = initial.Read64(112) });
                        }
                    }
                }
                Require(ContinueDebugEvent(pid, eventThread, status), "Continue host event"); pending = false;
            }
            throw new IOException("No unfinished workbook write was captured before the bounded deadline.");
        }
        finally
        {
            if (attached)
            {
                // Only the exact retained host is terminated on instrumentation failure.
                if (!host.HasExited) host.Kill();
                if (pending) ContinueDebugEvent((uint)host.Id, eventThread, 0x10002);
                var end = Stopwatch.StartNew();
                while (end.Elapsed < TimeSpan.FromSeconds(5))
                {
                    if (!WaitForDebugEvent(buffer, 100)) continue;
                    var kind = Marshal.ReadInt32(buffer);
                    ContinueDebugEvent((uint)Marshal.ReadInt32(buffer, 4), (uint)Marshal.ReadInt32(buffer, 8), 0x10002);
                    if (kind == 5) { attached = false; break; }
                }
                if (attached) DebugActiveProcessStop((uint)host.Id);
            }
            foreach (var thread in threads.Values) CloseHandle(thread);
            Marshal.FreeHGlobal(buffer);
            var directory = Path.GetDirectoryName(Path.GetDirectoryName(workbook))!;
            File.WriteAllText(directory + "-write-pause.json", System.Text.Json.JsonSerializer.Serialize(new
            { HostId = host.Id, Address = (long)address, WriteBreakpoints = hits, MatchingWrites = matchingWrites, Events = events }));
        }
    }

    internal static bool IncompleteZip(byte[] data) => data.Length >= 4096 &&
        data.AsSpan(0, 4).SequenceEqual(new byte[] { 80, 75, 3, 4 }) &&
        data.AsSpan(Math.Max(0, data.Length - 65557)).IndexOf(new byte[] { 80, 75, 5, 6 }) < 0;

    public void Dispose()
    {
        // Normally the lifetime job has already terminated these threads. On a
        // caller failure release our suspension; never leave a surviving host frozen.
        foreach (var thread in _threads) { ResumeThread(thread); CloseHandle(thread); }
        _threads.Clear();
    }

    private static bool MatchesFile(nint process, nint file, string expected)
    {
        if (!DuplicateHandle(process, file, GetCurrentProcess(), out var copy, 0, false, 2)) return false;
        try
        {
            var path = new System.Text.StringBuilder(32768);
            var length = GetFinalPathNameByHandle(copy, path, (uint)path.Capacity, 0);
            if (length == 0 || length >= path.Capacity) return false;
            var name = path.ToString(); if (name.StartsWith("\\\\?\\", StringComparison.Ordinal)) name = name[4..];
            return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseHandle(copy); }
    }

    private static void Require(bool value, string message)
    { if (!value) throw new Win32Exception(Marshal.GetLastWin32Error(), message); }

    // Windows SDK AMD64 CONTEXT layout, 16-byte aligned. Only control, integer
    // and debug fields are requested; no code or process memory is written.
    private sealed class ThreadContext : IDisposable
    {
        private readonly nint _storage, _address, _thread;
        internal ThreadContext(nint thread)
        {
            _thread = thread; _storage = Marshal.AllocHGlobal(1248); _address = (_storage + 15) & ~15;
            Marshal.Copy(new byte[1232], 0, _address, 1232); Marshal.WriteInt32(_address, 48, 0x100013);
            if (!GetThreadContext(thread, _address))
            { var error = Marshal.GetLastWin32Error(); Marshal.FreeHGlobal(_storage); throw new Win32Exception(error); }
        }
        internal long Read64(int offset) => Marshal.ReadInt64(_address, offset);
        internal void Write64(int offset, long value) => Marshal.WriteInt64(_address, offset, value);
        internal uint Flags { get => (uint)Marshal.ReadInt32(_address, 68); set => Marshal.WriteInt32(_address, 68, (int)value); }
        internal void Save() => Require(SetThreadContext(_thread, _address), "Set owned thread context");
        public void Dispose() => Marshal.FreeHGlobal(_storage);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process, nint address, byte[] bytes, nuint count, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugActiveProcess(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugActiveProcessStop(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DebugSetProcessKillOnExit(bool kill);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WaitForDebugEvent(nint info, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ContinueDebugEvent(uint pid, uint tid, uint status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(nint thread, nint context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadContext(nint thread, nint context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(nint thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(nint process, nint source, nint target, out nint copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(nint file, System.Text.StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
