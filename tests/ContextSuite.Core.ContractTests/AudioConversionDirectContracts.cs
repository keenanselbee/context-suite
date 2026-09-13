using System.Security.Cryptography;
using System.Collections.Immutable;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    public static async Task RunAsync(string scratch, string executable, string fixtures, Action<bool, string> check, string? artworkFixtures = null)
    {
        Directory.CreateDirectory(scratch);
        var wave = Path.Combine(scratch, "Audio.wav"); var mp3 = Path.Combine(scratch, "Compressed.mp3");
        File.Copy(Path.Combine(fixtures, "source.wav"), wave); File.Copy(Path.Combine(fixtures, "source.mp3"), mp3);
        var waveHash = SHA256.HashData(await File.ReadAllBytesAsync(wave)); var mp3Hash = SHA256.HashData(await File.ReadAllBytesAsync(mp3));
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var access = new Access { Allowed = true };
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio must not open an image planner.");
        var prompts = 0; var starts = 0; var finishes = 0; var quiet = new QuietWorkflow();
        vm.QuickBatchStarted += (request, _) => { starts++; quiet.Begin(request.RequestId, false, DateTimeOffset.UtcNow); };
        vm.QuickBatchCompleted += (request, rows) => { finishes++; quiet.Complete(request.RequestId, rows.Select(row => row.Result).ToArray()); };
        Func<AudioConversionViewModel, CancellationToken, Task<ConfirmedAudioConversion?>> prompt = (_, _) =>
            throw new InvalidOperationException("Routine conversion must not open a quality prompt.");
        vm.AudioConversionRequested += (model, token) => { prompts++; return prompt(model, token); };
        foreach (var (action, extension) in new[] { ("wav", ".wav"), ("flac", ".flac"), ("mp3", ".mp3"), ("m4a", ".m4a"), ("vorbis", ".ogg"), ("opus", ".opus") })
        {
            vm.Admit(new(Guid.NewGuid(), "convert", action, [wave])); await vm.WaitForIdleAsync();
            check(action == "wav" ? vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == 0 :
                vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath.EndsWith(extension),
                "Audio direct conversion: fixed menu target " + action);
        }
        check(prompts == 0 && starts == 6 && finishes == 6 && access.Admissions == 5 && !quiet.NeedsAttention && !vm.HasProblems,
            "Audio direct conversion: routine work uses one quiet completion per batch without a prompt");
        var processId = worker.ProcessId;
        if (artworkFixtures is not null)
        {
            foreach (var name in new[] { "two-covers", "large-cover" })
            {
                var original = Path.Combine(scratch, name + ".flac");
                File.Copy(Path.Combine(artworkFixtures, name + ".flac"), original);
                var bytes = await File.ReadAllBytesAsync(original); var digest = SHA256.HashData(bytes);
                var pictures = FlacMetadata.Parse(bytes).Blocks.Where(block => block.Type == 6).ToImmutableArray();
                var admissions = access.Admissions;
                vm.Admit(new(Guid.NewGuid(), "convert", "flac", [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == admissions,
                    "Audio artwork direct: same-format no-op starts no admission: " + name);
                foreach (var action in new[] { "vorbis", "opus" })
                {
                    var extension = action == "vorbis" ? ".ogg" : ".opus";
                    vm.Admit(new(Guid.NewGuid(), "convert", action, [original])); await vm.WaitForIdleAsync();
                    var row = vm.Rows[^1]; var output = row.OutputPath;
                    check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                        output.EndsWith(extension) && prompts == 0 && !quiet.NeedsAttention && !vm.HasProblems && worker.ProcessId == processId,
                        "Audio artwork direct: quiet validated copy without a prompt: " + name + "/" + action);
                    using (var file = File.OpenRead(output))
                    {
                        var inventory = await OggMetadata.ReadAsync(file, default, true);
                        var tags = inventory.ConversionTags(pictures);
                        check(tags["title"] == "Artwork title" && tags["artist"] == "Fixture artist" &&
                            inventory.Channels == 2 && inventory.SampleRate == 48000,
                            "Audio artwork direct: published complete pictures, descriptions, tags and audio properties: " + name + "/" + action);
                    }
                    var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
                    vm.Admit(new(Guid.NewGuid(), "convert", action, [original])); await vm.WaitForIdleAsync();
                    check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                        SHA256.HashData(await File.ReadAllBytesAsync(output)).SequenceEqual(outputHash),
                        "Audio artwork direct: repeated conversion preserves the existing copy: " + name + "/" + action);
                }
                check(SHA256.HashData(await File.ReadAllBytesAsync(original)).SequenceEqual(digest),
                    "Audio artwork direct: original hash unchanged: " + name);
            }
        }
        var folder = Path.Combine(scratch, "Captured output"); Directory.CreateDirectory(folder);
        vm.Settings = new() { Convert = new(OutputDirectory: folder), PlayCompletionSound = false };
        var before = access.Admissions;
        prompt = async (decision, _) =>
        {
            var plan = decision.Plan; access.Allowed = false; await decision.RefreshAccessAsync();
            check(!decision.CanConfirm && decision.LicenseActionLabel.Contains("Activate"), "Audio direct conversion: open quality prompt exposes activation when expired");
            access.Allowed = true; await decision.RefreshAccessAsync();
            check(decision.CanConfirm && ReferenceEquals(plan, decision.Plan) && plan.Target == AudioFormat.M4a && plan.Items.Length == 2 &&
                plan.Settings.Preferences.OutputDirectory == folder, "Audio direct conversion: activation retains target, files and settings");
            access.Allowed = false; await decision.RefreshAccessAsync();
            check(!decision.CanConfirm, "Audio direct conversion: deactivation blocks open quality prompt");
            access.Allowed = true; await decision.RefreshAccessAsync();
            vm.Settings = new() { PlayCompletionSound = false };
            ConfirmedAudioConversion? result = null; decision.Confirmed += confirmed => result = confirmed;
            decision.ConfirmCommand.Execute(null); return result;
        };
        vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [wave, mp3])); await vm.WaitForIdleAsync();
        check(prompts == 1 && access.Admissions == before + 1 && vm.Rows.TakeLast(2).All(row => row.Result.State == OperationState.Succeeded &&
            Path.GetDirectoryName(row.OutputPath) == folder) && worker.ProcessId == processId,
            "Audio direct conversion: one quality decision and admission cover mixed source formats on the same worker");
        before = access.Admissions; prompt = (_, _) => Task.FromResult<ConfirmedAudioConversion?>(null);
        vm.Admit(new(Guid.NewGuid(), "convert", "opus", [mp3])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == before,
            "Audio direct conversion: declining required changes starts no admission or publication");
        prompt = async (decision, _) =>
        {
            await decision.RefreshAccessAsync(); ConfirmedAudioConversion? result = null; decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null); access.Allowed = false; return result;
        };
        vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [mp3])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Failed && vm.Rows[^1].Status.Contains("Activate"),
            "Audio direct conversion: final admission rejects access revoked after quality confirmation");
        prompt = async (decision, _) =>
        {
            await decision.RefreshAccessAsync(); ConfirmedAudioConversion? result = null; decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null); return result;
        };
        access.Allowed = true;
        var rowsBefore = vm.Rows.Count; vm.RetryFailed(); vm.RetryFailed(); await vm.WaitForIdleAsync();
        check(vm.Rows.Count == rowsBefore + 1 && vm.Rows[^1].Action == "m4a" && vm.Rows[^1].Result.State == OperationState.Succeeded && !vm.CanRetry,
            "Audio direct conversion: reactivation retry retains target and suppresses duplicate clicks");
        access.Allowed = false; vm.Admit(new(Guid.NewGuid(), "convert", "flac", [wave])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Failed && vm.CanRetry, "Audio direct conversion: routine expired action offers retry");
        access.Allowed = true; vm.Settings = new() { Convert = new(OutputDirectory: folder), PlayCompletionSound = false };
        vm.RetryFailed(); vm.Settings = new() { PlayCompletionSound = false }; await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Action == "flac" && Path.GetDirectoryName(vm.Rows[^1].OutputPath) == folder && vm.Rows[^1].Settings.Preferences.OutputDirectory == folder,
            "Audio direct conversion: retry captures current settings without changing its target");
        prompt = async (_, token) =>
        {
            vm.CancelCommand.Execute(null); await Task.Delay(Timeout.Infinite, token); return null;
        };
        before = access.Admissions; vm.Admit(new(Guid.NewGuid(), "convert", "opus", [mp3])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == before,
            "Audio direct conversion: cancellation while deciding starts no work");
        var unsupported = Path.Combine(scratch, "Not audio.txt"); await File.WriteAllTextAsync(unsupported, "Disposable non-audio fixture.");
        vm.Admit(new(Guid.NewGuid(), "convert", "flac", [unsupported, wave])); await vm.WaitForIdleAsync();
        check(vm.Rows[^2].Result.State == OperationState.Failed && vm.Rows[^1].Result.State == OperationState.Succeeded,
            "Audio direct conversion: one unreadable audio input does not prevent a supported file");
        var absentWorker = new WorkerClient(Path.Combine(scratch, "missing", "ContextSuite.Worker.exe")); var absentAccess = new Access();
        await using (var absent = new MainViewModel(absentWorker, new(), publisher, absentAccess))
        {
            absent.Admit(new(Guid.NewGuid(), "convert", "flac", [wave])); await absent.WaitForIdleAsync();
            check(absent.Rows.Single().Result.State == OperationState.Unsupported && absent.Rows.Single().Status.Contains("unavailable in this build") &&
                absentWorker.ProcessId is null && absentAccess.Admissions == 0, "Audio direct conversion: missing engine starts no worker or trial");
        }
        check(SHA256.HashData(await File.ReadAllBytesAsync(wave)).SequenceEqual(waveHash) && SHA256.HashData(await File.ReadAllBytesAsync(mp3)).SequenceEqual(mp3Hash) &&
            !Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(), "Audio direct conversion: originals and reservation cleanup survive all paths");
        await File.WriteAllTextAsync(Path.Combine(scratch, "audio-conversion-direct.json"), JsonSerializer.Serialize(new
        { prompts, starts, finishes, access.Admissions, rows = vm.Rows.Select(row => new { row.Action, row.Result, row.Settings }) }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class Access : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Licensed" : "Activate your license to convert."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected optimization admission.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Admissions++; return Task.FromResult(new OperationAdmission(new(Allowed, Allowed ? "Licensed" : "Activate your license to convert."), confirmed.Plan.BatchId)); }
    }
    private sealed class RefusingRecycler : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) => throw new InvalidOperationException("Direct copy tests cannot recycle.");
    }
}
