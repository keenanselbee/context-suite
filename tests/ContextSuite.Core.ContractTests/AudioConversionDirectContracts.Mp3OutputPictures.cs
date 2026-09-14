using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task Mp3OutputPicturesAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh MP3 output artwork evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true }; var prompts = 0; var confirm = true;
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.RequiredConsent == AudioConversionConsent.LossyTranscoding, "MP3 artwork direct: precise lossy quality prompt");
            if (!confirm) return null;
            await decision.RefreshAccessAsync(); ConfirmedAudioConversion? result = null; decision.Confirmed += value => result = value;
            decision.ConfirmCommand.Execute(null); return result;
        };
        var reports = new List<object>();
        foreach (var name in new[] { "flac-mixed.flac", "vorbis-mixed.ogg", "opus-mixed.opus", "m4a-tail-png.m4a", "m4a-fast-jpeg.m4a", "m4a-fast-large.m4a" })
        {
            var original = Path.Combine(scratch, name); File.Copy(Path.Combine(fixtures, name), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var modified = File.GetLastWriteTimeUtc(original);
            ImmutableArray<FlacMetadataBlock> pictures;
            using (var file = File.OpenRead(original))
            {
                pictures = Path.GetExtension(original) switch
                {
                    ".flac" => FlacMetadata.Parse(await File.ReadAllBytesAsync(original)).Blocks.Where(block => block.Type == 6).ToImmutableArray(),
                    ".m4a" => (await M4aMetadata.ReadAsync(file, default, true)).Pictures,
                    _ => OggPictureComments.Read((await OggMetadata.ReadAsync(file, default, true)).Descriptions)
                };
            }
            pictures = Mp3PictureFrames.ForOutput(pictures);
            var admissions = access.Admissions; var promptCount = prompts;
            if (Path.GetExtension(original) != ".flac")
            {
                confirm = false; vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == admissions &&
                    !Directory.GetFiles(scratch, Path.GetFileNameWithoutExtension(name) + "*.mp3").Any(), "MP3 artwork direct: declined quality creates no output " + name);
            }
            confirm = true; vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [original])); await vm.WaitForIdleAsync();
            var row = vm.Rows[^1]; var output = row.OutputPath;
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated,
                "MP3 artwork direct: validated copy " + name + " " + row.Status);
            using (var file = File.OpenRead(output))
            {
                var inventory = await Mp3Metadata.ReadAsync(file, default, true);
                check(inventory.Tags["title"] == "Artwork title" && inventory.Pictures.Length == pictures.Length &&
                    !inventory.Pictures.Where((picture, index) => !picture.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any(),
                    "MP3 artwork direct: published exact APIC inventory and title " + name);
            }
            var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
            vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "MP3 artwork direct: safe copy collision " + name);
            var afterConversions = access.Admissions; var afterPrompts = prompts;
            vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [output])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == afterConversions && prompts == afterPrompts,
                "MP3 artwork direct: same-format copy is a no-op " + name);
            check(prompts - promptCount == (Path.GetExtension(original) == ".flac" ? 0 : 3), "MP3 artwork direct: only required quality prompts " + name);
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(original) == modified,
                "MP3 artwork direct: original bytes and time unchanged " + name);
            reports.Add(new { Source = original, Output = output, SourceSha256 = Convert.ToHexString(hash), OutputSha256 = Convert.ToHexString(outputHash), Pictures = pictures.Length });
        }
        await File.WriteAllTextAsync(Path.Combine(scratch, "mp3-output-artwork-direct.json"), JsonSerializer.Serialize(reports));
    }
}
