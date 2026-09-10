using System.Collections.ObjectModel;
using System.Collections.Immutable;
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
    IOperationAccess? trial = null) : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly Queue<(OperationRequest Request, FileRow[] Rows)> _pending = new();
    private readonly HashSet<Guid> _received = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _active;
    private Task? _running;
    private bool _busy;
    private int _batch;
    private string _summary = "Select files in Explorer and choose Analyze, Convert, or Optimize.";
    private ICommand? _cancelCommand;
    private ICommand? _settingsCommand;
    public ObservableCollection<FileRow> Rows { get; } = [];
    public SuiteSettings Settings { get; set; } = settings ?? new();
    internal OutputPublisher? Publisher { get; } = publisher;
    public string RecoveryNotice { get; } = RecoveryMessage(publisher);
    public bool IsBusy => _busy;
    public bool HasResults => Rows.Count != 0;
    public bool IsLanding => Rows.Count == 0;
    public IEnumerable<FileRow> DisplayRows => Rows.Where(row => !row.WasRetried);
    private IEnumerable<FileRow> RetryCandidates => Rows.Where(row => row.Operation is "convert" or "optimize")
        .GroupBy(row => row.Operation)
        .SelectMany(tool => tool.GroupBy(row => row.Path, StringComparer.OrdinalIgnoreCase).Select(file => file.Last()))
        .Where(row => row.Result.State == OperationState.Failed && row.Result.Publication?.IsCommitted != true);
    public bool CanRetry => !IsBusy && RetryCandidates.Any();
    public bool HasProblems => DisplayRows.Any(row => row.Result.State is OperationState.Failed or OperationState.Unsupported || row.Result.Publication?.HasWarning == true);
    public bool ShowDetails => DisplayRows.Any(row => row.Operation == "analyze" ||
        row.Result.State is OperationState.Failed or OperationState.Unsupported ||
        row.Result.Publication is { CleanupWarning: true } or { HasWarning: true, MetadataWarning: false });
    public string Summary { get => _summary; private set { _summary = value; Changed(); } }
    public ICommand CancelCommand => _cancelCommand ??= new CancelPendingCommand(this);
    public ICommand SettingsCommand => _settingsCommand ??= new OpenSettingsCommand(this);
    public event Action<string>? SettingsRequested;
    public event Func<ConversionViewModel, CancellationToken, Task<ConfirmedImageBatch?>>? ConversionRequested;
    public event Action<OperationRequest, FileRow[]>? QuickBatchStarted;
    public event Action<OperationRequest, FileRow[]>? QuickBatchCompleted;
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
        var rows = request.Paths.Select(path => new FileRow(batchId, request.Operation, path, snapshot, request.Action)).ToArray();
        foreach (var row in rows) Rows.Add(row);
        Changed(nameof(HasResults)); Changed(nameof(IsLanding)); Changed(nameof(DisplayRows));
        _pending.Enqueue((request, rows));
        if (!IsBusy) { _busy = true; Changed(nameof(IsBusy)); Changed(nameof(CanRetry)); _running = DrainAsync(); }
        return new(1, request.RequestId, true, "Selection received.");
    }

    private async Task DrainAsync()
    {
        await Task.Yield();
        while (_pending.TryDequeue(out var batch))
        {
            using var active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _active = active;
            if (batch.Request.IsQuickAction) QuickBatchStarted?.Invoke(batch.Request, batch.Rows);
            try
            {
                if (batch.Request.Operation == "analyze")
                {
                    await AnalyzeBatchAsync(batch.Rows, active.Token);
                    continue;
                }
                if (batch.Request.Operation == "convert" && Publisher is not null && trial is not null && (batch.Request.IsQuickConversion || ConversionRequested is not null))
                {
                    await ConvertBatchAsync(batch.Request, batch.Rows, active.Token);
                    continue;
                }
                if (batch.Request.Operation == "optimize" && Publisher is not null && trial is not null)
                {
                    await OptimizeBatchAsync(batch.Request, batch.Rows, active.Token);
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
            finally
            {
                _active = null;
                RefreshSummary();
                if (batch.Request.IsQuickAction) QuickBatchCompleted?.Invoke(batch.Request, batch.Rows);
            }
        }
        RefreshSummary();
        _busy = false;
        Changed(nameof(IsBusy)); Changed(nameof(CanRetry));
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
        ImageFormat? directTarget = request.IsQuickConversion ? request.Action switch
        {
            "png" => ImageFormat.Png, "jpeg" => ImageFormat.Jpeg, "webp" => ImageFormat.WebP,
            "bmp" => ImageFormat.Bmp, "tga" => ImageFormat.Tga, "dds" => ImageFormat.Dds, _ => throw new InvalidDataException("Unknown conversion target.")
        } : null;
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
                if (directTarget is not ImageFormat.Dds && directTarget == facts.Format && facts.UnsupportedReason is null)
                {
                    row.ApplyResult(new(row.Path, OperationState.Unchanged, $"Already {facts.Format}; original kept. Use Optimize for same-format processing."));
                    continue;
                }
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
        ConfirmedImageBatch? confirmed = null;
        if (directTarget is { } target)
        {
            planner.TargetFixed = true;
            planner.Target = target;
            // The menu target and immutable Settings choice confirm routine work.
            // Automatic metadata omission can still force a copy of this batch.
            planner.WebPLossless = planner.Target == ImageFormat.WebP;
            if (!planner.IsDdsWorkflow)
            {
                var plan = ImageConversionPlanner.Create(request.RequestId, selection.Where(s => s.Facts is not null).Select(s => s.Facts!),
                    new(target, WebPLossless: planner.WebPLossless, Metadata: ImageMetadataMode.Automatic), rows[0].Settings,
                    rows[0].Settings.Preferences.ReplaceOriginals && rows[0].Settings.Preferences.OutputDirectory is null && PublicationSupport.ReplacementAvailable);
                if (!plan.HasExecutableItems && !planner.NeedsBackgroundChoice)
                {
                    foreach (var item in plan.Items)
                        rows.First(row => row.ItemId == item.Source.ItemId).ApplyResult(new(item.Source.Path,
                            OperationState.Unsupported, item.BlockReason ?? "This file cannot use the selected format."));
                    return;
                }
                if (plan.CanConfirmQuickAction)
                    confirmed = plan.Confirm(true, plan.ReplaceOriginal, PublicationSupport.ReplacementAvailable);
            }
        }
        Summary = $"Review conversion choices for batch {rows[0].Batch}. No files changed by this batch.";
        if (confirmed is null && ConversionRequested is not null)
            confirmed = await ConversionRequested(planner, cancellationToken);
        if (confirmed is null)
        {
            foreach (var row in rows.Where(r => r.Result.State == OperationState.Pending))
                row.ApplyResult(new(row.Path, OperationState.Cancelled, "Conversion cancelled before confirmation"));
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var byId = rows.ToDictionary(row => row.ItemId);
        var completed = rows.Count(r => r.Result.State is OperationState.Failed or OperationState.Unsupported or OperationState.Unchanged);
        await new ImageBatchExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (item, result) =>
        {
            byId[item.Source.ItemId].ApplyResult(result);
            if (result.State != OperationState.Running) completed++;
            Summary = $"Converting: {completed} of {rows.Length} finished.";
        }, cancellationToken);
    }

    private async Task OptimizeBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken cancellationToken)
    {
        var selection = new List<ConversionSelection>();
        for (var index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            Summary = $"Checking PNG {index + 1} of {rows.Length}.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking PNG support"));
            try
            {
                var facts = await worker.ProbeAsync(new(row.ItemId, row.Path), cancellationToken, forOptimization: true);
                selection.Add(new(row.ItemId, row.Path, facts, null));
                row.ApplyResult(new(row.Path, OperationState.Pending, "Waiting for optimization confirmation"));
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
        var planner = new OptimizationViewModel(trial!, request.RequestId, selection, rows[0].Settings, PublicationSupport.ReplacementAvailable);
        ConfirmedPngOptimization? confirmed;
        {
            planner.Preset = request.Action switch
            {
                "auto" => PngOptimizationPreset.Auto, "balanced" => PngOptimizationPreset.Balanced,
                "smallest" => PngOptimizationPreset.Smallest, _ => PngOptimizationPreset.Lossless
            };
            planner.ReplaceOriginal = rows[0].Settings.Preferences.ReplaceOriginals &&
                rows[0].Settings.Preferences.OutputDirectory is null && PublicationSupport.ReplacementAvailable;
            // Explicit saved output preference is captured with the menu choice.
            // The executor still owns access admission and publication validation.
            confirmed = planner.Plan?.HasExecutableItems == true
                ? planner.Plan.Confirm(planner.ReplaceOriginal, PublicationSupport.ReplacementAvailable) : null;
        }
        if (confirmed is null)
        {
            foreach (var row in rows.Where(row => row.Result.State == OperationState.Pending))
            {
                var reason = planner.Plan?.Items.FirstOrDefault(item => item.Source.ItemId == row.ItemId)?.BlockReason;
                row.ApplyResult(new(row.Path, reason is null ? OperationState.Cancelled : OperationState.Unsupported,
                    reason ?? "Optimization cancelled before confirmation"));
            }
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var byId = rows.ToDictionary(row => row.ItemId);
        var completed = rows.Count(row => row.Result.State is OperationState.Failed or OperationState.Unsupported);
        await new PngOptimizationExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, (item, result) =>
        {
            byId[item.Source.ItemId].ApplyResult(result);
            if (result.State != OperationState.Running) completed++;
            Summary = $"Optimizing: {completed} of {rows.Length} finished.";
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

    internal string RetryFailed()
    {
        // Inspect the latest attempt for each source/tool, not every historical error.
        // Newly queued rows immediately suppress a second click, even before dispatch.
        var failed = RetryCandidates.ToArray();
        if (failed.Length == 0) return "No files need another attempt. Completed files are skipped.";
        var available = failed.Where(row => File.Exists(row.Path)).ToArray();
        var received = 0;
        foreach (var group in available.GroupBy(row => (row.Operation, row.Action)))
        foreach (var chunk in group.Chunk(OperationRequest.MaximumPaths))
        {
            try
            {
                var reply = Admit(new(Guid.NewGuid(), group.Key.Operation, group.Key.Action, chunk.Select(row => row.Path).ToImmutableArray()));
                if (reply.Accepted)
                {
                    received += chunk.Length;
                    foreach (var row in chunk) row.WasRetried = true;
                }
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or IOException)
            { /* A disappeared file or rejected batch remains in the existing result history. */ }
        }
        var notQueued = failed.Length - received;
        RefreshSummary();
        return notQueued == 0 ? "" : $"{notQueued} file(s) could not be restarted. Check that the files are still available.";
    }

    private void RefreshSummary()
    {
        var current = DisplayRows.ToArray();
        var noChanges = current.All(r => r.Result.Publication?.IsCommitted != true);
        var completed = current.Count(r => r.Result.State == OperationState.Succeeded);
        var warnings = current.Count(r => r.Result.Publication?.HasWarning == true);
        var failed = current.Count(r => r.Result.State == OperationState.Failed);
        var cancelled = current.Count(r => r.Result.State == OperationState.Cancelled);
        var unchanged = current.Count(r => r.Result.State == OperationState.Unchanged);
        var unsupported = current.Count(r => r.Result.State == OperationState.Unsupported);
        var pending = current.Length - completed - failed - cancelled - unchanged - unsupported;
        var counts = new List<string>();
        if (completed > 0) counts.Add($"{completed} completed" + (warnings > 0 ? $" ({warnings} warnings)" : ""));
        var alreadyTarget = current.Count(row => row.Operation == "convert" && row.Result.State == OperationState.Unchanged);
        if (unchanged > alreadyTarget) counts.Add($"{unchanged - alreadyTarget} with no smaller result");
        if (alreadyTarget > 0) counts.Add($"{alreadyTarget} already in target format");
        if (failed > 0) counts.Add($"{failed} failed");
        if (unsupported > 0) counts.Add($"{unsupported} not supported");
        if (cancelled > 0) counts.Add($"{cancelled} cancelled");
        if (pending > 0) counts.Add($"{pending} waiting");
        Summary = counts.Count == 0 ? "Select files in Explorer and choose Analyze, Convert, or Optimize." : string.Join(" · ", counts) + ".";
        if (current.Any(row => row.Result.Publication?.MetadataWarning == true))
            Summary += " Some metadata was removed from optimized copies. Those originals are unchanged.";
        if (noChanges && counts.Count > 0) Summary += " No files changed.";
        var optimized = current.Where(row => row.Operation == "optimize" && row.Result.Publication?.IsCommitted == true).ToArray();
        var saved = optimized.Sum(row => row.Result.Publication!.SourceBytes - row.Result.Publication.OutputBytes);
        var inputBytes = optimized.Sum(row => row.Result.Publication!.SourceBytes);
        if (saved > 0) Summary += $" Optimization saved {saved:N0} bytes ({100.0 * saved / inputBytes:F1}% of saved files' input size).";
        Changed(nameof(HasProblems));
        Changed(nameof(ShowDetails));
        Changed(nameof(DisplayRows)); Changed(nameof(CanRetry));
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

internal sealed class FileRow(int batch, string operation, string path, BatchSettings settings, string? action = null) : INotifyPropertyChanged
{
    public Guid ItemId { get; } = Guid.NewGuid();
    public int Batch { get; set; } = batch;
    public bool WasRetried { get; set; }
    public string Operation { get; } = operation;
    public string Action { get; } = action ?? (operation switch { "optimize" => "choose-preset", "analyze" => "open-details", _ => "choose-format" });
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public BatchSettings Settings { get; } = settings;
    public FileResult Result { get; private set; } = new(path, OperationState.Pending, "Pending");
    public DdsInfo? Analysis { get; private set; }
    public string Status => Result.Message;
    public string Outcome => Result.Publication?.MetadataWarning == true ? "Completed with warning" :
        Result.Publication?.HasWarning == true ? "Saved — needs attention" : Result.State switch
    {
        OperationState.Succeeded => "Completed", OperationState.Unchanged => Operation == "convert" ? "Already in target format" : "No smaller result",
        OperationState.Unsupported => "Not supported", OperationState.Failed => "Needs attention",
        _ => Result.State.ToString()
    };
    public bool HasOutput => OutputPath.Length > 0;
    public string Savings => Result.Publication is { IsCommitted: true } p && p.SourceBytes > p.OutputBytes
        ? $"{100.0 * (p.SourceBytes - p.OutputBytes) / p.SourceBytes:F1}%" : "—";
    public string OutputPath => Result.Publication?.OutputPath ?? "";
    public string RetainedOriginalPath => Result.Publication?.RetainedOriginalPath ?? "";
    public string RecoveryRecordPath => Result.Publication?.RecoveryRecordPath ?? "";
    public string ResultDetails => $"Source: {Path}\n{Status}" +
        (Result.EngineIdentity is null ? "" : $"\nEngine / policy: {Result.EngineIdentity}") +
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
        foreach (var property in new[] { nameof(Result), nameof(Status), nameof(OutputPath), nameof(RetainedOriginalPath), nameof(RecoveryRecordPath), nameof(ResultDetails), nameof(Outcome), nameof(HasOutput), nameof(Savings) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
