using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Licensing;

namespace ContextSuite.Application.Infrastructure;

internal sealed class PaidLicenseManager(ILicenseService service, LicenseStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // This does no HTTP. A later application timer calls RefreshAsync; quick work
    // within grace can be admitted without waiting for a licensing round trip.
    public async Task<PaidLicenseStatus> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var lease = await store.LockAsync(cancellationToken);
            var record = await store.ReadAsync(cancellationToken);
            var status = Status(record);
            if (record is { State: LicenseRecordState.Active })
                await store.SaveAsync(record with { LastObservedUtc = EffectiveNow(record) }, cancellationToken);
            return status;
        }
        catch (Exception error) when (StorageError(error)) { return StorageUnavailable(); }
    }

    public async Task<PaidLicenseStatus> ActivateAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        if (key.Length is 0 or > 512 || key.Any(char.IsControl))
            return (await ReadStatusAsync(cancellationToken)) with { Message = "Enter your license key." };
        try
        {
            await using var lease = await store.LockAsync(cancellationToken);
            var previous = await store.ReadAsync(cancellationToken);
            if (previous is { State: not LicenseRecordState.NotActivated })
                return new(PaidLicenseState.RecoveryRequired, "A license is already stored. Validate it, deactivate it, or recover through the customer portal before activating again.");
            var pending = new LicenseRecord(1, service.Environment, previous?.InstallationId ?? Guid.NewGuid(),
                LicenseRecordState.ActivationPending, key);
            // Durable marker precedes HTTP. Cancellation, lost reply or save failure
            // leaves a recoverable pending state rather than blindly retrying.
            await store.SaveAsync(pending, cancellationToken);
            var reply = await service.ActivateAsync(key, pending.InstallationId, cancellationToken);
            if (reply.State == LicenseReplyState.ActivationDeclined)
            {
                // A definitive provider refusal did not allocate a slot. Allow
                // correction without sending the user through interrupted recovery.
                await store.SaveAsync(new(1, service.Environment, pending.InstallationId, LicenseRecordState.NotActivated), cancellationToken);
                return new(PaidLicenseState.NotActivated, reply.Message);
            }
            if (reply.State != LicenseReplyState.Granted || reply.Receipt is null)
                return new(PaidLicenseState.RecoveryRequired, reply.Message + " Use the customer portal to check any activation before retrying.");
            var now = _clock.GetUtcNow();
            var active = pending with { State = LicenseRecordState.Active, Receipt = reply.Receipt,
                VerifiedUtc = now, LastObservedUtc = now, LastRefreshAttemptUtc = now };
            await store.SaveAsync(active, cancellationToken);
            return Status(active);
        }
        catch (Exception error) when (StorageError(error)) { return StorageUnavailable(); }
    }

    public async Task<PaidLicenseStatus> RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            LicenseRecord record;
            var refreshId = Guid.NewGuid();
            await using (var lease = await store.LockAsync(cancellationToken))
            {
                var current = await store.ReadAsync(cancellationToken);
                if (current is not { State: LicenseRecordState.Active, Receipt: not null }) return Status(current);
                record = current;
                var status = Status(record);
                var observed = EffectiveNow(record);
                if (!force && (!status.RefreshDue || record.LastRefreshAttemptUtc is { } attempt && observed - attempt < PaidLicensePolicy.RefreshInterval))
                    return status;
                await store.SaveAsync(record with { LastObservedUtc = observed, LastRefreshAttemptUtc = observed, RefreshId = refreshId }, cancellationToken);
            }
            // Never make quiet admission wait for a background network request.
            var reply = await service.ValidateAsync(record.Key!, record.InstallationId, record.Receipt.ActivationId, cancellationToken);
            await using var completionLease = await store.LockAsync(cancellationToken);
            var latest = await store.ReadAsync(cancellationToken);
            // A newer refresh, transfer or recovery wins over this delayed reply.
            if (latest is not { State: LicenseRecordState.Active } || latest.RefreshId != refreshId ||
                latest.InstallationId != record.InstallationId || latest.Key != record.Key || latest.Receipt != record.Receipt)
                return Status(latest);
            var attempted = latest with { RefreshId = null };
            var validatedOnline = reply.State == LicenseReplyState.Granted && reply.Receipt is { } receipt &&
                receipt.LicenseId == record.Receipt.LicenseId && receipt.ActivationId == record.Receipt.ActivationId;
            if (validatedOnline)
            {
                // Explicit online validation can recover a corrected local clock.
                var now = _clock.GetUtcNow();
                attempted = attempted with { Receipt = reply.Receipt, VerifiedUtc = now, LastObservedUtc = now, LastRefreshAttemptUtc = now };
            }
            else if (reply.State == LicenseReplyState.Rejected)
                attempted = attempted with { State = LicenseRecordState.Rejected };
            await store.SaveAsync(attempted, cancellationToken);
            var next = Status(attempted);
            if (validatedOnline && next.CanStart)
                return next with { Message = "License validated online. All future updates included." };
            return reply.State is LicenseReplyState.Unavailable or LicenseReplyState.InvalidResponse ?
                next with { Message = next.CanStart ? "Online validation is unavailable. Your saved license remains active within its offline period." : next.Message } : next;
        }
        catch (Exception error) when (StorageError(error)) { return StorageUnavailable(); }
    }

    public async Task<PaidLicenseStatus> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var lease = await store.LockAsync(cancellationToken);
            var record = await store.ReadAsync(cancellationToken);
            if (record?.Receipt is null || record.Key is null) return Status(record);
            var pending = record with { State = LicenseRecordState.DeactivationPending };
            await store.SaveAsync(pending, cancellationToken);
            var reply = await service.DeactivateAsync(record.Key, record.Receipt.ActivationId, cancellationToken);
            if (reply.State != LicenseReplyState.Deactivated) return new(PaidLicenseState.RecoveryRequired,
                "Deactivation could not be confirmed. Check the customer portal; this installation is not using cached paid access.");
            var empty = new LicenseRecord(1, service.Environment, record.InstallationId, LicenseRecordState.NotActivated);
            await store.SaveAsync(empty, cancellationToken);
            return Status(empty);
        }
        catch (Exception error) when (StorageError(error)) { return StorageUnavailable(); }
    }

    // Explicit UI acknowledgement after portal recovery, not an automatic retry
    // and not a trial reset. This cannot revoke a remote activation by itself.
    public async Task<PaidLicenseStatus> ForgetAfterPortalRecoveryAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) return await ReadStatusAsync(cancellationToken);
        try
        {
            await using var lease = await store.LockAsync(cancellationToken);
            var record = await store.ReadAsync(cancellationToken);
            if (record is { State: LicenseRecordState.Active })
                return new(PaidLicenseState.RecoveryRequired, "Deactivate the stored license before removing it.");
            await store.SaveAsync(new(1, service.Environment, record?.InstallationId ?? Guid.NewGuid(), LicenseRecordState.NotActivated), cancellationToken);
            return new(PaidLicenseState.NotActivated, "Local license recovery completed. You can enter your license key again.");
        }
        catch (Exception error) when (StorageError(error)) { return StorageUnavailable(); }
    }

    private PaidLicenseStatus Status(LicenseRecord? record)
    {
        if (record is null || record.State == LicenseRecordState.NotActivated)
            return new(PaidLicenseState.NotActivated, "No paid license activated. One purchase includes all future updates.");
        if (record.State is LicenseRecordState.ActivationPending or LicenseRecordState.DeactivationPending)
            return new(PaidLicenseState.RecoveryRequired, "An activation change was interrupted. Check the customer portal before activating again.");
        if (record.State == LicenseRecordState.Rejected)
            return new(PaidLicenseState.Rejected, "The stored license is no longer valid. Recover it through the customer portal.");
        return PaidLicensePolicy.Evaluate(_clock.GetUtcNow(), record.VerifiedUtc!.Value, record.LastObservedUtc!.Value, record.Receipt!.ExpiresUtc);
    }

    private DateTimeOffset EffectiveNow(LicenseRecord record) =>
        record.LastObservedUtc > _clock.GetUtcNow() ? record.LastObservedUtc.Value : _clock.GetUtcNow();
    private static bool StorageError(Exception error) => error is IOException or UnauthorizedAccessException or
        CryptographicException or JsonException or InvalidDataException;
    private static PaidLicenseStatus StorageUnavailable() => new(PaidLicenseState.Unavailable,
        "License storage is unavailable or damaged. No paid access was granted. Retry or contact support; your trial was not reset.");
}
