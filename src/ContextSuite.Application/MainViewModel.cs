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
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel(WorkerClient worker, SuiteSettings? settings = null, OutputPublisher? publisher = null,
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
    public event Func<AudioConversionViewModel, CancellationToken, Task<ConfirmedAudioConversion?>>? AudioConversionRequested;
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
            Summary = $"Analyzing file {index + 1} of {rows.Length}. No files changed.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Reading file information"));
            try
            {
                var facts = await FileAnalysisReader.ReadAsync(row.Path, cancellationToken,
                    worker.HasAudioProbe ? worker.ProbeAudioAsync : null, worker.HasPdfProbe ? worker.ProbePdfAsync : null);
                row.ApplyAnalysis(facts);
            }
            catch (InvalidDataException)
            { row.ApplyResult(new(row.Path, OperationState.Unsupported, "This path cannot be analyzed. Choose an available regular file; linked paths and offline placeholders are not read.")); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            { row.ApplyResult(new(row.Path, OperationState.Failed, "Could not read a stable file header. Check that the file is available and has finished saving, then run Analyze again.")); }
        }
    }

    private async Task ConvertBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken cancellationToken)
    {
        if (request.IsQuickAudioConversion)
        {
            await ConvertAudioBatchAsync(request, rows, cancellationToken);
            return;
        }
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
        var images = new List<ImageSourceFacts>();
        var audio = new List<AudioFileSource>();
        var documents = new List<PdfFileSource>();
        for (var index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[index];
            Summary = $"Checking file {index + 1} of {rows.Length}.";
            row.ApplyResult(new(row.Path, OperationState.Running, "Checking optimization support"));
            try
            {
                var header = await FileAnalysisReader.ReadAsync(row.Path, cancellationToken, headerOnly: true);
                if (header.Identity is { FormatId: "flac" or "png" or "pdf", Basis: IdentificationBasis.Content } identity &&
                    !string.Equals(Path.GetExtension(row.Path), "." + identity.FormatId, StringComparison.OrdinalIgnoreCase))
                {
                    row.ApplyResult(new(row.Path, OperationState.Unsupported,
                        $"This file contains {identity.Name}, but its filename has a different extension. Rename it to .{identity.FormatId} before optimizing."));
                    continue;
                }
                if (header.Identity is { FormatId: "flac", Basis: IdentificationBasis.Content })
                {
                    if (request.Action is "balanced" or "smallest")
                    {
                        row.ApplyResult(new(row.Path, OperationState.Unsupported, "For FLAC, choose Auto or Lossless. Balanced and Smallest apply to PNG images."));
                        continue;
                    }
                    if (!worker.HasFlacOptimizer)
                    {
                        row.ApplyResult(new(row.Path, OperationState.Unsupported, "FLAC optimization is unavailable in this build."));
                        continue;
                    }
                    audio.Add(await worker.ProbeFlacAsync(new(row.ItemId, row.Path), cancellationToken));
                }
                else if (header.Identity is { FormatId: "png", Basis: IdentificationBasis.Content })
                    images.Add(await worker.ProbeAsync(new(row.ItemId, row.Path), cancellationToken, forOptimization: true));
                else if (header.Identity is { FormatId: "pdf", Basis: IdentificationBasis.Content })
                {
                    if (request.Action is "balanced" or "smallest")
                    {
                        row.ApplyResult(new(row.Path, OperationState.Unsupported, "For PDF, choose Auto or Lossless. Balanced and Smallest apply to PNG images."));
                        continue;
                    }
                    if (!worker.HasPdfOptimizer)
                    {
                        row.ApplyResult(new(row.Path, OperationState.Unsupported, "PDF optimization is unavailable in this build."));
                        continue;
                    }
                    documents.Add(await worker.ProbePdfFileAsync(new(row.ItemId, row.Path), cancellationToken));
                }
                else
                {
                    row.ApplyResult(new(row.Path, OperationState.Unsupported, "Optimization supports PNG images, FLAC audio and PDF documents. Other files are kept unchanged."));
                    continue;
                }
                row.ApplyResult(new(row.Path, OperationState.Pending, "Preparing optimization"));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                var message = error is MediaWorkerException ? error.Message : "File could not be read. Check the file and retry.";
                row.ApplyResult(new(row.Path, error is MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
                    ? OperationState.Unsupported : OperationState.Failed, message));
            }
        }
        var snapshot = rows[0].Settings;
        var replace = snapshot.Preferences.ReplaceOriginals && snapshot.Preferences.OutputDirectory is null && PublicationSupport.ReplacementAvailable;
        var preset = request.Action switch
        {
            "auto" => PngOptimizationPreset.Auto, "balanced" => PngOptimizationPreset.Balanced,
            "smallest" => PngOptimizationPreset.Smallest, _ => PngOptimizationPreset.Lossless
        };
        var pngPlan = images.Count == 0 ? null : PngOptimizationPlan.Create(request.RequestId, images, snapshot, replace, preset);
        var flacPlan = audio.Count == 0 ? null : FlacOptimizationPlan.Create(request.RequestId, audio, snapshot, replace);
        var pdfPlan = documents.Count == 0 ? null : PdfOptimizationPlan.Create(request.RequestId, documents, snapshot);
        var byId = rows.ToDictionary(row => row.ItemId);
        foreach (var item in pngPlan?.Items ?? [])
            if (!item.CanExecute) byId[item.Source.ItemId].ApplyResult(new(item.Source.Path, OperationState.Unsupported, item.BlockReason!));
        foreach (var item in flacPlan?.Items ?? [])
            if (!item.CanExecute) byId[item.Source.ItemId].ApplyResult(new(item.Source.Path, OperationState.Unsupported, item.BlockReason!));
        foreach (var item in pdfPlan?.Items ?? [])
            if (!item.CanExecute) byId[item.Source.ItemId].ApplyResult(new(item.Source.Path, OperationState.Unsupported, item.BlockReason!));
        var png = pngPlan?.HasExecutableItems == true ? pngPlan.Confirm(replace, PublicationSupport.ReplacementAvailable) : null;
        var flac = flacPlan?.HasExecutableItems == true ? flacPlan.Confirm(replace, PublicationSupport.ReplacementAvailable) : null;
        var pdf = pdfPlan?.HasExecutableItems == true ? pdfPlan.Confirm() : null;
        if (png is null && flac is null && pdf is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        // One Explorer invocation is one admitted batch, including mixed families.
        // All confirmed plans carry the same request ID and settings snapshot.
        var admission = png is not null ? await trial!.AdmitOptimizationAsync(png, cancellationToken)
            : flac is not null ? await trial!.AdmitOptimizationAsync(flac, cancellationToken)
            : await trial!.AdmitOptimizationAsync(pdf!, cancellationToken);
        if (png is not null)
            await new PngOptimizationExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(png, admission,
                (item, result) => Report(item.Source.ItemId, result), cancellationToken);
        if (flac is not null)
            await new FlacOptimizationExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(flac, admission,
                (item, result) => Report(item.Source.ItemId, result), cancellationToken);
        if (pdf is not null)
            await new PdfOptimizationExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(pdf, admission,
                (item, result) => Report(item.Source.ItemId, result), cancellationToken);

        void Report(Guid itemId, FileResult result)
        {
            byId[itemId].ApplyResult(result);
            var completed = rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running));
            Summary = $"Optimizing: {completed} of {rows.Length} finished.";
        }
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
    public FileAnalysis? Analysis { get; private set; }
    public bool HasAnalysis => Analysis is not null;
    public string Status => Result.Message;
    public string Outcome => Result.Publication?.MetadataWarning == true ? "Completed with warning" :
        Result.Publication?.HasWarning == true ? "Saved — needs attention" : Result.State switch
    {
        OperationState.Succeeded => Operation == "analyze" ? Analysis?.Identity.Name ?? "Analyzed" : "Completed", OperationState.Unchanged => Operation == "convert" ? "Already in target format" : "No smaller result",
        OperationState.Unsupported => "Not supported", OperationState.Failed => "Needs attention",
        _ => Result.State.ToString()
    };
    public bool HasOutput => OutputPath.Length > 0;
    public string Savings => Result.Publication is { IsCommitted: true } p && p.SourceBytes > p.OutputBytes
        ? $"{100.0 * (p.SourceBytes - p.OutputBytes) / p.SourceBytes:F1}%" : "—";
    public string OutputPath => Result.Publication?.OutputPath ?? "";
    public string RetainedOriginalPath => Result.Publication?.RetainedOriginalPath ?? "";
    public string RecoveryRecordPath => Result.Publication?.RecoveryRecordPath ?? "";
    public string OperationDetails => HasAnalysis ? "" : $"Source: {Path}\n{Status}" +
        (Result.EngineIdentity is null ? "" : $"\nEngine / policy: {Result.EngineIdentity}") +
        (OutputPath.Length == 0 ? "" : $"\nOutput: {OutputPath}") +
        (RetainedOriginalPath.Length == 0 ? "" : $"\nRetained original: {RetainedOriginalPath}") +
        (RecoveryRecordPath.Length == 0 ? "" : $"\nRecovery record: {RecoveryRecordPath}");

    public string AnalysisSummary => Analysis is not { } analysis ? "" :
        $"What it is: {analysis.Identity.Name} ({(analysis.Identity.Basis == IdentificationBasis.Filename ? "filename hint" : analysis.Identity.Confidence.ToString().ToLowerInvariant())})\n" +
        $"Commonly used for: {analysis.Identity.CommonUses}\nFile size: {analysis.FileBytes:N0} bytes" +
        (analysis.FilenameHints.IsDefaultOrEmpty || analysis.FilenameHints.All(hint => hint.Id == analysis.Identity.FormatId) ? "" :
            "\nFilename hints (not confirmed): " + string.Join("; ", analysis.FilenameHints.Select(hint => hint.Name + " — " + hint.CommonUses))) +
        string.Concat(analysis.Warnings.Select(warning => "\n" + warning));

    public string AnalysisDetails => Analysis is not { } analysis ? "" :
        $"Source: {Path}\n{string.Join("\n", analysis.Identity.Evidence)}\n" +
        string.Join("\n", analysis.Facts.GroupBy(fact => fact.Group).Select(group => group.Key + ":\n" +
            string.Join("\n", group.Select(fact => $"{fact.Label}: {FactText(fact)}")))) +
        $"\nRead-only analysis; no files changed. Analyzer {FileAnalysis.AnalyzerVersion}; catalog {FileTypeCatalog.Default.Revision}.";

    public string ResultDetails => HasAnalysis ? AnalysisSummary + "\n" + AnalysisDetails : OperationDetails;

    private static string FactText(AnalysisFact fact) => fact.Availability switch
    {
        FactAvailability.Unknown => "Unknown", FactAvailability.NotEncoded => "Not encoded",
        FactAvailability.Unavailable => "Not checked by this analyzer",
        _ => (fact.Text ?? fact.Integer?.ToString("N0") ?? (fact.Boolean is { } value ? value ? "Yes" : "No" : "Unknown")) +
            (fact.Availability == FactAvailability.Derived ? " (derived)" : "")
    };

    public void ApplyAnalysis(FileAnalysis facts)
    {
        Analysis = facts;
        ApplyResult(new(Path, OperationState.Succeeded, facts.Identity.Name +
            (facts.Warnings.IsEmpty ? "" : $"; {facts.Warnings.Length} warning(s)")));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Analysis)));
    }

    public void ApplyResult(FileResult result)
    {
        if (!string.Equals(System.IO.Path.GetFullPath(result.Path), System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The file result belongs to a different source.");
        Result = result;
        foreach (var property in new[] { nameof(Result), nameof(Status), nameof(OutputPath), nameof(RetainedOriginalPath), nameof(RecoveryRecordPath), nameof(ResultDetails), nameof(Outcome), nameof(HasOutput), nameof(Savings), nameof(HasAnalysis), nameof(AnalysisSummary), nameof(AnalysisDetails), nameof(OperationDetails) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
