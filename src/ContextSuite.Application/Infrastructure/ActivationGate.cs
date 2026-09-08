using System.Diagnostics;
using System.Windows.Threading;
using ContextSuite.Runtime;

namespace ContextSuite.Application.Infrastructure;

// Serialize launcher handoff with automatic idle shutdown, not media work. Mutex
// ownership is thread-affine: acquire, await and release on the same WPF dispatcher.
// A terminated launcher abandons the mutex; the next owner can safely acquire it.
internal sealed class ActivationGate : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Mutex _mutex = new(false, LocalPipe.SessionName + "-handoff",
        new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true });
    private bool _held;

    public bool TryEnter()
    {
        _dispatcher.VerifyAccess();
        if (_held) return true;
        try { _held = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _held = true; }
        return _held;
    }

    public async Task EnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        while (!TryEnter())
        {
            if (timer.Elapsed >= TimeSpan.FromSeconds(15)) throw new IOException("Another application handoff did not finish.");
            await Task.Delay(25, cancellationToken);
        }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_held) { _mutex.ReleaseMutex(); _held = false; }
        _mutex.Dispose();
    }
}
