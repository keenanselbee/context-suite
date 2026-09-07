using System.Runtime.InteropServices;

namespace ContextSuite.Application.Infrastructure;

internal static class PublicationSupport
{
    // Native commit, recycling, permanent-delete veto, and crash probes verified on build 26200.
    // Extend this allowlist only with equivalent Windows evidence. Location checks remain mandatory.
    public static bool ReplacementAvailable => OperatingSystem.IsWindows() &&
        Environment.OSVersion.Version.Build == 26200 && RuntimeInformation.ProcessArchitecture == Architecture.X64;
}
