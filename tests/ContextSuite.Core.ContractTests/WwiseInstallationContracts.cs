using System.Buffers.Binary;
using ContextSuite.Core.Audio;

internal static class WwiseInstallationContracts
{
    public static async Task RunAsync(string workDirectory, Action<bool, string> check)
    {
        var root = Path.Combine(workDirectory, "wwise-installations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var audiokinetic = Path.Combine(root, "Audiokinetic");
            var installation = Path.Combine(audiokinetic, "Wwise2025.1.0.1");
            var bin = Path.Combine(installation, "Authoring", "x64", "Release", "bin");
            Directory.CreateDirectory(bin);
            var console = Path.Combine(bin, "WwiseConsole.exe");
            var canonical = Path.GetFullPath(console);
            check(WwiseInstallation.ResolveConsolePath(installation) == canonical &&
                WwiseInstallation.ResolveConsolePath(bin) == canonical &&
                WwiseInstallation.ResolveConsolePath(console) == canonical,
                "Wwise installation: install, bin and executable resolve to one canonical path");
            var customInstallation = Path.Combine(root, "AudioTools");
            var customAuthoring = Path.Combine(customInstallation, "Authoring");
            var customBin = Path.Combine(customAuthoring, "x64", "Release", "bin");
            Directory.CreateDirectory(customBin);
            var customConsole = Path.Combine(customBin, "WwiseConsole.exe");
            check(WwiseInstallation.ResolveConsolePath(customInstallation) == customConsole &&
                WwiseInstallation.ResolveConsolePath(customAuthoring) == customConsole &&
                WwiseInstallation.ResolveConsolePath(customConsole) == customConsole,
                "Wwise installation: explicit custom-named install accepts the same Authoring layout");

            foreach (var invalid in new[] { "", Path.Combine(root, "elsewhere"),
                Path.Combine(root, "Wwise2025.1.0.1", "Authoring", "x86", "Release", "bin", "WwiseConsole.exe"),
                "Wwise2025.1.0.1", @"C:Wwise2025.1.0.1", @"\Wwise2025.1.0.1",
                console + ":alternate", Path.Combine(root, "AudioTools", "..", "AudioTools"), console + "\n" })
            {
                try { WwiseInstallation.ResolveConsolePath(invalid); check(false, "Wwise installation: invalid path"); }
                catch (ArgumentException) { check(true, "Wwise installation: rejects invalid or non-x64 path"); }
            }
            try { await WwiseInstallation.InspectAsync(console); check(false, "Wwise installation: missing executable"); }
            catch (FileNotFoundException) { check(true, "Wwise installation: missing executable is actionable"); }

            await File.WriteAllBytesAsync(console, new byte[128]);
            await RejectInvalid("missing MZ signature");
            var fake = FakePe(0x014c);
            await File.WriteAllBytesAsync(console, fake);
            await Refuse("x86 executable");
            fake = FakePe(0x8664);
            await File.WriteAllBytesAsync(console, fake);
            await Refuse("x64 PE lacking Wwise version identity");

            var discovered = await WwiseInstallation.DiscoverAsync([audiokinetic]);
            check(discovered.Count == 0, "Wwise installation: invalid candidate is skipped during bounded discovery");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { await WwiseInstallation.InspectAsync(console, cancelled.Token); check(false, "Wwise installation: cancelled inspect"); }
                catch (OperationCanceledException) { check(true, "Wwise installation: inspect honors cancellation"); }
                try { await WwiseInstallation.DiscoverAsync([audiokinetic], cancelled.Token); check(false, "Wwise installation: cancelled discovery"); }
                catch (OperationCanceledException) { check(true, "Wwise installation: discovery honors cancellation"); }
            }

            var crowded = Path.Combine(root, "Crowded");
            for (var index = 0; index <= WwiseInstallation.MaximumCandidates; index++)
                Directory.CreateDirectory(Path.Combine(crowded, $"Wwise{index:D3}"));
            try { await WwiseInstallation.DiscoverAsync([crowded]); check(false, "Wwise installation: candidate limit"); }
            catch (InvalidDataException) { check(true, "Wwise installation: discovery enforces candidate limit"); }
            check((await WwiseInstallation.DiscoverAsync(Enumerable.Repeat(audiokinetic, 9))).Count == 0,
                "Wwise installation: duplicate roots do not expand bounded discovery");
            try { await WwiseInstallation.DiscoverAsync(InfiniteRoots()); check(false, "Wwise installation: unbounded root iterator"); }
            catch (ArgumentException) { check(true, "Wwise installation: infinite root iterator is stopped at its raw entry limit"); }
            try
            {
                await WwiseInstallation.DiscoverAsync(Enumerable.Range(0, 9).Select(index => Path.Combine(root, $"Root{index}")));
                check(false, "Wwise installation: root limit");
            }
            catch (ArgumentException) { check(true, "Wwise installation: discovery enforces root limit"); }

            async Task RejectInvalid(string scenario)
            {
                try { await WwiseInstallation.InspectAsync(console); check(false, "Wwise installation: " + scenario); }
                catch (InvalidDataException) { check(true, "Wwise installation: rejects " + scenario); }
            }
            async Task Refuse(string scenario)
            {
                try { await WwiseInstallation.InspectAsync(console); check(false, "Wwise installation: " + scenario); }
                catch (NotSupportedException) { check(true, "Wwise installation: refuses " + scenario); }
            }
            IEnumerable<string> InfiniteRoots()
            {
                while (true) yield return audiokinetic;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] FakePe(ushort machine)
    {
        var bytes = new byte[128];
        "MZ"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), 64);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(64));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(84), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(88), 0x20b);
        return bytes;
    }
}
