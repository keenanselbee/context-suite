namespace ContextSuite.Core.Licensing;

// Pure policy over already validated, protected local facts. This is not a
// cryptographic entitlement or a substitute for transport identity checks.
public static class PaidLicensePolicy
{
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(30);
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);

    public static PaidLicenseStatus Evaluate(DateTimeOffset now, DateTimeOffset verifiedUtc,
        DateTimeOffset lastObservedUtc, DateTimeOffset? expiresUtc, bool rejected = false, bool recoveryRequired = false)
    {
        if (rejected) return new(PaidLicenseState.Rejected, "This license is no longer valid. Check your license or open the customer portal.");
        if (recoveryRequired) return new(PaidLicenseState.RecoveryRequired, "License recovery is needed. Check the customer portal before activating again.");
        if (verifiedUtc < DateTimeOffset.UnixEpoch || verifiedUtc > DateTimeOffset.MaxValue - OfflineGrace ||
            lastObservedUtc < verifiedUtc || now < lastObservedUtc - TimeSpan.FromMinutes(5))
            return new(PaidLicenseState.Unavailable, "Check Windows date and time, then validate your license online.");
        // A small backwards correction must not extend the offline deadline.
        if (now < lastObservedUtc) now = lastObservedUtc;
        var deadline = verifiedUtc + OfflineGrace;
        if (expiresUtc < deadline) deadline = expiresUtc.Value;
        if (now >= deadline)
            return new(PaidLicenseState.Expired, "Connect to the internet and validate your license to continue. Analyze remains available.", true, deadline);
        var due = now >= verifiedUtc + RefreshInterval;
        return new(due ? PaidLicenseState.Offline : PaidLicenseState.Active,
            due ? "License active using saved validation. Online refresh is due." : "License active. All future updates included.", due, deadline);
    }
}
