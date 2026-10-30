using ContextSuite.Private.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Private.Audio;

internal static class ProcessContracts
{
    public static int Child(string mode, string signal)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var handle = new SafeFileHandle(GetStdHandle(-10), false);
        using var input = new FileStream(handle, FileAccess.Read);
        if (!input.CanSeek || !IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out var inJob) || !inJob) return 2;
        input.Seek(-1, SeekOrigin.End);
        if (input.ReadByte() != 255) return 3;
        input.Seek(0, SeekOrigin.Begin);
        if (input.ReadByte() != 17) return 4;
        if (WriteFile(handle, [99], 1, out _, IntPtr.Zero) || Marshal.GetLastWin32Error() != 5) return 7;
        if (mode == "inspect") { Console.Write("seekable-read-only-job-input"); return 0; }
        if (mode == "exit") return int.Parse(signal, System.Globalization.CultureInfo.InvariantCulture);
        if (mode == "echo") { Console.OpenStandardOutput().Write(Encoding.UTF8.GetBytes(signal)); return 0; }
        if (mode == "flood") { Console.Write(new string('x', 100000)); return 0; }
        if (mode != "wait") return 5;
        using var ready = EventWaitHandle.OpenExisting(signal);
        ready.Set();
        Thread.Sleep(30000);
        return 6;
    }

    public static async Task RunAsync(string root, List<string> checks)
    {
        var source = Path.Combine(root, "inherited-input ü.tmp");
        await File.WriteAllBytesAsync(source, [17, 33, 255]);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path.");
        async Task<byte[]> Run(string mode, int limit, CancellationToken token, string signal = "unused", Func<int, Exception>? exitFailure = null)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await SeekableMediaProcess.RunAsync(executable, root, input, ["--process-fixture", mode, signal], limit, token, exitFailure);
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await Run("inspect", 1024, deadline.Token);
        if (Encoding.UTF8.GetString(result) != "seekable-read-only-job-input") throw new Exception("Inherited seekable input or job assignment failed.");
        checks.Add("native launcher: seekable inherited input and job membership before fixture work");
        foreach (var literal in new[] { "", "two words", "quotes \" and trailing\\", "Unicode \u00fc \ud83d\ude00\r\nsecond line\n\ttab -i file:ignored & $(ignored)" })
        {
            if (Encoding.UTF8.GetString(await Run("echo", 4096, deadline.Token, literal)) != literal)
                throw new Exception("Native argument boundaries or metadata text changed.");
            checks.Add("native launcher: literal quoted metadata round-trip");
        }
        try { await Run("echo", 4096, deadline.Token, "nul\0suffix"); throw new Exception("NUL truncated metadata argument."); }
        catch (ArgumentException) { checks.Add("native launcher: NUL arguments rejected"); }
        try { await Run("echo", 4096, deadline.Token, new string('x', 32767)); throw new Exception("Command line exceeded its native budget."); }
        catch (InvalidDataException) { checks.Add("native launcher: command-line length rejected before process creation"); }
        try { await Run("flood", 16, deadline.Token); throw new Exception("Native output cap ignored."); }
        catch (InvalidDataException) { checks.Add("native launcher: output limit terminates and awaits owned child"); }
        var eventName = "Local\\ContextSuite.Audio.Test." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = Run("wait", 1024, cancel.Token, eventName,
            _ => throw new Exception("Exit mapping replaced active cancellation."));
        if (!await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(5)))) throw new Exception("Owned child never reached its running phase.");
        var timer = Stopwatch.StartNew();
        cancel.Cancel();
        try { await running; throw new Exception("Running native child ignored cancellation."); }
        catch (OperationCanceledException)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("Native cancellation did not promptly await child exit.");
            checks.Add("native launcher: cancellation after child starts terminates and awaits it");
        }
        if (!(await File.ReadAllBytesAsync(source)).SequenceEqual(new byte[] { 17, 33, 255 })) throw new Exception("Inherited input changed.");
        checks.Add("native launcher: input bytes unchanged across success, limits and cancellation");
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint bytes, out uint written, IntPtr overlapped);
}
