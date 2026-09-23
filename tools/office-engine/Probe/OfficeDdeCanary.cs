using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Disposable data-only DDE service. No execute, poke, shell, file or network action.
// Each instance registers a random name and answers only one fixed topic/item.
internal sealed class OfficeDdeCanary : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId, _instance;
    private IntPtr _service, _topic, _item;
    private int _connections, _requests, _callbackFailure;
    private Exception? _failure;
    private bool _disposed;
    internal string Name { get; } = "ContextSuiteDde" + Guid.NewGuid().ToString("N");
    internal const string Topic = "AuthoredData";
    internal const string Item = "VALUE";
    internal const string Value = "987654";
    internal (int Connections, int Requests) Counts => (Volatile.Read(ref _connections), Volatile.Read(ref _requests));

    internal OfficeDdeCanary()
    {
        _thread = new Thread(Serve) { IsBackground = true, Name = "Context Suite authored DDE control" };
        _thread.Start();
        try { _ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }

    private void Serve()
    {
        Callback callback = Receive;
        nuint timer = 0;
        try
        {
            _threadId = GetCurrentThreadId();
            // Refuse execute, poke and advise at the DDEML boundary as well.
            var error = DdeInitializeW(ref _instance, callback, 0x1c000, 0);
            if (error != 0) throw new IOException("DDE control initialization failed: " + error);
            _service = StringHandle(_instance, Name); _topic = StringHandle(_instance, Topic); _item = StringHandle(_instance, Item);
            if (DdeNameService(_instance, _service, IntPtr.Zero, 1) == IntPtr.Zero)
                throw new IOException("DDE control service registration failed.");
            timer = SetTimer(IntPtr.Zero, 0, 600000, IntPtr.Zero);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _ready.SetResult();
            while (true)
            {
                var status = GetMessageW(out var message, IntPtr.Zero, 0, 0);
                if (status == 0) break;
                if (status < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (message.Id == 0x113 && message.WParam == timer) throw new TimeoutException("DDE control lifetime exceeded.");
                TranslateMessage(ref message); DispatchMessageW(ref message);
            }
        }
        catch (Exception error) { _failure = error; _ready.TrySetException(error); }
        finally
        {
            if (timer != 0) KillTimer(IntPtr.Zero, timer);
            if (_instance != 0)
            {
                if (_service != IntPtr.Zero) DdeNameService(_instance, _service, IntPtr.Zero, 2);
                foreach (var handle in new[] { _service, _topic, _item })
                    if (handle != IntPtr.Zero) DdeFreeStringHandle(_instance, handle);
                if (!DdeUninitialize(_instance)) _failure ??= new IOException("DDE control cleanup failed.");
            }
            GC.KeepAlive(callback);
        }
    }

    private IntPtr Receive(uint type, uint format, IntPtr conversation, IntPtr first, IntPtr second, IntPtr data, nuint one, nuint two)
    {
        try
        {
            if (type == 0x1062 && DdeCmpStringHandles(first, _topic) == 0 && DdeCmpStringHandles(second, _service) == 0)
            {
                Interlocked.Increment(ref _connections);
                return (IntPtr)1;
            }
            if (type == 0x20b0 && DdeCmpStringHandles(first, _topic) == 0 && DdeCmpStringHandles(second, _item) == 0)
            {
                Interlocked.Increment(ref _requests);
                if (format is not (1 or 13)) return IntPtr.Zero;
                var bytes = (format == 13 ? Encoding.Unicode : Encoding.ASCII).GetBytes(Value + "\0");
                return DdeCreateDataHandle(_instance, bytes, (uint)bytes.Length, 0, second, format, 0);
            }
            return IntPtr.Zero;
        }
        catch { Interlocked.Exchange(ref _callbackFailure, 1); return IntPtr.Zero; }
    }

    internal string Query()
    {
        if (_disposed || !_thread.IsAlive || _failure is not null || Volatile.Read(ref _callbackFailure) != 0)
            throw new IOException("DDE control is not healthy.", _failure);
        uint instance = 0;
        Callback callback = (_, _, _, _, _, _, _, _) => IntPtr.Zero;
        var handles = new List<IntPtr>();
        IntPtr conversation = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            var error = DdeInitializeW(ref instance, callback, 0x10, 0);
            if (error != 0) throw new IOException("DDE client control initialization failed: " + error);
            foreach (var value in new[] { Name, Topic, Item }) handles.Add(StringHandle(instance, value));
            conversation = DdeConnect(instance, handles[0], handles[1], IntPtr.Zero);
            if (conversation == IntPtr.Zero) throw new IOException("DDE positive-control connection failed.");
            data = DdeClientTransaction(IntPtr.Zero, 0, conversation, handles[2], 13, 0x20b0, 2000, out _);
            if (data == IntPtr.Zero) throw new IOException("DDE positive-control request failed.");
            var length = DdeGetData(data, null, 0, 0);
            if (length is < 2 or > 128 || length % 2 != 0) throw new IOException("DDE control response length.");
            var bytes = new byte[length];
            if (DdeGetData(data, bytes, length, 0) != length) throw new IOException("Incomplete DDE control response.");
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            if (data != IntPtr.Zero) DdeFreeDataHandle(data);
            if (conversation != IntPtr.Zero) DdeDisconnect(conversation);
            foreach (var handle in handles) DdeFreeStringHandle(instance, handle);
            if (instance != 0) DdeUninitialize(instance);
            GC.KeepAlive(callback);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thread.IsAlive)
        {
            if (!PostThreadMessageW(_threadId, 0x12, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!_thread.Join(TimeSpan.FromSeconds(5))) throw new IOException("DDE control thread did not stop.");
        }
        if (_failure is not null || Volatile.Read(ref _callbackFailure) != 0)
            throw new IOException("DDE control failed.", _failure);
    }

    private static IntPtr StringHandle(uint instance, string text)
    {
        var result = DdeCreateStringHandleW(instance, text, 1200);
        return result != IntPtr.Zero ? result : throw new IOException("DDE control string handle failed.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr Callback(uint type, uint format, IntPtr conversation, IntPtr first, IntPtr second, IntPtr data, nuint one, nuint two);
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public IntPtr Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint DdeInitializeW(ref uint instance, Callback callback, uint flags, uint reserved);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern IntPtr DdeCreateStringHandleW(uint instance, string text, int codePage);
    [DllImport("user32.dll")] private static extern IntPtr DdeNameService(uint instance, IntPtr service, IntPtr reserved, uint flags);
    [DllImport("user32.dll")] private static extern int DdeCmpStringHandles(IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern IntPtr DdeCreateDataHandle(uint instance, byte[] bytes, uint count, uint offset, IntPtr item, uint format, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr DdeConnect(uint instance, IntPtr service, IntPtr topic, IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr DdeClientTransaction(IntPtr bytes, uint count, IntPtr conversation, IntPtr item, uint format, uint type, uint timeout, out uint result);
    [DllImport("user32.dll")] private static extern uint DdeGetData(IntPtr data, byte[]? bytes, uint count, uint offset);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DdeFreeDataHandle(IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DdeDisconnect(IntPtr conversation);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DdeFreeStringHandle(uint instance, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DdeUninitialize(uint instance);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessageW(out Message message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(IntPtr window, nuint id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(IntPtr window, nuint timer);
}
