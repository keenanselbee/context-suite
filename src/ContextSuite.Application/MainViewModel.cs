using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;

namespace ContextSuite.Application;

internal sealed class MainViewModel(WorkerClient worker) : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Queue<(OperationRequest Request, FileRow[] Rows)> _pending = new();
    private readonly HashSet<Guid> _received = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _active;
    private Task? _running;
    private int _batch;
    private string _summary = "Select files in Explorer and choose Analyze, Convert, or Optimize.";
    private ICommand? _cancelCommand;
    public ObservableCollection<FileRow> Rows { get; } = [];
    public bool IsBusy => _running is { IsCompleted: false };
    public string Summary { get => _summary; private set { _summary = value; Changed(); } }
    public ICommand CancelCommand => _cancelCommand ??= new CancelPendingCommand(this);
    public event PropertyChangedEventHandler? PropertyChanged;

    public ActivationReply Admit(OperationRequest? request)
    {
        if (request is null) return new(1, Guid.Empty, true, "Application opened.");
        request.Validate();
        if (_received.Contains(request.RequestId)) return new(1, request.RequestId, true, "Already received.");
        // Bound both pending work and retained UI history. Never silently discard selections.
        if (_lifetime.IsCancellationRequested || Rows.Count + request.Paths.Length > 16384 || _received.Count >= 1024)
            return new(1, request.RequestId, false, "The session queue is full. Close it after work finishes and try again.");
        _received.Add(request.RequestId);
        var batchId = ++_batch;
        var rows = request.Paths.Select(path => new FileRow(batchId, request.Operation, path)).ToArray();
        foreach (var row in rows) Rows.Add(row);
        _pending.Enqueue((request, rows));
        if (!IsBusy) _running = DrainAsync();
        return new(1, request.RequestId, true, "Selection received.");
    }

    private async Task DrainAsync()
    {
        await Task.Yield();
        while (_pending.TryDequeue(out var batch))
        {
            using var active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _active = active;
            try
            {
                Summary = $"Checking available media implementations for {batch.Rows.Length} files…";
                foreach (var row in batch.Rows) row.Status = "Checking capabilities";
                var capabilities = await worker.GetCapabilitiesAsync(active.Token);
                active.Token.ThrowIfCancellationRequested();
                foreach (var row in batch.Rows)
                    row.Status = capabilities.Length == 0 ? "Unsupported — not implemented" : "Planning not implemented";
            }
            catch (OperationCanceledException)
            {
                foreach (var row in batch.Rows) row.Status = "Cancelled";
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception or
                InvalidOperationException or System.Text.Json.JsonException)
            {
                foreach (var row in batch.Rows) row.Status = "Worker unavailable — retry selection";
            }
            finally { _active = null; }
        }
        Summary = $"{Rows.Count} files received across {_received.Count} batches. No files changed.";
        Changed(nameof(IsBusy));
    }

    private void CancelPending()
    {
        _active?.Cancel();
        while (_pending.TryDequeue(out var batch))
            foreach (var row in batch.Rows) row.Status = "Cancelled";
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        CancelPending();
        if (_running is not null) await _running;
        await worker.DisposeAsync();
        _lifetime.Dispose();
    }

    private sealed class CancelPendingCommand(MainViewModel owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { owner.CancelPending(); }
    }
}

internal sealed class FileRow(int batch, string operation, string path) : INotifyPropertyChanged
{
    private string _status = "Pending";
    public int Batch { get; set; } = batch;
    public string Operation { get; } = operation;
    public string Path { get; } = path;
    public string Status
    {
        get => _status;
        set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
