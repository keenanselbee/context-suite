using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Core.Licensing;

namespace ContextSuite.Application.TestHost;

// Test-host only. No networking, real keys, production composition or shipping bypass.
// The real app still performs DPAPI storage and access admission around this provider.
internal sealed class LicenseWorkflowFixture : ILicenseService
{
    public const string TestKey = "TEST-ONLY";
    private static readonly Guid LicenseId = new("a3b02f1e-c580-4407-9a60-68e751d5926a");
    public LicenseEnvironment Environment => LicenseEnvironment.Sandbox;

    public static void Prepare(string root)
    {
        // The wrapper reserves a new directory for each run. Never reset a used
        // profile or write an expired-trial fixture into normal application data.
        if (!Directory.Exists(root) || Directory.EnumerateFileSystemEntries(root).Any() ||
            !Path.GetFullPath(root).Contains(Path.DirectorySeparatorChar + ".codex-temp" +
                Path.DirectorySeparatorChar + "license-workflow" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("License workflow requires a fresh isolated test directory.");
        var access = Directory.CreateDirectory(Path.Combine(root, "Access"));
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(access.FullName, "trial.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, StartedUtc = now.AddDays(-4), LastObservedUtc = now
        }));
        var pixels = new byte[64 * 48 * 3];
        for (var y = 0; y < 48; y++)
        for (var x = 0; x < 64; x++)
        {
            var offset = (y * 64 + x) * 3;
            pixels[offset] = (byte)(x < 32 ? 230 : 35);
            pixels[offset + 1] = (byte)(y < 24 ? 60 : 200);
            pixels[offset + 2] = 90;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 48, 96, 96, PixelFormats.Rgb24, null, pixels, 64 * 3)));
        using var output = new FileStream(Path.Combine(root, "fixture.png"), FileMode.CreateNew);
        encoder.Save(output);
    }

    public Task<LicenseReply> ActivateAsync(string key, Guid installationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(key == TestKey && installationId != Guid.Empty
            ? new LicenseReply(LicenseReplyState.Granted, "Simulated grant", new(LicenseId, Guid.NewGuid(), null))
            : new LicenseReply(LicenseReplyState.ActivationDeclined, "Use TEST-ONLY in this isolated test."));
    }

    public Task<LicenseReply> ValidateAsync(string key, Guid installationId, Guid activationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(key == TestKey && installationId != Guid.Empty && activationId != Guid.Empty
            ? new LicenseReply(LicenseReplyState.Granted, "Simulated validation", new(LicenseId, activationId, null))
            : new LicenseReply(LicenseReplyState.Rejected, "Simulated rejection"));
    }

    public Task<LicenseReply> DeactivateAsync(string key, Guid activationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LicenseReply(key == TestKey && activationId != Guid.Empty
            ? LicenseReplyState.Deactivated : LicenseReplyState.Rejected, "Simulated deactivation"));
    }
}
