using System.ComponentModel;
using ContextSuite.Application;
using ContextSuite.Core.Audio;

internal static class WemSetupContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var console = Path.Combine(scratch, "Wwise", "Authoring", "x64", "Release", "bin", "WwiseConsole.exe");
        var original = Path.Combine(scratch, "original.wem");
        var tool = new WwiseInstallation(console, new Version(2023, 1, 0, 1), new string('A', 64));
        var reference = new WemReference(original, new string('B', 64), 100, WemCodec.Vorbis, 48000, 2);
        static Task<IReadOnlyList<WwiseInstallation>> None(CancellationToken _) =>
            Task.FromResult<IReadOnlyList<WwiseInstallation>>([]);
        Task<WwiseInstallation> InspectTool(string _, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(tool); }
        Task<WemReference> InspectReference(string _, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(reference); }

        var saved = 0;
        using (var setup = new WemEncodingViewModel(null, (_, _) => { saved++; return Task.CompletedTask; },
            CancellationToken.None, InspectTool, None, InspectReference))
        {
            await setup.InitializeAsync();
            check(setup.SelectedInstallation is null && !setup.CanContinue && setup.Message.Contains("Install Wwise", StringComparison.Ordinal),
                "WEM setup: missing tool presents installation guidance and disables Continue");
            await setup.ChooseWwiseAsync(console);
            check(setup.SelectedInstallation == tool && saved == 1 && !setup.CanContinue,
                "WEM setup: Locate inspects and remembers tool but match mode still requires a reference");
            setup.Advanced = true;
            check(!setup.CanContinue, "WEM setup: advanced mode requires an explicit codec");
            setup.Pcm16 = true;
            WemEncodingOptions? accepted = null;
            setup.Confirmed += options => accepted = options;
            await setup.ConfirmAsync();
            check(accepted is { Codec: WemCodec.Pcm16, Reference: null } && saved == 2,
                "WEM setup: located tool and explicit PCM16 continue with saved location");
        }

        var rejectSave = true;
        using (var setup = new WemEncodingViewModel(console, (_, _) => rejectSave
            ? Task.FromException(new InvalidOperationException("Settings changed; retry save.")) : Task.CompletedTask,
            CancellationToken.None, InspectTool, None, InspectReference))
        {
            await setup.InitializeAsync();
            await setup.ChooseReferenceAsync(original);
            WemEncodingOptions? accepted = null;
            setup.Confirmed += options => accepted = options;
            await setup.ConfirmAsync();
            check(accepted is null && setup.CanContinue && setup.Message.Contains("Settings changed", StringComparison.Ordinal),
                "WEM setup: settings conflict stays open and retains pending choices");
            rejectSave = false;
            await setup.ConfirmAsync();
            check(accepted is { Codec: WemCodec.Vorbis, Reference: not null },
                "WEM setup: retry after settings conflict confirms same reference");
        }

        var toolChanged = false;
        using (var setup = new WemEncodingViewModel(console, (_, _) => Task.CompletedTask,
            CancellationToken.None, (_, _) => Task.FromResult(toolChanged ? tool with { ExecutableSha256 = new string('C', 64) } : tool),
            None, InspectReference))
        {
            await setup.InitializeAsync();
            setup.Advanced = true; setup.Pcm16 = true;
            var confirmed = false;
            setup.Confirmed += _ => confirmed = true;
            toolChanged = true;
            await setup.ConfirmAsync();
            check(!confirmed && setup.CanContinue && setup.Message.Contains("installation changed", StringComparison.Ordinal),
                "WEM setup: changed Wwise executable is refused without closing choices");
            await setup.ChooseWwiseAsync(console);
            check(setup.SelectedInstallation?.ExecutableSha256 == new string('C', 64) &&
                setup.Installations.Count == 1,
                "WEM setup: locating the same Wwise path replaces its stale inspected identity");
            await setup.ConfirmAsync();
            check(confirmed, "WEM setup: refreshed Wwise identity allows retry");
        }

        var referenceChanged = false;
        using (var setup = new WemEncodingViewModel(console, (_, _) => Task.CompletedTask,
            CancellationToken.None, InspectTool, None,
            (_, _) => Task.FromResult(referenceChanged ? reference with { Sha256 = new string('D', 64) } : reference)))
        {
            await setup.InitializeAsync();
            await setup.ChooseReferenceAsync(original);
            var confirmed = false;
            setup.Confirmed += _ => confirmed = true;
            referenceChanged = true;
            await setup.ConfirmAsync();
            check(!confirmed && setup.CanContinue && setup.Message.Contains("original WEM changed", StringComparison.Ordinal),
                "WEM setup: changed original WEM is refused without closing choices");
        }

        using (var setup = new WemEncodingViewModel(null, (_, _) => Task.CompletedTask,
            CancellationToken.None, (_, _) => Task.FromException<WwiseInstallation>(new Win32Exception("Inspection unavailable")),
            None, InspectReference))
        {
            await setup.ChooseWwiseAsync(console);
            check(setup.SelectedInstallation is null && setup.Message.Contains("Inspection unavailable", StringComparison.Ordinal),
                "WEM setup: native inspection error surfaces in the dialog");
        }

        using (var cancelled = new CancellationTokenSource())
        {
            var calls = 0;
            using var setup = new WemEncodingViewModel(console, (_, _) => Task.CompletedTask, cancelled.Token,
                (_, _) => { if (++calls == 2) cancelled.Cancel(); return Task.FromResult(tool); }, None, InspectReference);
            await setup.InitializeAsync();
            setup.Advanced = true; setup.Pcm16 = true;
            var confirmed = false;
            setup.Confirmed += _ => confirmed = true;
            await setup.ConfirmAsync();
            check(!confirmed, "WEM setup: cancellation before confirmation cannot emit options");
        }

        var gate = new TaskCompletionSource<WwiseInstallation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspections = 0;
        using (var setup = new WemEncodingViewModel(console, (_, _) => Task.CompletedTask,
            CancellationToken.None, (_, _) => ++inspections == 1 ? Task.FromResult(tool) : gate.Task, None, InspectReference))
        {
            await setup.InitializeAsync();
            setup.Advanced = true; setup.Pcm16 = true;
            var confirmation = setup.ConfirmAsync();
            check(setup.IsBusy && !setup.CanChoose && !setup.CanContinue,
                "WEM setup: confirmation locks choices while identity is re-inspected");
            setup.SelectedInstallation = null;
            setup.Advanced = false;
            setup.Vorbis = true;
            gate.SetResult(tool);
            WemEncodingOptions? accepted = null;
            setup.Confirmed += options => accepted = options;
            await confirmation;
            check(setup.SelectedInstallation == tool && setup.Advanced && setup.Pcm16 &&
                accepted is { Codec: WemCodec.Pcm16 },
                "WEM setup: busy mutations cannot change the captured conversion choice");
        }
    }
}
