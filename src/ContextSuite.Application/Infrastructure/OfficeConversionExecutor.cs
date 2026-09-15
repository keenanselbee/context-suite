using System.ComponentModel;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;

namespace ContextSuite.Application.Infrastructure;

internal sealed record OfficeConversionExecution(OperationAdmission Admission, IReadOnlyList<FileResult> Results);

// Keep this executor alive while cleanup is pending. Its leases and journal must
// survive a failed native cleanup; restart recovery may act only after owner death.
internal sealed class OfficeConversionExecutor(WorkerClient worker, OutputPublisher publisher, IOperationAccess access,
    string contextRoot) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Context> _pending = [];
    internal IReadOnlyList<string> PendingRecoveryRecords => _pending.Select(context => context.RecordPath).ToArray();

    public async Task<OfficeConversionExecution> ExecuteAsync(ConfirmedOfficeConversion confirmed, Action<FileResult>? report, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(confirmed); _ = confirmed.Plan.Confirm();
        token.ThrowIfCancellationRequested();
        if (!worker.HasOfficeConverter)
        {
            var unavailable = new OperationAdmission(new(false, "Office PDF conversion is unavailable in this build."), confirmed.Plan.BatchId);
            var results = confirmed.Plan.Sources.Select(source => new FileResult(source.Path, OperationState.Unsupported, unavailable.Status.Message)).ToArray();
            foreach (var result in results) report?.Invoke(result);
            return new(unavailable, results);
        }
        var admission = await access.AdmitConversionAsync(confirmed, token);
        return await ExecuteAdmittedAsync(confirmed, admission, report, token);
    }

    internal async Task<OfficeConversionExecution> ExecuteAdmittedAsync(ConfirmedOfficeConversion confirmed, OperationAdmission admission,
        Action<FileResult>? report, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm();
        if (admission.BatchId != confirmed.Plan.BatchId) throw new InvalidDataException("Office admission belongs to another batch.");
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            var results = new List<FileResult>();
            foreach (var source in confirmed.Plan.Sources)
            {
                FileResult result;
                if (!admission.IsAllowed) result = new(source.Path, OperationState.Failed, admission.Status.Message);
                else if (token.IsCancellationRequested) result = new(source.Path, OperationState.Cancelled, "Office conversion cancelled. Original kept.");
                else if (_pending.Count != 0) result = new(source.Path, OperationState.Failed,
                    "Earlier Office work still needs cleanup. Close Context Suite and reopen it to recover before retrying. Original kept.");
                else result = await ConvertAsync(source, confirmed.Plan, report, token);
                results.Add(result); report?.Invoke(result);
            }
            return new(admission, results);
        }
        finally { _gate.Release(); }
    }

    private async Task<FileResult> ConvertAsync(OfficeConversionSource source, OfficeConversionPlan plan, Action<FileResult>? report, CancellationToken token)
    {
        Context? context = null;
        OutputReservation? reservation = null;
        FileResult? result = null;
        Exception? failure = null;
        try
        {
            report?.Invoke(new(source.Path, OperationState.Running, "Preparing document for PDF conversion"));
            PublicationFiles.RejectLinks(contextRoot);
            Directory.CreateDirectory(contextRoot);
            var prepared = await OfficeContextPreparation.CreateAsync(contextRoot, worker.OfficeEngineDirectory, source.Path,
                source.Format, source.Calculation, token);
            context = new(prepared);
            if (prepared.OriginalIdentity.Length != source.FileBytes ||
                !string.Equals(prepared.OriginalIdentity.Sha256, source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Office source changed after admission.");
            reservation = await publisher.ReserveAsync(new(prepared.Work.ItemId, source.Path, "pdf", plan.Settings, QuickAction: true), token);
            if (reservation.Record.Source != prepared.OriginalIdentity)
                throw new InvalidDataException("The publication source differs from the prepared document.");
            token.ThrowIfCancellationRequested();
            await Task.Run(() => context.CreateProfile(worker.OfficeEngineDirectory, token), token);
            report?.Invoke(new(source.Path, OperationState.Running, "Creating PDF"));
            var candidate = await worker.ExportOfficeAsync(prepared.Work, token, prepared.Journal.RecordEngineIntent);
            await prepared.VerifyAsync(token);
            // Revoke native access and remove the isolated profile before allowing
            // independent validation or publication. This also empties its worker job.
            await CleanNativeAsync(context);
            report?.Invoke(new(source.Path, OperationState.Running, "Validating PDF"));
            var validated = await worker.ValidateOfficePdfAsync(new(prepared.Work, candidate, reservation.TemporaryPath), token);
            await prepared.VerifyAsync(token);
            result = (await publisher.PublishAsync(reservation, validated.Validation, token)).ToFileResult() with { EngineIdentity = validated.EngineIdentity };
        }
        catch (Exception error) when (Expected(error)) { failure = error; }
        finally
        {
            if (context is not null)
            {
                try
                {
                    await CleanNativeAsync(context);
                    context.Prepared.Dispose();
                }
                catch (Exception error) when (Expected(error))
                {
                    _pending.Add(context);
                    failure = error;
                }
            }
            if (reservation is { Finished: false })
            {
                try { result = (await publisher.AbandonAsync(reservation, token.IsCancellationRequested)).ToFileResult(); }
                catch (Exception error) when (Expected(error)) { failure ??= error; }
            }
        }
        if (failure is null) return result ?? throw new InvalidOperationException("Office conversion produced no outcome.");
        if (result?.Publication?.IsCommitted == true) return result;
        if (context is not null && _pending.Contains(context))
            return new(source.Path, OperationState.Failed, "Office cleanup needs attention. Original and recovery records were kept. Close Context Suite and reopen it before retrying.",
                new(source.Path, PublicationOutcome.Failed, "Office cleanup needs attention.", RecoveryRecordPath: context.RecordPath));
        var cancelled = failure is OperationCanceledException;
        var state = cancelled ? OperationState.Cancelled : failure is NotSupportedException or MediaWorkerException { Failure: ImageFailure.UnsupportedInput }
            ? OperationState.Unsupported : OperationState.Failed;
        var message = cancelled ? "Office conversion cancelled. Original kept." : failure is MediaWorkerException { Failure: ImageFailure.ResourceLimit }
            ? "The document exceeds a PDF processing limit. Try fewer pages or a smaller page size. Original kept."
            : "Office PDF conversion failed. Check the document and try again. Original kept.";
        return new(source.Path, state, message, result?.Publication);
    }

    private async Task CleanNativeAsync(Context context)
    {
        if (context.NativeClean) return;
        await worker.DisposeAsync();
        var journal = context.Prepared.Journal;
        if (journal.Changes.Any(change => change.Step == OfficeOwnershipStep.EngineIntent) &&
            !journal.Changes.Any(change => change.Step == OfficeOwnershipStep.ProcessesStopped))
            journal.Record(new(OfficeOwnershipStep.ProcessesStopped));
        if (context.Owner is not null) await Task.Run(async () => await context.Owner.DisposeAsync());
        context.NativeClean = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var context in _pending.ToArray())
            {
                await CleanNativeAsync(context);
                context.Prepared.Dispose();
                _pending.Remove(context);
            }
        }
        finally { _gate.Release(); }
    }

    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or
        InvalidOperationException or ArgumentException or Win32Exception or AggregateException or NotSupportedException or
        OperationCanceledException or System.Text.Json.JsonException or System.Security.SecurityException;

    private sealed class Context(OfficeContextPreparation prepared)
    {
        internal OfficeContextPreparation Prepared { get; } = prepared;
        internal OfficeSandboxOwner? Owner { get; private set; }
        internal bool NativeClean { get; set; }
        internal string RecordPath => Path.Combine(Path.GetDirectoryName(Prepared.Work.DirectoryPath)!, Prepared.Work.ItemId.ToString("N") + ".ownership");
        internal void CreateProfile(string engine, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { Owner = OfficeSandboxOwner.Create(Prepared.Journal); }
            catch (OfficeOwnershipCreationException error) { Owner = error.Owner; throw; }
            Owner.GrantDirectory(engine, false);
            Owner.GrantDirectory(Path.Combine(Prepared.Work.DirectoryPath, "input"), false);
            foreach (var child in new[] { "output", "profile", "temp" })
            {
                token.ThrowIfCancellationRequested();
                Owner.GrantDirectory(Path.Combine(Prepared.Work.DirectoryPath, child), true);
            }
        }
    }
}
