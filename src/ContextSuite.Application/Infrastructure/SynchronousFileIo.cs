using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal static class SynchronousFileIo
{
    // The delegate must remain synchronous. Stop cancellation before this thread
    // can return to the pool or execute another caller's I/O. Awaiting a timeout
    // instead would abandon the operation and any handle it eventually opens.
    public static T Run<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var thread = OpenThread(1, false, GetCurrentThreadId()); // THREAD_TERMINATE, required by CancelSynchronousIo
        if (thread.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var sync = new object();
        var active = true;
        using var timer = new Timer(_ =>
        {
            lock (sync)
            {
                if (active) CancelSynchronousIo(thread);
            }
        }, null, Timeout.Infinite, Timeout.Infinite);
        // Repeat to cover cancellation immediately before the native request is
        // issued. ERROR_NOT_FOUND is expected between requests. Drivers decide
        // when cancellation completes; the caller still waits for actual return.
        using var registration = cancellationToken.UnsafeRegister(_ => timer.Change(0, 50), null);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return operation();
        }
        catch (Exception error) when ((error is IOException or Win32Exception) && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("File inspection was canceled.", error, cancellationToken);
        }
        finally
        {
            lock (sync) active = false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelSynchronousIo(SafeWaitHandle thread);
}
