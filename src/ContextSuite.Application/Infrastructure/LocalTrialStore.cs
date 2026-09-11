using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextSuite.Core.Images;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal enum LocalTrialState { NotStarted, Active, Expired, Unavailable }
internal sealed record LocalTrialStatus(LocalTrialState State, string Message, DateTimeOffset? ExpiresUtc = null);
internal sealed record TrialAdmission(LocalTrialStatus Status, Guid BatchId, DateTimeOffset? AdmittedUtc = null)
{
    public bool IsAllowed => AdmittedUtc is not null && Status.State == LocalTrialState.Active;
}

// Local trial bookkeeping only. No key validation, hidden copies, reset switch or permissive fallback.
internal sealed class LocalTrialStore : IOperationAccess
{
    async Task<OperationAdmission> IOperationAccess.AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm(true);
        var admission = await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    async Task<OperationAdmission> IOperationAccess.AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        var admission = await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    async Task<OperationAdmission> IOperationAccess.AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No confirmed PDF optimization can execute.");
        var admission = await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    async Task<OperationAccessStatus> IOperationAccess.ReadAccessAsync(CancellationToken cancellationToken)
    {
        var status = await ReadStatusAsync(cancellationToken);
        return new(status.State is LocalTrialState.NotStarted or LocalTrialState.Active, status.Message);
    }

    async Task<OperationAdmission> IOperationAccess.AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken)
    {
        var admission = await AdmitAsync(confirmed, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    async Task<OperationAdmission> IOperationAccess.AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken)
    {
        var admission = await AdmitAsync(confirmed, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    async Task<OperationAdmission> IOperationAccess.AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No confirmed FLAC optimization can execute.");
        var admission = await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }

    private sealed record TrialRecord(int SchemaVersion, DateTimeOffset StartedUtc, DateTimeOffset LastObservedUtc);
    async Task<OperationAdmission> IOperationAccess.AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No confirmed audio conversion can execute.");
        var admission = await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
        return new(new(admission.IsAllowed, admission.Status.Message), admission.BatchId);
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
        WriteIndented = true
    };
    private static readonly TimeSpan Duration = TimeSpan.FromHours(72);
    private static readonly TimeSpan RollbackTolerance = TimeSpan.FromMinutes(5);
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _observedUtc;
    private long _observedTimestamp;
    private bool _hasSeenRecord;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "Access", "trial.json");

    public LocalTrialStore(string path, TimeProvider? clock = null)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Trial storage requires an absolute path.");
        _path = Path.GetFullPath(path);
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<LocalTrialStatus> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = await ReadAsync(cancellationToken);
            if (record is null) return new(LocalTrialState.NotStarted, "Your 72-hour trial starts when you confirm your first valid conversion or optimization.");
            var now = Observe(record.LastObservedUtc);
            return Status(record, now);
        }
        catch (Exception error) when (IsStorageError(error)) { return Unavailable(error); }
        finally { _gate.Release(); }
    }

    public async Task<TrialAdmission> AdmitAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        // Only the pure planner can construct confirmation, after all required choices.
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No confirmed conversion can execute.");
        return await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
    }

    public async Task<TrialAdmission> AdmitAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No confirmed optimization can execute.");
        return await AdmitBatchAsync(confirmed.Plan.BatchId, cancellationToken);
    }

    private async Task<TrialAdmission> AdmitBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RejectLinks(_path);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            RejectLinks(directory);
            await using var lease = await LockAsync(cancellationToken);
            var record = await ReadAsync(cancellationToken);
            var now = Observe(record?.LastObservedUtc);
            var next = record is null ? new TrialRecord(1, now, now) : record with { LastObservedUtc = now };
            // Record the high-water mark even when expired; a later clock correction cannot extend access.
            await SaveAsync(next, record is not null, cancellationToken);
            _hasSeenRecord = true;
            var status = Status(next, now);
            return new(status, batchId, status.State == LocalTrialState.Active ? now : null);
        }
        catch (Exception error) when (IsStorageError(error)) { return new(Unavailable(error), batchId); }
        finally { _gate.Release(); }
    }

    private async Task<TrialRecord?> ReadAsync(CancellationToken cancellationToken)
    {
        RejectLinks(_path);
        try
        {
            await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length is 0 or > 4096) throw new InvalidDataException("Trial record size is invalid.");
            var record = await JsonSerializer.DeserializeAsync<TrialRecord>(file, JsonOptions, cancellationToken);
            if (record is null || record.SchemaVersion != 1 || record.StartedUtc.Offset != TimeSpan.Zero ||
                record.LastObservedUtc.Offset != TimeSpan.Zero || record.StartedUtc < DateTimeOffset.UnixEpoch ||
                record.LastObservedUtc < record.StartedUtc || record.LastObservedUtc > DateTimeOffset.MaxValue - Duration)
                throw new InvalidDataException("Trial record version or timestamps are invalid.");
            _hasSeenRecord = true;
            return record;
        }
        catch (FileNotFoundException) when (!_hasSeenRecord) { return null; }
        catch (DirectoryNotFoundException) when (!_hasSeenRecord) { return null; }
    }

    private DateTimeOffset Observe(DateTimeOffset? recordedUtc)
    {
        var utc = _clock.GetUtcNow().ToUniversalTime();
        var timestamp = _clock.GetTimestamp();
        var floor = _observedUtc is DateTimeOffset observed ?
            observed + _clock.GetElapsedTime(_observedTimestamp, timestamp) : utc;
        if (recordedUtc > floor) floor = recordedUtc.Value;
        var effective = utc > floor ? utc : floor;
        _observedUtc = effective;
        _observedTimestamp = timestamp;
        if (utc < floor - RollbackTolerance) throw new TrialClockException();
        if (effective < DateTimeOffset.UnixEpoch || effective > DateTimeOffset.MaxValue - Duration)
            throw new InvalidDataException("System time is outside the supported range.");
        return effective;
    }

    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(_path + ".lock");
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(3))
            { await Task.Delay(50, cancellationToken); }
        }
    }

    private async Task SaveAsync(TrialRecord record, bool exists, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(_path)!, $"trial-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, record, JsonOptions, cancellationToken);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(_path);
            // Same-directory rename: never truncate the old record or delete it before the new file is ready.
            File.Move(temporary, _path, overwrite: exists);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static LocalTrialStatus Status(TrialRecord record, DateTimeOffset now)
    {
        var expires = record.StartedUtc + Duration;
        return now >= expires ? new(LocalTrialState.Expired, "Your trial has expired. Activate a license to convert or optimize. Analyze remains available.", expires) :
            new(LocalTrialState.Active, "Trial active. Already admitted batches may finish after expiry.", expires);
    }

    private static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked trial storage is not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException;

    private static LocalTrialStatus Unavailable(Exception error)
    {
        return new(LocalTrialState.Unavailable, error is TrialClockException ?
            "Windows time moved backwards. Correct the system clock before starting another conversion." :
            "Trial data is unavailable or damaged. No conversion was admitted and the trial was not reset.");
    }

    private sealed class TrialClockException : IOException { }
}
