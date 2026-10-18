using System.Text;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Installation;
using ContextSuite.Core.Operations;

internal static class InstalledToolsContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var directory = Path.Combine(scratch, "installed-tools");
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, InstalledTools.FileName);
        var source = Path.Combine(directory, "input.txt");
        await File.WriteAllTextAsync(source, "Selection checks must preserve this file.");
        var original = await File.ReadAllBytesAsync(source);
        var originalTime = File.GetLastWriteTimeUtc(source);
        var legacyDirectory = Path.Combine(directory, "legacy");
        Directory.CreateDirectory(legacyDirectory);
        var legacy = InstalledTools.Load(legacyDirectory);
        check(legacy.Analyze && legacy.Convert && legacy.Optimize, "installed tools: absent legacy marker preserves all tools");
        var names = new[] { "Analyze", "Convert", "Optimize" };
        var operations = new[] { "analyze", "convert", "optimize" };
        var actions = new[] { "open-details", "png", "auto" };
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var loaded = await store.LoadAsync();

        for (var mask = 1; mask < 8; mask++)
        {
            var selected = names.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            var json = JsonSerializer.Serialize(new { schema = 1, tools = selected });
            await File.WriteAllTextAsync(marker, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: mask % 2 == 0));
            var tools = InstalledTools.Load(directory);
            check(operations.Select(tools.IncludesOperation).SequenceEqual(Enumerable.Range(0, 3).Select(index => (mask & (1 << index)) != 0)),
                $"installed tools: selection {mask} reads exact tool membership with or without a UTF-8 BOM");

            for (var index = 0; index < 3; index++)
            {
                var worker = new WorkerClient(Path.Combine(directory, "absent-worker.exe"));
                await using var viewModel = new MainViewModel(worker, installedTools: tools);
                var reply = viewModel.Admit(new(Guid.NewGuid(), operations[index], actions[index], [source]));
                var included = (mask & (1 << index)) != 0;
                check(reply.Accepted == included, $"installed tools: selection {mask} admits only installed {names[index]}");
                if (!included)
                    check(viewModel.Rows.Count == 0 && !viewModel.IsBusy && worker.ProcessId is null && reply.Message.Contains("installing", StringComparison.Ordinal),
                        $"installed tools: disabled {names[index]} creates no work and explains installation selection");
                else
                    await viewModel.WaitForIdleAsync();
            }

            await using (var settingsOwner = new MainViewModel(new WorkerClient(Path.Combine(directory, "absent-worker.exe")), installedTools: tools))
            {
                var settingsRequests = 0;
                settingsOwner.SettingsRequested += _ => settingsRequests++;
                check(settingsOwner.Admit(null).Accepted &&
                    settingsOwner.Admit(new(Guid.NewGuid(), "convert", "settings", [])).Accepted &&
                    settingsOwner.Admit(new(Guid.NewGuid(), "optimize", "settings", [])).Accepted && settingsRequests == 2 && settingsOwner.Rows.Count == 0,
                    $"installed tools: selection {mask} keeps standalone and Settings entry points accessible");
            }
            var editor = new SettingsViewModel(store, loaded, "convert", false, tools);
            var expectedSection = tools.Convert ? 0 : tools.Optimize ? 1 : -1;
            check(editor.HasConvert == tools.Convert && editor.HasOptimize == tools.Optimize && editor.SelectedSection == expectedSection &&
                editor.SaveCommand.CanExecute(null) == (tools.Convert || tools.Optimize),
                $"installed tools: selection {mask} exposes only available processing settings");
            editor.SelectedSection = 1;
            check(editor.SelectedSection == (tools.Optimize ? 1 : tools.Convert ? 0 : -1),
                $"installed tools: selection {mask} cannot select an absent settings tab");
        }

        foreach (var json in new[]
        {
            "", "null", "[]", "{}", "{", "{\"schema\":2,\"tools\":[\"Analyze\"]}",
            "{\"schema\":\"1\",\"tools\":[\"Analyze\"]}", "{\"schema\":1,\"tools\":[]}",
            "{\"schema\":1,\"tools\":[\"Analyze\",\"Analyze\"]}", "{\"schema\":1,\"tools\":[\"analyze\"]}",
            "{\"schema\":1,\"tools\":[null]}", "{\"schema\":1,\"tools\":[1]}", "{\"schema\":1,\"tools\":null}",
            "{\"schema\":1,\"tools\":[\"Other\"]}", "{\"schema\":1,\"schema\":1,\"tools\":[\"Analyze\"]}",
            "{\"schema\":1,\"tools\":[\"Analyze\"],\"extra\":true}", new string(' ', 4097)
        })
        {
            await File.WriteAllTextAsync(marker, json);
            try { InstalledTools.Load(directory); check(false, "installed tools: malformed present marker must fail closed"); }
            catch (InstallationToolsException error)
            {
                check(error.Message.Contains("Reinstall", StringComparison.Ordinal) && error.InnerException is not null,
                    "installed tools: malformed present marker fails with reinstallation guidance and cause");
            }
        }
        using (var locked = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { InstalledTools.Load(directory); check(false, "installed tools: unreadable marker must fail closed"); }
            catch (InstallationToolsException) { check(true, "installed tools: unreadable marker never enables legacy fallback"); }
        }
        await using (var defaultViewModel = new MainViewModel(new WorkerClient(Path.Combine(directory, "absent-worker.exe"))))
            check(defaultViewModel.InstalledTools == InstalledTools.All, "installed tools: existing development callers retain all tools");
        var remaining = await File.ReadAllBytesAsync(source);
        check(original.SequenceEqual(remaining) && File.GetLastWriteTimeUtc(source) == originalTime,
            "installed tools: admission tests preserve source bytes and timestamp");
    }
}
