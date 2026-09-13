using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task Mp3PicturesAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("MP3 artwork evidence directory already exists.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true }; var prompts = 0; var confirm = true;
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.RequiredConsent == (decision.Plan.Target == AudioFormat.Flac ? AudioConversionConsent.PrecisionReduction : AudioConversionConsent.LossyTranscoding),
                "MP3 artwork direct: quality prompt has precise required consent");
            if (!confirm) return null;
            await decision.RefreshAccessAsync(); ConfirmedAudioConversion? result = null; decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null); return result;
        };
        foreach (var name in new[] { "v2-e1", "v3-e1", "v4-e3" })
        {
            var original = Path.Combine(scratch, name + ".mp3"); File.Copy(Path.Combine(fixtures, name + ".mp3"), original);
            var bytes = await File.ReadAllBytesAsync(original); var digest = SHA256.HashData(bytes);
            using var input = new MemoryStream(bytes); var pictures = (await Mp3Metadata.ReadAsync(input, default, true)).Pictures;
            var admissions = access.Admissions; var promptCount = prompts;
            vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == admissions && prompts == promptCount,
                "MP3 artwork direct: no-op starts no prompt, admission or publication " + name);
            confirm = false;
            vm.Admit(new(Guid.NewGuid(), "convert", "flac", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == admissions && !Directory.GetFiles(scratch, name + "*.flac").Any(),
                "MP3 artwork direct: declining precision starts no work " + name);
            confirm = true;
            foreach (var action in new[] { "flac", "vorbis", "opus" })
            {
                vm.Admit(new(Guid.NewGuid(), "convert", action, [original])); await vm.WaitForIdleAsync();
                var row = vm.Rows[^1]; var output = row.OutputPath;
                check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated,
                    "MP3 artwork direct: validated copy publication " + name + "/" + action + " " + row.Status);
                using (var file = File.OpenRead(output))
                {
                    if (action == "flac")
                    {
                        var header = FlacMetadata.Parse(await File.ReadAllBytesAsync(output)); var actual = header.Blocks.Where(block => block.Type == 6).ToArray();
                        check(actual.Length == pictures.Length && actual.Where((block, i) => !block.Data.AsSpan().SequenceEqual(pictures[i].Data.AsSpan())).Count() == 0 &&
                            FlacConversionMetadata.Read(header, true)["title"] == "Artwork title", "MP3 artwork direct: exact published FLAC pictures and title " + name);
                    }
                    else check((await OggMetadata.ReadAsync(file, default, true)).ConversionTags(pictures)["title"] == "Artwork title",
                        "MP3 artwork direct: exact published Ogg pictures and title " + name + "/" + action);
                }
                var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
                vm.Admit(new(Guid.NewGuid(), "convert", action, [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                    SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "MP3 artwork direct: collision keeps prior copy " + name + "/" + action);
            }
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(digest), "MP3 artwork direct: original remains identical " + name);
        }
        check(!Directory.GetFiles(scratch, ".context-suite-*.tmp", SearchOption.AllDirectories).Any(), "MP3 artwork direct: reservations cleaned");
        await File.WriteAllTextAsync(Path.Combine(scratch, "mp3-artwork-direct.json"), JsonSerializer.Serialize(new { Prompts = prompts, access.Admissions,
            Rows = vm.Rows.Select(row => new { row.OutputPath, row.Status, row.Result.State }).ToArray() }));
    }
}
