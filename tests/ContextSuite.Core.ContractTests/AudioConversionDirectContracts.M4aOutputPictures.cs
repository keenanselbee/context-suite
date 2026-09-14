using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task M4aOutputPicturesAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh M4A output artwork evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true }; var prompts = 0; var confirm = true;
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.RequiredConsent == AudioConversionConsent.LossyTranscoding, "M4A artwork direct: precise lossy quality prompt");
            if (!confirm) return null;
            await decision.RefreshAccessAsync(); ConfirmedAudioConversion? result = null; decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null); return result;
        };
        var reports = new List<object>();
        foreach (var name in new[] { "flac-mixed.flac", "vorbis-mixed.ogg", "opus-mixed.opus", "mp3-png.mp3", "mp3-jpeg.mp3", "mp3-large.mp3" })
        {
            var original = Path.Combine(scratch, name); File.Copy(Path.Combine(fixtures, name), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var modified = File.GetLastWriteTimeUtc(original);
            ImmutableArray<FlacMetadataBlock> pictures;
            using (var file = File.OpenRead(original))
            {
                pictures = Path.GetExtension(original) switch
                {
                    ".flac" => FlacMetadata.Parse(await File.ReadAllBytesAsync(original)).Blocks.Where(block => block.Type == 6).ToImmutableArray(),
                    ".mp3" => (await Mp3Metadata.ReadAsync(file, default, true)).Pictures,
                    _ => OggPictureComments.Read((await OggMetadata.ReadAsync(file, default, true)).Descriptions)
                };
            }
            pictures = M4aPictureAtoms.ForOutput(pictures);
            var admissions = access.Admissions; var promptCount = prompts;
            if (Path.GetExtension(original) != ".flac")
            {
                confirm = false; vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == admissions &&
                    !Directory.GetFiles(scratch, Path.GetFileNameWithoutExtension(name) + "*.m4a").Any(), "M4A artwork direct: declined quality creates no output " + name);
            }
            confirm = true; vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [original])); await vm.WaitForIdleAsync();
            var row = vm.Rows[^1]; var output = row.OutputPath;
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated,
                "M4A artwork direct: validated copy " + name + " " + row.Status);
            using (var file = File.OpenRead(output))
            {
                var inventory = await M4aMetadata.ReadAsync(file, default, true);
                check(inventory.Tags["title"] == "Artwork title" && inventory.Pictures.Length == pictures.Length &&
                    !inventory.Pictures.Where((picture, index) => !picture.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any(),
                    "M4A artwork direct: published exact cover inventory and title " + name);
            }
            var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
            vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "M4A artwork direct: safe copy collision " + name);
            var afterConversions = access.Admissions; var afterPrompts = prompts;
            vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [output])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == afterConversions && prompts == afterPrompts,
                "M4A artwork direct: same-format copy is a no-op " + name);
            check(prompts - promptCount == (Path.GetExtension(original) == ".flac" ? 0 : 3), "M4A artwork direct: only required quality prompts " + name);
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(original) == modified,
                "M4A artwork direct: original bytes and time unchanged " + name);
            reports.Add(new { Source = original, Output = output, SourceSha256 = Convert.ToHexString(hash), OutputSha256 = Convert.ToHexString(outputHash), Pictures = pictures.Length });
        }
        foreach (var name in new[] { "refused-0.flac", "refused-1.flac", "refused-2.flac" })
        {
            var original = Path.Combine(scratch, name); File.Copy(Path.Combine(fixtures, name), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var admissions = access.Admissions;
            var promptCount = prompts;
            vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unsupported && access.Admissions == admissions && prompts == promptCount &&
                !Directory.GetFiles(scratch, Path.GetFileNameWithoutExtension(name) + "*.m4a").Any() &&
                SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash),
                "M4A artwork direct: unrepresentable metadata refused before admission/publication " + name);
        }
        await File.WriteAllTextAsync(Path.Combine(scratch, "m4a-output-artwork-direct.json"), JsonSerializer.Serialize(reports));
    }
}
