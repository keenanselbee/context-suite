using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ContextSuite.Application.Infrastructure;
using Microsoft.Win32.SafeHandles;

internal static class AnalysisIoContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "analysis-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var observations = new List<object>();
        foreach (var mode in new[] { "cancel", "deadline", "release" })
        {
            var path = Path.Combine(root, mode + ".bin");
            await File.WriteAllBytesAsync(path, [0, 127, 255, 1]);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var modified = File.GetLastWriteTimeUtc(path);
            using var cancellation = new CancellationTokenSource();
            using var held = new HeldOplock(path);
            var elapsed = Stopwatch.StartNew();
            var reading = FileAnalysisReader.ReadAsync(path, cancellation.Token);
            try
            {
                check(await Task.Run(() => held.Broken.WaitOne(TimeSpan.FromSeconds(3))), "analysis I/O: " + mode + " reaches native open");
                check(!reading.IsCompleted, "analysis I/O: " + mode + " is blocked by the held oplock");
                if (mode == "cancel") cancellation.Cancel();
                if (mode == "release") held.Dispose();
                string outcome;
                try
                {
                    var result = await reading.WaitAsync(TimeSpan.FromSeconds(8));
                    check(mode == "release" && result.FileBytes == 4 && result.InspectedBytes == 4,
                        "analysis I/O: released control completes the real reader");
                    outcome = "success";
                }
                catch (OperationCanceledException error)
                {
                    check(mode == "cancel" && cancellation.IsCancellationRequested && error.CancellationToken.IsCancellationRequested,
                        "analysis I/O: blocked open returns cancellation without releasing oplock");
                    outcome = "canceled";
                }
                catch (IOException error)
                {
                    check(mode == "deadline" && error.Message == "Opening or reading the file exceeded the time limit.",
                        "analysis I/O: blocked open returns a readable deadline failure without releasing oplock");
                    outcome = "deadline";
                }
                check(mode != "cancel" || elapsed.Elapsed < TimeSpan.FromSeconds(3), "analysis I/O: user cancellation does not wait for the five-second deadline");
                check(mode != "deadline" || elapsed.Elapsed >= TimeSpan.FromSeconds(4.5), "analysis I/O: deadline uses the production five-second budget");
                observations.Add(new { mode, outcome, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds });
            }
            finally
            {
                // Release only the authored lock and observe the original task even
                // on assertion/observation failure. Never leave blocked background I/O.
                held.Dispose();
                try { await reading; }
                catch (Exception error) when (error is OperationCanceledException or IOException or Win32Exception) { }
            }
            var afterHash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            check(hash.SequenceEqual(afterHash) && modified == File.GetLastWriteTimeUtc(path),
                "analysis I/O: " + mode + " preserves original bytes and time");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(true, "analysis I/O: " + mode + " leaves no reader lease");
            check((await FileAnalysisReader.ReadAsync(path, CancellationToken.None)).FileBytes == 4,
                "analysis I/O: " + mode + " permits a fresh analysis");
        }

        // Keep the same native thread after scope disposal, then cancel its old
        // token while a new unrelated open is blocked. A late cancellation must
        // not escape into that second operation, even through a queued timer tick.
        var reusePath = Path.Combine(root, "thread-reuse.bin");
        await File.WriteAllBytesAsync(reusePath, [1, 2, 3]);
        using var oldToken = new CancellationTokenSource();
        using var reuseLock = new HeldOplock(reusePath);
        var reuse = Task.Run(() =>
        {
            SynchronousFileIo.Run(() => { oldToken.Cancel(); return 1; }, oldToken.Token);
            using var file = HeldOplock.Open(reusePath);
            if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        });
        try
        {
            check(await Task.Run(() => reuseLock.Broken.WaitOne(TimeSpan.FromSeconds(3))), "analysis I/O: subsequent same-thread open is pending");
            await Task.Delay(250);
            check(!reuse.IsCompleted, "analysis I/O: disposed scope cannot cancel later same-thread I/O");
        }
        finally { reuseLock.Dispose(); await reuse; }

        var racePath = Path.Combine(root, "cancel-before-open.bin");
        await File.WriteAllBytesAsync(racePath, [4, 5, 6]);
        using var raceToken = new CancellationTokenSource();
        using var raceLock = new HeldOplock(racePath);
        using var entered = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var race = Task.Run(() => SynchronousFileIo.Run(() =>
        {
            entered.Set();
            proceed.Wait();
            using var file = HeldOplock.Open(racePath);
            if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            return true;
        }, raceToken.Token));
        try
        {
            check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), "analysis I/O: cancellation-race control entered synchronous scope");
            raceToken.Cancel();
            await Task.Delay(150); // Cancellation initially finds no pending I/O.
            proceed.Set();
            check(await Task.Run(() => raceLock.Broken.WaitOne(TimeSpan.FromSeconds(3))), "analysis I/O: native open starts after cancellation was requested");
            try { await race.WaitAsync(TimeSpan.FromSeconds(3)); check(false, "analysis I/O: repeated cancellation closes the pre-request race"); }
            catch (OperationCanceledException) { check(true, "analysis I/O: repeated cancellation closes the pre-request race"); }
        }
        finally
        {
            proceed.Set();
            raceLock.Dispose();
            try { await race; }
            catch (OperationCanceledException) { }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "observations.json"), System.Text.Json.JsonSerializer.Serialize(observations));
        Console.WriteLine("Analyze synchronous I/O evidence: " + root);
    }

    private sealed class HeldOplock : IDisposable
    {
        private readonly SafeFileHandle _file;
        private IntPtr _overlapped;
        public EventWaitHandle Broken { get; } = new(false, EventResetMode.ManualReset);

        public HeldOplock(string path)
        {
            _file = Open(path);
            if (_file.IsInvalid) { var error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error); }
            _overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
            Marshal.StructureToPtr(new Overlapped { Event = Broken.SafeWaitHandle.DangerousGetHandle() }, _overlapped, false);
            if (DeviceIoControl(_file, 0x90000, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, _overlapped) || Marshal.GetLastWin32Error() != 997)
            { var error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error, "Authored oplock was not granted as pending."); }
        }

        public static SafeFileHandle Open(string path) => CreateFileW(path, 0x80000000, 7, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);

        public void Dispose()
        {
            if (_overlapped != IntPtr.Zero)
            {
                CancelIoEx(_file, _overlapped);
                GetOverlappedResult(_file, _overlapped, out _, true);
                _file.Dispose();
                Marshal.FreeHGlobal(_overlapped);
                _overlapped = IntPtr.Zero;
            }
            else _file.Dispose();
            Broken.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Overlapped
        {
            public UIntPtr Internal, InternalHigh;
            public uint Offset, OffsetHigh;
            public IntPtr Event;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputBytes, IntPtr output, uint outputBytes, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
    }
}
