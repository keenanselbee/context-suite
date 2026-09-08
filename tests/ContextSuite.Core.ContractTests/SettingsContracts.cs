using ContextSuite.Application.Infrastructure;
using ContextSuite.Application;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class SettingsContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var directory = Path.Combine(scratch, "settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        var original = await store.LoadAsync();
        check(original.CanSave && original.Revision is null && !original.Settings.Convert.AllowReplacingOriginals &&
            !original.Settings.Optimize.AllowReplacingOriginals, "settings: missing file uses copy-only defaults");
        var snapshot = original.Settings.Capture("convert");
        var changed = original.Settings with { Convert = new(true, directory), PlayCompletionSound = false };
        var saved = await store.SaveAsync(changed, original);
        check((await store.LoadAsync()).Settings == changed, "settings: typed roundtrip");
        check(snapshot.Preferences == new ToolSettings(), "settings: captured batch remains unchanged");
        check(snapshot.PlayCompletionSound && !changed.Capture("optimize").PlayCompletionSound, "settings: completion mute captured per batch");
        check(!changed.Capture("optimize").Preferences.AllowReplacingOriginals, "settings: per-tool replacement consent");
        await RejectAsync(() => store.SaveAsync(new(), original), check, "settings: reject stale save");

        var oldBytes = await File.ReadAllBytesAsync(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await RejectAsync(() => store.SaveAsync(new(), saved), check, "settings: locked destination rejects save");
        var afterFailure = await File.ReadAllBytesAsync(path);
        check(oldBytes.SequenceEqual(afterFailure), "settings: failed save retains previous bytes");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            await RejectAsync(() => store.SaveAsync(new(), saved, cancel.Token), check, "settings: cancelled save");
        }
        var afterCancel = await File.ReadAllBytesAsync(path);
        check(oldBytes.SequenceEqual(afterCancel), "settings: cancelled save retains previous bytes");

        foreach (var text in new[] { "{\"SchemaVersion\":999}", "{\"SchemaVersion\":\"future\"}", "{}", "null" })
        {
            await File.WriteAllTextAsync(path, text);
            var unknown = await store.LoadAsync();
            check(!unknown.CanSave && unknown.Warning is not null, "settings: unknown version is read-only");
            await RejectAsync(() => store.SaveAsync(new(), unknown), check, "settings: unknown schema is not overwritten");
            check(await File.ReadAllTextAsync(path) == text, "settings: unknown schema bytes preserved");
        }
        foreach (var text in new[] { "", "{", "{\"SchemaVersion\":1," })
        {
            await File.WriteAllTextAsync(path, text);
            var damaged = await store.LoadAsync();
            check(damaged.CanSave && damaged.Warning is not null && damaged.Settings == new SuiteSettings(),
                "settings: malformed/partial JSON uses safe defaults");
            await store.SaveAsync(new(), damaged);
            check((await store.LoadAsync()).Warning is null, "settings: explicit save repairs malformed settings");
        }
        await File.WriteAllTextAsync(path,
            "{\"SchemaVersion\":1,\"Convert\":{\"AllowReplacingOriginals\":\"yes\",\"OutputDirectory\":\"relative\"},\"Optimize\":{\"AllowReplacingOriginals\":true}}");
        var repaired = await store.LoadAsync();
        check(repaired.Settings.Convert == new ToolSettings() && repaired.Settings.Optimize.AllowReplacingOriginals &&
            repaired.Warning is not null, "settings: repair invalid fields independently");
        await RejectAsync(() => store.SaveAsync(new() { Convert = new(false, "relative") }, repaired), check,
            "settings: cannot persist relative output folder");
        await File.WriteAllTextAsync(path, new string(' ', 65537));
        check(!(await store.LoadAsync()).CanSave, "settings: bounded input size");

        var editorStore = new SettingsStore(Path.Combine(directory, "editor.json"));
        var editor = new SettingsViewModel(editorStore, await editorStore.LoadAsync(), "optimize", false);
        check(editor.SelectedSection == 1 && !editor.CanAllowReplacement && editor.NamingExample.Contains("Optimized", StringComparison.Ordinal),
            "settings UI: correct section and unverified replacement disabled");
        editor.Convert.OutputDirectory = directory;
        LoadedSettings? eventResult = null;
        editor.Saved += result => eventResult = result;
        await editor.SaveAsync();
        check(eventResult is not null && eventResult.Settings.Convert.OutputDirectory == directory &&
            (await editorStore.LoadAsync()).Settings == eventResult.Settings && !editor.IsSaving,
            "settings UI: Save persists drafts and publishes immutable preferences");
        var readOnlyEditor = new SettingsViewModel(store, await store.LoadAsync(), "convert", false);
        check(!readOnlyEditor.SaveCommand.CanExecute(null), "settings UI: unavailable settings cannot be saved");

        var allowed = new SuiteSettings { Convert = new(true) }.Capture("convert");
        check(allowed.SelectOutput(false, false, true, false).Mode == OutputMode.SiblingCopy,
            "policy: quick action remains a copy despite permission");
        check(allowed.SelectOutput(true, true, false, true).Mode == OutputMode.RecoverableReplacement,
            "policy: verified confirmed replacement");
        foreach (var policy in new Action[]
        {
            () => snapshot.SelectOutput(true, true, false, true),
            () => allowed.SelectOutput(true, false, false, true),
            () => allowed.SelectOutput(true, true, true, true),
            () => allowed.SelectOutput(true, true, false, false),
            () => changed.Capture("convert").SelectOutput(true, true, false, true),
            () => changed.Capture("analyze").SelectOutput(false, false, false, false)
        }) await RejectAsync(() => { policy(); return Task.CompletedTask; }, check, "policy: unsafe publication choice rejected");
        check(!snapshot.SelectOutput(false, false, false, false).SkipIfLarger &&
            new SuiteSettings().Capture("optimize").SelectOutput(false, false, false, false).SkipIfLarger,
            "policy: only Optimize defaults to skip larger");

        var cases = new[]
        {
            ("gamma.webp", "convert", "png", 1, "gamma - Converted.png"),
            ("gamma.webp", "optimize", ".WEBP", 2, "gamma - Optimized (2).webp"),
            ("gamma - Copy (2).webp", "convert", "png", 1, "gamma - Copy (2) - Converted.png"),
            ("gamma - Optimized.webp", "optimize", "webp", 1, "gamma - Optimized - Optimized.webp"),
            ("猫.été.png", "convert", "webp", 3, "猫.été - Converted (3).webp")
        };
        foreach (var (source, operation, extension, ordinal, expected) in cases)
            check(OutputNames.Create(source, operation, extension, ordinal) == expected, "names: " + expected);
        check(OutputNames.Create("gamma.png", "convert", "webp", replaceSource: true) == "gamma.webp",
            "names: explicit replacement keeps basename");
        check(OutputNames.Create("gamma.png", "convert", "webp", 2, replaceSource: true) == "gamma (2).webp",
            "names: replacement never claims unrelated destination");
        check(OutputNames.Create("gamma.dds", "convert", "dds", representation: new(DdsCompression.BC7, TextureTransfer.Srgb)) ==
            "gamma - BC7-sRGB.dds", "names: meaningful DDS representation");
        check(new DdsRepresentation(DdsCompression.BC5, TextureTransfer.Linear, true).Suffix == "BC5-SNORM" &&
            new DdsRepresentation(DdsCompression.BC6H, TextureTransfer.Linear).Suffix == "BC6H-UF16", "names: signed/data DDS representations");
        foreach (var action in new Action[]
        {
            () => OutputNames.Create("gamma.png", "convert", "../png"),
            () => OutputNames.Create("gamma.png", "convert", "png", 0),
            () => OutputNames.Create("gamma.png", "analyze", "png"),
            () => OutputNames.Create(new string('a', 250) + ".png", "convert", "png"),
            () => OutputNames.Create("gamma.dds", "convert", "png", representation: new(DdsCompression.BC7, TextureTransfer.Srgb)),
            () => _ = new DdsRepresentation(DdsCompression.BC5, TextureTransfer.Srgb).Suffix,
            () => _ = new DdsRepresentation(DdsCompression.BC7, TextureTransfer.Linear, true).Suffix,
            () => _ = new DdsRepresentation((DdsCompression)999, TextureTransfer.Linear).Suffix
        }) await RejectAsync(() => { action(); return Task.CompletedTask; }, check, "names: invalid or unsafe name rejected");
        // Preserve unique scratch evidence; never recursively clean a caller-provided path.
    }

    private static async Task RejectAsync(Func<Task> action, Action<bool, string> check, string name)
    {
        try { await action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException)
        { check(true, name); return; }
        throw new InvalidOperationException("FAILED to reject: " + name);
    }
}
