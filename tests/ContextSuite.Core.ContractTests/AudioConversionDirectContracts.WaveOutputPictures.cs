using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task WaveOutputPicturesAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh WAV output artwork evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += (_, _) => throw new InvalidOperationException("WAV output retains these sources without a quality prompt.");
        var reports = new List<object>();
        foreach (var name in new[] { "flac-mixed.flac", "vorbis-mixed.ogg", "opus-mixed.opus", "mp3-mixed.mp3", "m4a-tail-png.m4a", "m4a-fast-jpeg.m4a", "m4a-fast-large.m4a" })
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
                    ".m4a" => (await M4aMetadata.ReadAsync(file, default, true)).Pictures,
                    _ => OggPictureComments.Read((await OggMetadata.ReadAsync(file, default, true)).Descriptions)
                };
            }
            pictures = Mp3PictureFrames.ForOutput(pictures);
            var admissions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            var row = vm.Rows[^1]; var output = row.OutputPath;
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated && access.Admissions == admissions + 1,
                "WAV artwork direct: validated copy " + name + " " + row.Status);
            using (var file = File.OpenRead(output))
            {
                var inventory = await WaveMetadata.ReadAsync(file, default, true);
                check(inventory.Tags.Single(tag => tag.Name == "title").Value == "Artwork title" && inventory.Pictures.Length == pictures.Length &&
                    !inventory.Pictures.Where((picture, index) => !picture.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any(),
                    "WAV artwork direct: published exact APIC inventory and title " + name);
            }
            var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "WAV artwork direct: safe copy collision " + name);
            var afterConversions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [output])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == afterConversions,
                "WAV artwork direct: same-format copy is a no-op " + name);
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(original) == modified,
                "WAV artwork direct: original bytes and time unchanged " + name);
            reports.Add(new { Source = original, Output = output, SourceSha256 = Convert.ToHexString(hash), OutputSha256 = Convert.ToHexString(outputHash), Pictures = pictures.Length });
        }
        foreach (var name in new[] { "refused-0.flac", "refused-1.flac" })
        {
            var original = Path.Combine(scratch, name); File.Copy(Path.Combine(fixtures, name), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var admissions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unsupported && access.Admissions == admissions &&
                !Directory.GetFiles(scratch, Path.GetFileNameWithoutExtension(name) + "*.wav").Any() &&
                SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash),
                "WAV artwork direct: unrepresentable picture metadata cannot admit or publish " + name);
        }
        await File.WriteAllTextAsync(Path.Combine(scratch, "wave-output-artwork-direct.json"), JsonSerializer.Serialize(reports));
    }
}
