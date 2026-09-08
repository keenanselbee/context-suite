using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Images;
using ContextSuite.Core.Dds;

namespace ContextSuite.Application;

internal sealed class MainViewModel(WorkerClient worker, SuiteSettings? settings = null, OutputPublisher? publisher = null,
    LocalTrialStore? trial = null) : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Queue<(OperationRequest Request, FileRow[] Rows)> _pending = new();
    private readonly HashSet<Guid> _received = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _active;
    private Task? _running;
    private int _batch;
    private string _summary = "Select files in Explorer and choose Analyze, Convert, or Optimize.";
    private ICommand? _cancelCommand;
    private ICommand? _settingsCommand;
    public ObservableCollection<FileRow> Rows { get; } = [];
    public SuiteSettings Settings { get; set; } = settings ?? new();
    internal OutputPublisher? Publisher { get; } = publisher;
    public string RecoveryNotice { get; } = RecoveryMessage(publisher);
    public bool IsBusy => _running is { IsCompleted: false };
    public string Summary { get => _summary; private set { _summary = value; Changed(); } }
    public ICommand CancelCommand => _cancelCommand ??= new CancelPendingCommand(this);
    public ICommand SettingsCommand => _settingsCommand ??= new OpenSettingsCommand(this);
    public event Action<string>? SettingsRequested;
    public event Func<ConversionViewModel, CancellationToken, Task<ConfirmedImageBatch?>>? ConversionRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ActivationReply Admit(OperationRequest? request)
    {
        if (request is null) return new(1, Guid.Empty, true, "Application opened.");
        request.Validate();
        if (request.IsSettingsRequest)
        {
            SettingsRequested?.Invoke(request.Operation);
            return new(1, request.RequestId, true, "Settings opened.");
        }
        if (_received.Contains(request.RequestId)) return new(1, request.RequestId, true, "Already received.");
        // Bound both pending work and retained UI history. Never silently discard selections.
        if (_lifetime.IsCancellationRequested || Rows.Count + request.Paths.Length > 16384 || _received.Count >= 1024)
            return new(1, request.RequestId, false, "The session queue is full. Close it after work finishes and try again.");
        _received.Add(request.RequestId);
        var batchId = ++_batch;
        var snapshot = Settings.Capture(request.Operation);
        var rows = request.Paths.Select(path => new FileRow(batchId, request.Operation, path, snapshot)).ToArray();
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
                if (batch.Request.Operation == "analyze")
                {
                    await AnalyzeBatchAsync(batch.Rows, active.Token);
                    continue;
                }
                if (batch.Request.Operation == "convert" && Publisher is not null && trial is not null && ConversionRequested is not null)
                {
                    await ConvertBatchAsync(batch.Request, batch.Rows, active.Token);
                    continue;
                }
                Summary = $"Checking available media implementations for {batch.Rows.Length} files…";
                foreach (var row in batch.Rows) row.ApplyResult(new(row.Path, OperationState.Running, "Checking capabilities"));
                var capabilities = await worker.GetCapabilitiesAsync(active.Token);
                active.Token.ThrowIfCancellationRequested();
                foreach (var row in batch.Rows)
                    row.ApplyResult(new(row.Path, OperationState.Unsupported,
                        capabilities.Any(capability => capability.Operation == batch.Request.Operation) ? "Planning unavailable" : "Unsupported — not implemented"));
            }
            catch (OperationCanceledException)
            {
                foreach (var row in batch.Rows)
                    if (row.Result.State is OperationState.Pending or OperationState.Running) row.ApplyResult(new(row.Path, OperationState.Cancelled, "Cancelled"));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception or
                InvalidOperationException or System.Text.Json.JsonException)
            {
                foreach (var row in batch.Rows)
                    if (row.Result.Publication?.IsCommitted != true) row.ApplyResult(new(row.Path, OperationState.Failed, "Worker unavailable — retry selection"));
            }
            finally { _active = null; }
        }
        RefreshSummary();
        Changed(nameof(IsBusy));
    }

    internal async Task WaitForIdleAsync()
    {
        if (_running is not null) await _running;
    }

    private async Task AnalyzeBatchAsync(FileRow[] rows, CancellationToken cancellationToken)
    {
        for (var index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            Summary = $"Reading DDS header {index + 1} of {rows.Length}. No files changed.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Reading DDS header"));
            try
            {
                var facts = await DdsParser.ReadAsync(row.Path, cancellationToken);
                row.ApplyAnalysis(facts);
            }
            catch (InvalidDataException)
            { row.ApplyResult(new(row.Path, OperationState.Unsupported, "Not a supported DDS header. No files changed.")); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { row.ApplyResult(new(row.Path, OperationState.Failed, "Could not read this file. Check access and retry.")); }
        }
    }

    private async Task ConvertBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken cancellationToken)
    {
        var selection = new List<ConversionSelection>();
        for (var index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            Summary = $"Reading image {index + 1} of {rows.Length}. No conversion has been confirmed.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Reading image properties"));
            try
            {
                var facts = await worker.ProbeAsync(new(row.ItemId, row.Path), cancellationToken);
                selection.Add(new(row.ItemId, row.Path, facts, null));
                row.ApplyResult(new(row.Path, OperationState.Pending, "Waiting for conversion choices"));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                var message = error is MediaWorkerException ? error.Message : "Image could not be read. Check the file and retry.";
                selection.Add(new(row.ItemId, row.Path, null, message));
                row.ApplyResult(new(row.Path, error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
                    ? OperationState.Unsupported : OperationState.Failed, message));
            }
        }
        if (!selection.Any(s => s.Facts is not null)) return;
        await using var planner = new ConversionViewModel(worker, trial!, request.RequestId, selection, rows[0].Settings, PublicationSupport.ReplacementAvailable);
        Summary = $"Review conversion choices for batch {rows[0].Batch}. No files changed by this batch.";
        var confirmed = await ConversionRequested!(planner, cancellationToken);
        if (confirmed is null)
        {
            foreach (var row in rows.Where(r => r.Result.State == OperationState.Pending))
                row.ApplyResult(new(row.Path, OperationState.Cancelled, "Conversion cancelled before confirmation"));
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var byId = rows.ToDictionary(row => row.ItemId);
        var completed = rows.Count(r => r.Result.State is OperationState.Failed or OperationState.Unsupported);
        await new ImageBatchExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (item, result) =>
        {
            byId[item.Source.ItemId].ApplyResult(result);
            if (result.State != OperationState.Running) completed++;
            Summary = $"Batch {rows[0].Batch}: {completed} of {rows.Length} finished. {result.Message}";
        }, cancellationToken);
    }

    private void CancelPending()
    {
        _active?.Cancel();
        while (_pending.TryDequeue(out var batch))
            foreach (var row in batch.Rows)
                if (row.Result.Publication?.IsCommitted != true) row.ApplyResult(new(row.Path, OperationState.Cancelled, "Cancelled"));
        RefreshSummary();
    }

    internal void RecordResult(FileRow row, FileResult result)
    {
        if (!Rows.Contains(row)) throw new InvalidDataException("The result does not belong to this queue.");
        row.ApplyResult(result);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var prefix = $"{Rows.Count} files received across {_received.Count} batches.";
        var noChanges = Rows.All(r => r.Result.Publication?.IsCommitted != true);
        var completed = Rows.Count(r => r.Result.State == OperationState.Succeeded);
        var warnings = Rows.Count(r => r.Result.Publication?.HasWarning == true);
        var failed = Rows.Count(r => r.Result.State == OperationState.Failed);
        var cancelled = Rows.Count(r => r.Result.State == OperationState.Cancelled);
        var unchanged = Rows.Count(r => r.Result.State == OperationState.Unchanged);
        var unsupported = Rows.Count(r => r.Result.State == OperationState.Unsupported);
        var pending = Rows.Count - completed - failed - cancelled - unchanged - unsupported;
        Summary = $"{prefix} {completed} completed ({warnings} warnings), {unchanged} unchanged, {failed} failed, " +
            $"{cancelled} cancelled, {unsupported} unsupported, {pending} pending." + (noChanges ? " No files changed." : "");
    }

    private static string RecoveryMessage(OutputPublisher? publisher)
    {
        if (publisher is null) return "";
        try
        {
            var count = publisher.FindRecoveryRecords().Count;
            return count == 0 ? "" : $"{count} publication recovery record(s) retained at {publisher.RecordDirectory}. " +
                "Originals and temporary files were not automatically removed. Review these records before cleanup.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { return "Publication recovery records could not be checked. No originals or temporary files were removed."; }
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

    private sealed class OpenSettingsCommand(MainViewModel owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { owner.SettingsRequested?.Invoke("convert"); }
    }
}

internal sealed class FileRow(int batch, string operation, string path, BatchSettings settings) : INotifyPropertyChanged
{
    public Guid ItemId { get; } = Guid.NewGuid();
    public int Batch { get; set; } = batch;
    public string Operation { get; } = operation;
    public string Path { get; } = path;
    public BatchSettings Settings { get; } = settings;
    public FileResult Result { get; private set; } = new(path, OperationState.Pending, "Pending");
    public DdsInfo? Analysis { get; private set; }
    public string Status => Result.Message;
    public string OutputPath => Result.Publication?.OutputPath ?? "";
    public string RetainedOriginalPath => Result.Publication?.RetainedOriginalPath ?? "";
    public string RecoveryRecordPath => Result.Publication?.RecoveryRecordPath ?? "";
    public string ResultDetails => $"Source: {Path}\n{Status}" +
        (OutputPath.Length == 0 ? "" : $"\nOutput: {OutputPath}") +
        (RetainedOriginalPath.Length == 0 ? "" : $"\nRetained original: {RetainedOriginalPath}") +
        (RecoveryRecordPath.Length == 0 ? "" : $"\nRecovery record: {RecoveryRecordPath}") + AnalysisDetails;

    private string AnalysisDetails => Analysis is not { } dds ? "" :
        $"\nHeader: {(dds.HasDx10Header ? "DX10 extended" : "Legacy DDS")}; FourCC: 0x{dds.RawFourCc:X8}; DXGI: {dds.RawDxgiFormat}" +
        $"\nFormat: {dds.Format}; structure: {dds.Kind}; dimensions: {dds.Width} x {dds.Height} x {dds.Depth}; array count: {dds.ArraySize}; mip levels: {dds.MipLevels}" +
        $"\nColor interpretation: {(dds.IsSrgb ? "sRGB explicitly declared" : dds.IsTypeless ? "Typeless; typed interpretation required" : "No sRGB declaration; not proof of authored linear color or texture purpose")}" +
        $"\nAlpha mode: {(Enum.IsDefined((DdsAlphaMode)dds.RawAlphaMode) ? ((DdsAlphaMode)dds.RawAlphaMode).ToString() : "Unrecognized")} ({dds.RawAlphaMode})" +
        $"\nFile size: {dds.FileBytes:N0} bytes; expected payload: {(dds.ExpectedPayloadBytes is { } size ? $"{size:N0} bytes" : "Unavailable for this layout")}" +
        "\nHeader analysis only; no pixels decoded and no files changed. Conversion separately validates supported 2D textures." +
        string.Concat(dds.Warnings.Select(warning => "\nWarning: " + warning));

    public void ApplyAnalysis(DdsInfo facts)
    {
        Analysis = facts;
        ApplyResult(new(Path, OperationState.Succeeded, $"{facts.Format} {facts.Width} x {facts.Height}; {facts.MipLevels} mip(s)" +
            (facts.Warnings.IsEmpty ? "" : $"; {facts.Warnings.Length} warning(s)")));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Analysis)));
    }

    public void ApplyResult(FileResult result)
    {
        if (!string.Equals(System.IO.Path.GetFullPath(result.Path), System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The file result belongs to a different source.");
        Result = result;
        foreach (var property in new[] { nameof(Result), nameof(Status), nameof(OutputPath), nameof(RetainedOriginalPath), nameof(RecoveryRecordPath), nameof(ResultDetails) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
