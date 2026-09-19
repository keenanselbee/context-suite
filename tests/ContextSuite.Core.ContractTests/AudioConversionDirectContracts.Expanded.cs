using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    public static async Task ExpandedAsync(string scratch, string executable, string manifest, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use new direct-command evidence.");
        var fixtures = JsonSerializer.Deserialize<ExpandedAudioWorkerContracts.Fixture[]>(await File.ReadAllTextAsync(manifest))!;
        string[] names = ["tone-aiff-16-2", "tone-aifc-float32-2", "expanded-wmav1", "standard-48000-2-16-48000",
            "pro-48000-2-24-48000", "lossless-48000-2-24-48000", "voice-22050-1-16-22050"];
        var selected = names.Select(name => fixtures.Single(item => item.Name == name)).ToArray();
        Directory.CreateDirectory(scratch);
        var paths = new Dictionary<string, string>();
        var timestamps = new Dictionary<string, DateTime>();
        foreach (var fixture in selected)
        {
            check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Path))) == fixture.Sha256,
                "Expanded direct: verified input " + fixture.Name);
            var path = Path.Combine(scratch, fixture.Name + Path.GetExtension(fixture.Path));
            File.Copy(fixture.Path, path); paths.Add(fixture.Name, path);
            timestamps.Add(fixture.Name, File.GetLastWriteTimeUtc(path));
        }
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "workers"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new Exception("Expanded audio opened an image planner.");
        var prompts = 0; var starts = 0; var finishes = 0; var quiet = new QuietWorkflow();
        vm.QuickBatchStarted += (request, _) => { starts++; quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow); };
        vm.QuickBatchCompleted += (request, rows) => { finishes++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
        var expectedConsent = AudioConversionConsent.None;
        var expectedCount = 1;
        var decline = false;
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.RequiredConsent == expectedConsent && expectedConsent != AudioConversionConsent.None &&
                decision.Plan.Items.Count(item => item.CanExecute) == expectedCount, "Expanded direct: exact required consent and batch size");
            if (decline) return null;
            var plan = decision.Plan;
            access.Allowed = false; await decision.RefreshAccessAsync();
            check(!decision.CanConfirm && decision.LicenseActionLabel.Contains("Activate"), "Expanded direct: expired prompt exposes activation");
            access.Allowed = true; await decision.RefreshAccessAsync();
            check(decision.CanConfirm && ReferenceEquals(decision.Plan, plan), "Expanded direct: activation preserves the selected plan");
            ConfirmedAudioConversion? result = null;
            decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null);
            return result;
        };
        var outputs = new Dictionary<string, string>();
        foreach (var fixture in selected)
        {
            foreach (var (action, extension) in new[] { ("wav", ".wav"), ("flac", ".flac"), ("mp3", ".mp3"),
                ("m4a", ".m4a"), ("vorbis", ".ogg"), ("opus", ".opus") })
            {
                var lossy = fixture.Codec.StartsWith("wma", StringComparison.Ordinal) && fixture.Codec != "wmalossless";
                var floating = fixture.Codec.StartsWith("pcm_f", StringComparison.Ordinal);
                expectedConsent = lossy && action is "mp3" or "m4a" or "vorbis" or "opus" ? AudioConversionConsent.LossyTranscoding : 0;
                // Windows Voice supplies PCM16, which the FLAC recipe preserves exactly.
                if (action == "flac" && (lossy || floating) && fixture.Codec != "wmavoice") expectedConsent |= AudioConversionConsent.PrecisionReduction;
                if (action == "opus" && fixture.Rate != 48000) expectedConsent |= AudioConversionConsent.Resampling;
                var priorPrompts = prompts; var priorAdmissions = access.Admissions;
                vm.Admit(new(Guid.NewGuid(), "convert", action, [paths[fixture.Name]])); await vm.WaitForIdleAsync();
                var row = vm.Rows[^1];
                check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                    File.Exists(row.OutputPath) && row.OutputPath != row.Path && Path.GetExtension(row.OutputPath) == extension,
                    "Expanded direct: validated copy " + fixture.Name + "/" + action + ": " + row.Status);
                check(prompts == priorPrompts + (expectedConsent == 0 ? 0 : 1) && access.Admissions == priorAdmissions + 1 &&
                    starts == finishes && !quiet.NeedsAttention && !vm.HasProblems, "Expanded direct: one admission and quiet completion with only necessary prompts");
                outputs.Add(row.OutputPath, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(row.OutputPath))));
            }
        }
        var aiff = paths[names[0]]; var standard = paths[names[3]]; var voice = paths[names[6]];
        var invalid = Path.Combine(scratch, "not-audio.wma"); await File.WriteAllTextAsync(invalid, "This is not an audio container.");
        expectedCount = 3; expectedConsent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling;
        var before = access.Admissions; var beforePrompts = prompts;
        vm.Admit(new(Guid.NewGuid(), "convert", "opus", [invalid, aiff, standard, voice])); await vm.WaitForIdleAsync();
        check(vm.Rows[^4].Result.State == OperationState.Failed && vm.Rows.TakeLast(3).All(row => row.Result.State == OperationState.Succeeded) &&
            access.Admissions == before + 1 && prompts == beforePrompts + 1 && quiet.NeedsAttention,
            "Expanded direct: one mixed batch keeps the unreadable file visible and converts the three supported files");
        expectedCount = 1; expectedConsent = AudioConversionConsent.LossyTranscoding;
        decline = true; before = access.Admissions;
        vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [standard])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == before,
            "Expanded direct: declining quality changes publishes nothing and admits no work");
        decline = false;
        // Use an independent result list so RetryFailed retries this access failure alone.
        var retryWorker = new WorkerClient(executable, Path.Combine(scratch, "retry-workers"));
        await using (var retry = new MainViewModel(retryWorker, new() { PlayCompletionSound = false }, publisher, access))
        {
            access.Allowed = false;
            retry.Admit(new(Guid.NewGuid(), "convert", "flac", [aiff])); await retry.WaitForIdleAsync();
            check(retry.Rows.Single().Result.State == OperationState.Failed && retry.CanRetry && retry.Rows.Single().Status.Contains("Activate"),
                "Expanded direct: expired access blocks a routine copy and exposes retry");
            access.Allowed = true;
            var folder = Path.Combine(scratch, "retry-output"); Directory.CreateDirectory(folder);
            retry.Settings = new() { Convert = new(OutputDirectory: folder), PlayCompletionSound = false };
            retry.RetryFailed(); retry.RetryFailed(); await retry.WaitForIdleAsync();
            check(retry.Rows.Count == 2 && retry.Rows[^1].Result.State == OperationState.Succeeded && retry.Rows[^1].Action == "flac" &&
                Path.GetDirectoryName(retry.Rows[^1].OutputPath) == folder && !retry.CanRetry,
                "Expanded direct: activation retry retains target, captures settings and suppresses duplicate clicks");
        }
        var noVoicePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(executable))!, "worker-no-voice", Path.GetFileName(executable));
        var missingWorker = new WorkerClient(noVoicePath, Path.Combine(scratch, "missing-voice-workers"));
        await using (var missingVoice = new MainViewModel(missingWorker, new(), publisher, access))
        {
            before = access.Admissions;
            missingVoice.Admit(new(Guid.NewGuid(), "convert", "wav", [voice, aiff])); await missingVoice.WaitForIdleAsync();
            check(missingVoice.Rows[0].Result.State == OperationState.Unsupported && missingVoice.Rows[1].Result.State == OperationState.Succeeded &&
                access.Admissions == before + 1, "Expanded direct: absent Voice provider refuses Voice without preventing AIFF");
        }
        var absentWorker = new WorkerClient(Path.Combine(scratch, "absent", "ContextSuite.Worker.exe"));
        await using (var absent = new MainViewModel(absentWorker, new(), publisher, access))
        {
            before = access.Admissions;
            absent.Admit(new(Guid.NewGuid(), "convert", "wav", [aiff, standard, voice])); await absent.WaitForIdleAsync();
            check(absent.Rows.All(row => row.Result.State == OperationState.Unsupported && row.Status.Contains("unavailable in this build")) &&
                absentWorker.ProcessId is null && access.Admissions == before, "Expanded direct: absent engine starts neither worker nor admission");
        }
        foreach (var fixture in selected)
            check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(paths[fixture.Name]))) == fixture.Sha256 &&
                File.GetLastWriteTimeUtc(paths[fixture.Name]) == timestamps[fixture.Name],
                "Expanded direct: original retained " + fixture.Name);
        foreach (var output in outputs)
            check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output.Key))) == output.Value, "Expanded direct: earlier copy retained " + Path.GetFileName(output.Key));
        check(!Directory.EnumerateFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(), "Expanded direct: no abandoned publication files");
        await File.WriteAllTextAsync(Path.Combine(scratch, "expanded-direct.json"), JsonSerializer.Serialize(new
        { prompts, starts, finishes, access.Admissions, rows = vm.Rows.Select(row => new { row.Action, row.Result }) }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
