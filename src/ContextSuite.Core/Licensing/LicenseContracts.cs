namespace ContextSuite.Core.Licensing;

public enum LicenseEnvironment { Production, Sandbox }
public enum LicenseReplyState { Granted, Rejected, Unavailable, InvalidResponse, Deactivated, ActivationDeclined }

// No key is present in a receipt, status message or generated diagnostic string.
public sealed record LicenseReceipt(Guid LicenseId, Guid ActivationId, DateTimeOffset? ExpiresUtc);
public sealed record LicenseReply(LicenseReplyState State, string Message, LicenseReceipt? Receipt = null);

public interface ILicenseService
{
    LicenseEnvironment Environment { get; }
    Task<LicenseReply> ActivateAsync(string key, Guid installationId, CancellationToken cancellationToken);
    Task<LicenseReply> ValidateAsync(string key, Guid installationId, Guid activationId, CancellationToken cancellationToken);
    Task<LicenseReply> DeactivateAsync(string key, Guid activationId, CancellationToken cancellationToken);
}

public enum PaidLicenseState { NotActivated, Active, Offline, Expired, Rejected, RecoveryRequired, Unavailable }

public sealed record PaidLicenseStatus(PaidLicenseState State, string Message, bool RefreshDue = false,
    DateTimeOffset? OfflineUntilUtc = null)
{
    public bool CanStart => State is PaidLicenseState.Active or PaidLicenseState.Offline;
}
