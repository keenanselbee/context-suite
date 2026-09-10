using ContextSuite.Core.Licensing;

namespace ContextSuite.Core.ContractTests;

internal static class PaidLicenseContracts
{
    public static void Run(Action<bool, string> check)
    {
        var verified = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        PaidLicenseStatus At(TimeSpan elapsed) => PaidLicensePolicy.Evaluate(verified + elapsed, verified, verified, null);
        check(At(TimeSpan.Zero) is { CanStart: true, RefreshDue: false }, "paid policy: fresh validation");
        check(At(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1)).RefreshDue == false, "paid policy: before daily refresh boundary");
        check(At(TimeSpan.FromDays(1)) is { CanStart: true, RefreshDue: true }, "paid policy: daily refresh without blocking valid offline access");
        check(At(TimeSpan.FromDays(30) - TimeSpan.FromTicks(1)).CanStart, "paid policy: grace before exact deadline");
        check(At(TimeSpan.FromDays(30)) is { CanStart: false, RefreshDue: true }, "paid policy: exact offline deadline blocks new work");
        check(!PaidLicensePolicy.Evaluate(verified, verified, verified, null, rejected: true).CanStart, "paid policy: rejection overrides fresh cache");
        check(!PaidLicensePolicy.Evaluate(verified, verified, verified, null, recoveryRequired: true).CanStart, "paid policy: pending mutation blocks access");
        check(!PaidLicensePolicy.Evaluate(verified.AddDays(2), verified, verified, verified.AddDays(2)).CanStart, "paid policy: provider expiry caps grace");
        check(!At(TimeSpan.FromMinutes(-6)).CanStart, "paid policy: backwards clock rejected");
        check(PaidLicensePolicy.Evaluate(verified.AddDays(30).AddMinutes(-1), verified, verified.AddDays(30), null).CanStart == false,
            "paid policy: small clock correction cannot extend grace");
        check(!PaidLicensePolicy.Evaluate(verified, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, null).CanStart, "paid policy: malformed clock facts fail closed");
        check(!PaidLicensePolicy.Evaluate(verified, verified, verified.AddSeconds(-1), null).CanStart, "paid policy: invalid observation ordering");
    }
}
