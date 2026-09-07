using System.Runtime.InteropServices;

namespace ContextSuite.Application.Infrastructure;

// No ordinary DeleteFile fallback. Kept behind the publication verification gate.
internal sealed class WindowsFileRecycler : IFileRecycler
{
    public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = CoInitializeEx(0, 2);
            try
            {
                Marshal.ThrowExceptionForHR(initialized);
                completion.SetResult(Recycle(path, expected, cancellationToken));
            }
            catch (Exception error) when (error is COMException or IOException or InvalidDataException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                completion.SetResult(new(false, "Original retained: Windows recycling was unavailable or cancelled."));
            }
            finally { if (initialized >= 0) CoUninitialize(); }
        }) { IsBackground = true, Name = "Context Suite recycle operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static RecycleResult Recycle(string path, FileFingerprint expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = PublicationFiles.Normalize(path);
        if (!PublicationFiles.IsLocalNtfs(path)) return new(false, "Original retained: recycling is not verified for this location.");
        var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!;
        var operation = (IRecycleFileOperation)Activator.CreateInstance(type)!;
        IShellRecycleItem? item = null;
        using var sink = new RecycleProgressSink(path, expected, cancellationToken);
        try
        {
            var iid = typeof(IShellRecycleItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, ref iid, out item));
            // SILENT | NOERRORUI | EARLYFAILURE | RECYCLEONDELETE. Never YES-TO-ALL or elevation.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x0004 | 0x0400 | 0x00100000 | 0x00080000));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
            var result = operation.PerformOperations();
            var abortedResult = operation.GetAnyOperationsAborted(out var aborted);
            // A per-item successful recycle remains completed even if cancellation arrives afterwards.
            if (sink.Recycled) return new(true, "Original moved to Recycle Bin.", sink.RecycledPath);
            return new(false, result < 0 || abortedResult < 0 || aborted
                ? "Original retained or recovery requires review: Windows recycling failed or was cancelled."
                : "Original retained or recovery requires review: Windows did not confirm recycling.");
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellRecycleItem item);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleProgressSink : IRecycleProgressSink, IDisposable
{
    private const int Abort = unchecked((int)0x80004004);
    private readonly string _path;
    private readonly FileFingerprint _expected;
    private readonly CancellationToken _cancellation;
    private FileStream? _lease;
    private bool _approved;
    public bool Recycled { get; private set; }
    public string? RecycledPath { get; private set; }
    public bool RefusedPermanentDelete { get; private set; }

    internal RecycleProgressSink(string path, FileFingerprint expected, CancellationToken cancellation)
    {
        _path = path;
        _expected = expected;
        _cancellation = cancellation;
    }

    // A direct contract tests the permanent-delete rejection without deleting a file.
    public int PreDeleteItem(uint flags, nint item)
    {
        if ((flags & 0x80) == 0) { RefusedPermanentDelete = true; return Abort; }
        if (_cancellation.IsCancellationRequested || item == 0) return Abort;
        try
        {
            var path = ReadPath(item);
            if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return Abort;
            _lease?.Dispose();
            _lease = PublicationFiles.OpenRead(_path, allowRename: true);
            if (PublicationFiles.Fingerprint(_lease) != _expected) return Abort;
            _approved = true;
            return 0;
        }
        catch (Exception error) when (error is COMException or IOException or InvalidDataException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return Abort; }
    }

    public int PostDeleteItem(uint flags, nint item, int result, nint recycledItem)
    {
        Recycled = _approved && result >= 0 && recycledItem != 0;
        if (Recycled)
        {
            try { RecycledPath = ReadPath(recycledItem); }
            catch (COMException) { /* A confirmed Shell item need not expose a filesystem path. */ }
        }
        return 0;
    }

    private static string ReadPath(nint item)
    {
        var shellItem = (IShellRecycleItem)Marshal.GetObjectForIUnknown(item);
        try
        {
            Marshal.ThrowExceptionForHR(shellItem.GetDisplayName(0x80058000, out var text));
            try { return Marshal.PtrToStringUni(text) ?? ""; }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally { Marshal.ReleaseComObject(shellItem); }
    }

    public void Dispose() { _lease?.Dispose(); }
    public int StartOperations() => _cancellation.IsCancellationRequested ? Abort : 0;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint flags, nint item, nint name) => Abort;
    public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
    public int PreMoveItem(uint flags, nint item, nint destination, nint name) => Abort;
    public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
    public int PreCopyItem(uint flags, nint item, nint destination, nint name) => Abort;
    public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created) => 0;
    public int PreNewItem(uint flags, nint destination, nint name) => Abort;
    public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
    public int UpdateProgress(uint total, uint completed) => _cancellation.IsCancellationRequested ? Abort : 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}

// Signatures/vtable order follow Windows SDK 26100 shobjidl_core.h. Unused pointers stay opaque.
[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellRecycleItem
{
    [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
    [PreserveSig] int GetParent(out nint parent);
    [PreserveSig] int GetDisplayName(uint kind, out nint text);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(nint other, uint hint, out int order);
}

[ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecycleProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, nint item, nint name);
    [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint created);
    [PreserveSig] int PreMoveItem(uint flags, nint item, nint destination, nint name);
    [PreserveSig] int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint created);
    [PreserveSig] int PreCopyItem(uint flags, nint item, nint destination, nint name);
    [PreserveSig] int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint created);
    [PreserveSig] int PreDeleteItem(uint flags, nint item);
    [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint recycledItem);
    [PreserveSig] int PreNewItem(uint flags, nint destination, nint name);
    [PreserveSig] int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created);
    [PreserveSig] int UpdateProgress(uint total, uint completed);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRecycleFileOperation
{
    [PreserveSig] int Advise(IRecycleProgressSink sink, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOperationFlags(uint flags);
    [PreserveSig] int SetProgressMessage(nint message);
    [PreserveSig] int SetProgressDialog(nint dialog);
    [PreserveSig] int SetProperties(nint properties);
    [PreserveSig] int SetOwnerWindow(nint owner);
    [PreserveSig] int ApplyPropertiesToItem(nint item);
    [PreserveSig] int ApplyPropertiesToItems(nint items);
    [PreserveSig] int RenameItem(nint item, nint name, nint sink);
    [PreserveSig] int RenameItems(nint items, nint name);
    [PreserveSig] int MoveItem(nint item, nint destination, nint name, nint sink);
    [PreserveSig] int MoveItems(nint items, nint destination);
    [PreserveSig] int CopyItem(nint item, nint destination, nint name, nint sink);
    [PreserveSig] int CopyItems(nint items, nint destination);
    [PreserveSig] int DeleteItem(IShellRecycleItem item, IRecycleProgressSink sink);
    [PreserveSig] int DeleteItems(nint items);
    [PreserveSig] int NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}
