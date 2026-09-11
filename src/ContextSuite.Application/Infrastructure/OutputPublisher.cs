using System.ComponentModel;
using System.Text.Json;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed record RecycleResult(bool Recycled, string Message, string? RecycledPath = null);
internal interface IFileRecycler
{
    Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken cancellationToken);
}

internal enum PublicationStage { Prepared, Validated, Publishing, Committed, Recycled }
internal sealed record PublicationRecord(Guid ItemId, string SourcePath, string OutputPath, string TemporaryPath,
    string? BackupPath, FileFingerprint Source, FileFingerprint? Candidate, PublicationStage Stage);

internal sealed class OutputReservation(OutputIntent intent, OutputPolicy policy, PublicationRecord record, string recordPath)
{
    public OutputIntent Intent { get; } = intent;
    public OutputPolicy Policy { get; } = policy;
    public PublicationRecord Record { get; internal set; } = record;
    public string RecordPath { get; } = recordPath;
    public string TemporaryPath => Record.TemporaryPath;
    internal bool Finished { get; set; }
}

// Application-owned IO boundary. Tests inject failures here, never a fake media engine in production.
internal class PublicationIo
{
    public virtual void Move(string source, string destination) { File.Move(source, destination, overwrite: false); }
    public virtual void Replace(string temporary, string source, string backup)
    {
        File.Replace(temporary, source, backup, ignoreMetadataErrors: false);
    }
    public virtual void Checkpoint(PublicationStage stage) { }
}

internal sealed class OutputPublisher(string recordDirectory, IFileRecycler recycler, bool replacementVerified = false,
    PublicationIo? io = null)
{
    private readonly string _recordDirectory = PublicationFiles.Normalize(recordDirectory);
    private readonly PublicationIo _io = io ?? new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, OutputReservation> _reservations = [];
    private readonly HashSet<string> _destinations = new(StringComparer.OrdinalIgnoreCase);
    public string RecordDirectory => _recordDirectory;
    public static string DefaultRecordDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "Publications");

    public async Task<OutputReservation> ReserveAsync(OutputIntent intent, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        var temporaryOwned = false;
        try
        {
            if (intent.ItemId == Guid.Empty || _reservations.ContainsKey(intent.ItemId))
                throw new InvalidDataException("Output item IDs must be unique.");
            var source = PublicationFiles.Normalize(intent.SourcePath);
            _ = OutputNames.Create(source, intent.Settings.Operation, intent.TargetExtension, representation: intent.Dds,
                replaceSource: intent.ReplaceOriginal, pageNumber: intent.PageNumber);
            if (intent.Settings.Operation == "optimize" && !string.Equals(Path.GetExtension(source).TrimStart('.'),
                intent.TargetExtension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Optimize must retain the source format.");
            var policy = intent.Settings.SelectOutput(intent.ReplaceOriginal, intent.ReplacementConfirmed,
                intent.QuickAction, replacementVerified);
            if (intent.ReplaceOriginal && !PublicationFiles.IsLocalNtfs(source))
                throw new InvalidDataException("Replacement is not verified for this location. Use a copy.");
            await using var input = PublicationFiles.OpenRead(source);
            var fingerprint = await PublicationFiles.FingerprintAsync(input, cancellationToken);
            var directory = PublicationFiles.Normalize(intent.Settings.Preferences.OutputDirectory ?? Path.GetDirectoryName(source)!);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Choose an existing output folder.");
            var samePath = intent.ReplaceOriginal && string.Equals(Path.GetExtension(source).TrimStart('.'),
                intent.TargetExtension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
            var output = samePath ? source : ChooseName(intent, directory);
            if (_destinations.Contains(output) || _reservations.Values.Any(r =>
                string.Equals(r.Record.SourcePath, output, StringComparison.OrdinalIgnoreCase) ||
                (intent.ReplaceOriginal && string.Equals(r.Record.SourcePath, source, StringComparison.OrdinalIgnoreCase))))
                throw new IOException("Another pending output conflicts with this source. Process it sequentially.");
            temporary = Path.Combine(directory, $".context-suite-{intent.ItemId:N}.tmp");
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            temporaryOwned = true;
            var backup = samePath ? Path.Combine(directory,
                $"{Path.GetFileNameWithoutExtension(source)} - Original {Guid.NewGuid():N}{Path.GetExtension(source)}") : null;
            if (backup is not null && Path.GetFileName(backup).Length > 255)
                throw new InvalidDataException("The source name is too long for a recoverable replacement. Rename it first.");
            Directory.CreateDirectory(_recordDirectory);
            PublicationFiles.RejectLinks(_recordDirectory);
            var record = new PublicationRecord(intent.ItemId, source, output, temporary, backup, fingerprint, null, PublicationStage.Prepared);
            var reservation = new OutputReservation(intent, policy, record, Path.Combine(_recordDirectory, $"{intent.ItemId:N}.json"));
            await WriteRecordAsync(reservation, createNew: true);
            _io.Checkpoint(PublicationStage.Prepared);
            _reservations.Add(intent.ItemId, reservation);
            _destinations.Add(output);
            temporary = null;
            return reservation;
        }
        finally
        {
            if (temporaryOwned && temporary is not null) DeleteTemporary(temporary);
            _gate.Release();
        }
    }

    public async Task<PublicationResult> PublishAsync(OutputReservation reservation, OutputValidation validation,
        CancellationToken cancellationToken = default)
    {
        // Publication is serialized, including repeated sources. Engines may only write the reserved temporary file.
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (reservation.Finished || !_reservations.TryGetValue(reservation.Intent.ItemId, out var owned) || !ReferenceEquals(owned, reservation))
                throw new InvalidDataException("The output reservation is not active in this publisher.");
            var record = reservation.Record;
            var committed = false;
            var preserveRecord = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (validation.ItemId != record.ItemId || !validation.MatchesPlan)
                    throw new InvalidDataException("Output validation did not approve this plan.");
                await using var source = PublicationFiles.OpenRead(record.SourcePath, allowRename: true);
                if (await PublicationFiles.FingerprintAsync(source, cancellationToken) != record.Source)
                    throw new InvalidDataException("The source changed after planning. Keep it and plan again.");
                FileFingerprint candidate;
                await using (var output = PublicationFiles.OpenRead(record.TemporaryPath))
                    candidate = await PublicationFiles.FingerprintAsync(output, cancellationToken);
                if (candidate.Length == 0 || !string.Equals(candidate.Sha256, validation.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("The temporary output is empty or changed after semantic validation.");
                if (reservation.Policy.SkipIfLarger && candidate.Length >= record.Source.Length)
                    return Result(reservation, PublicationOutcome.Unchanged, "No smaller acceptable result.");
                reservation.Record = record = record with { Candidate = candidate, Stage = PublicationStage.Validated };
                await WriteRecordAsync(reservation);
                _io.Checkpoint(record.Stage);
                cancellationToken.ThrowIfCancellationRequested();
                PublicationFiles.RejectLinks(record.SourcePath);
                PublicationFiles.RejectLinks(record.OutputPath);
                PublicationFiles.RejectLinks(record.TemporaryPath);
                reservation.Record = record = record with { Stage = PublicationStage.Publishing };
                await WriteRecordAsync(reservation);
                _io.Checkpoint(record.Stage);
                cancellationToken.ThrowIfCancellationRequested();
                // Revalidate after all preparation/checkpoints. Keep copies locked through the rename.
                using var finalCandidate = PublicationFiles.OpenRead(record.TemporaryPath, allowRename: true);
                if (await PublicationFiles.FingerprintAsync(finalCandidate, cancellationToken) != candidate ||
                    !await PublicationFiles.MatchesAsync(record.SourcePath, record.Source))
                    throw new InvalidDataException("An input or output changed before commit.");
                if (record.BackupPath is not null)
                {
                    // ReplaceFile requires exclusive access to the candidate while merging metadata.
                    finalCandidate.Dispose();
                    // Own the backup destination before ReplaceFile can overwrite it.
                    using (new FileStream(record.BackupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    _io.Replace(record.TemporaryPath, record.SourcePath, record.BackupPath);
                }
                else _io.Move(record.TemporaryPath, record.OutputPath);
                committed = true;
                reservation.Record = record = record with { Stage = PublicationStage.Committed };
                await WriteRecordAsync(reservation);
                _io.Checkpoint(record.Stage);
                if (!await PublicationFiles.MatchesAsync(record.OutputPath, candidate))
                {
                    preserveRecord = true;
                    return Result(reservation, PublicationOutcome.Failed, "Published output identity changed; retained originals require review.", record.OutputPath);
                }
                if (!reservation.Intent.ReplaceOriginal)
                    return Result(reservation, PublicationOutcome.CopyCreated, "Copy created.", record.OutputPath);

                var original = record.BackupPath ?? record.SourcePath;
                if (!await PublicationFiles.MatchesAsync(original, record.Source))
                    return RecoveryRequired(reservation);
                if (cancellationToken.IsCancellationRequested)
                    return Retained(reservation, "Output completed; original retained after cancellation.");
                var recycled = await recycler.RecycleAsync(original, record.Source, cancellationToken);
                if (!recycled.Recycled)
                    return await PublicationFiles.MatchesAsync(original, record.Source) ? Retained(reservation, recycled.Message) : RecoveryRequired(reservation);
                reservation.Record = record with { Stage = PublicationStage.Recycled };
                await WriteRecordAsync(reservation);
                _io.Checkpoint(PublicationStage.Recycled);
                return Result(reservation, PublicationOutcome.SourceReplaced, "Source replaced; original moved to Recycle Bin.", record.OutputPath);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                Win32Exception or OperationCanceledException)
            {
                // ReplaceFile can fail after moving the original. Keep every surviving artifact for review.
                preserveRecord = true;
                if (!committed && reservation.Record.Stage >= PublicationStage.Publishing && record.Candidate is not null &&
                    await PublicationFiles.MatchesAsync(record.OutputPath, record.Candidate))
                {
                    committed = true;
                    reservation.Record = record = record with { Stage = PublicationStage.Committed };
                }
                if (reservation.Record.Stage == PublicationStage.Recycled)
                    return Result(reservation, PublicationOutcome.SourceReplaced,
                        "Source replaced and original recycled; publication record cleanup requires review.", record.OutputPath)
                        with { CleanupWarning = true, RecoveryRecordPath = reservation.RecordPath };
                if (committed)
                {
                    if (reservation.Intent.ReplaceOriginal)
                        return await PublicationFiles.MatchesAsync(record.BackupPath ?? record.SourcePath, record.Source) ?
                            Retained(reservation, "Output completed; original cleanup requires review.") : RecoveryRequired(reservation);
                    return Result(reservation, PublicationOutcome.CopyCreated, "Copy created; publication record cleanup requires review.", record.OutputPath)
                        with { CleanupWarning = true, RecoveryRecordPath = reservation.RecordPath };
                }
                var retained = await PublicationFiles.MatchesAsync(record.SourcePath, record.Source) ? record.SourcePath :
                    record.BackupPath is not null && await PublicationFiles.MatchesAsync(record.BackupPath, record.Source) ? record.BackupPath : null;
                return Result(reservation, error is OperationCanceledException ? PublicationOutcome.Cancelled : PublicationOutcome.Failed,
                    error is OperationCanceledException ? "Cancelled before publication." : "Publication failed. Review retained originals and recovery evidence.")
                    with { RetainedOriginalPath = retained };
            }
            finally
            {
                reservation.Finished = true;
                _reservations.Remove(record.ItemId);
                _destinations.Remove(record.OutputPath);
                // Never purge artifacts after an attempted replacement; partial API failures are not rollback proof.
                if (reservation.Record.Stage < PublicationStage.Publishing)
                {
                    if (DeleteTemporary(record.TemporaryPath)) DeleteTemporary(reservation.RecordPath);
                }
                else if (!preserveRecord && (reservation.Record.Stage == PublicationStage.Recycled ||
                    (!reservation.Intent.ReplaceOriginal && committed))) DeleteTemporary(reservation.RecordPath);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<PublicationResult> AbandonAsync(OutputReservation reservation, bool cancelled)
    {
        // Failed workers/validators never get a publication token; this still releases their reservation.
        return await PublishAsync(reservation, new(reservation.Intent.ItemId, "", false),
            cancelled ? new CancellationToken(true) : CancellationToken.None);
    }

    public IReadOnlyList<string> FindRecoveryRecords()
    {
        if (!Directory.Exists(_recordDirectory)) return [];
        PublicationFiles.RejectLinks(_recordDirectory);
        return Directory.EnumerateFiles(_recordDirectory, "*.json").Take(128)
            .Where(p => Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out _)).ToArray();
    }

    private string ChooseName(OutputIntent intent, string directory)
    {
        for (var ordinal = 1; ordinal <= 10000; ordinal++)
        {
            var name = OutputNames.Create(intent.SourcePath, intent.Settings.Operation, intent.TargetExtension, ordinal,
                intent.Dds, intent.ReplaceOriginal, intent.PageNumber);
            var path = PublicationFiles.Normalize(Path.Combine(directory, name));
            if (!_destinations.Contains(path) && !File.Exists(path) && !Directory.Exists(path)) return path;
        }
        throw new IOException("Too many output name collisions. Choose another folder.");
    }

    private static PublicationResult Retained(OutputReservation reservation, string message)
    {
        return Result(reservation, reservation.Record.BackupPath is null ? PublicationOutcome.OriginalRetained : PublicationOutcome.BackupRetained,
            message, reservation.Record.OutputPath);
    }

    private static PublicationResult RecoveryRequired(OutputReservation reservation)
    {
        return Result(reservation, PublicationOutcome.RecoveryRequired,
            "Output completed, but original recovery could not be confirmed. Review the recovery record before further changes.",
            reservation.Record.OutputPath) with { RetainedOriginalPath = null };
    }

    private static PublicationResult Result(OutputReservation reservation, PublicationOutcome outcome, string message, string? output = null)
    {
        var record = reservation.Record;
        return new(record.SourcePath, outcome, message, output,
            outcome is PublicationOutcome.SourceReplaced ? null : record.BackupPath ?? record.SourcePath,
            record.Stage >= PublicationStage.Publishing && outcome is not (PublicationOutcome.SourceReplaced or PublicationOutcome.CopyCreated) ? reservation.RecordPath : null,
            record.Source.Length, record.Candidate?.Length ?? 0);
    }

    private static async Task WriteRecordAsync(OutputReservation reservation, bool createNew = false)
    {
        PublicationFiles.RejectLinks(reservation.RecordPath);
        var temporary = reservation.RecordPath + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(file, reservation.Record);
            file.Flush(true);
        }
        File.Move(temporary, reservation.RecordPath, overwrite: !createNew);
    }

    private static bool DeleteTemporary(string path)
    {
        try { PublicationFiles.RejectLinks(path); File.Delete(path); return true; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }
}
