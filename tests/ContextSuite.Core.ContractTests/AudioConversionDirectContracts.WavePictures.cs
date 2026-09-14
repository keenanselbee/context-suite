using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task WavePicturesAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh WAV artwork evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += (_, _) => throw new InvalidOperationException("These PCM16/48kHz fixtures need no quality prompt.");
        var reports = new List<object>();
        foreach (var name in new[] { "v2-e1-before-lower", "v3-e1-after-upper", "v4-e3-before-upper", "neutral", "large" })
        {
            var original = Path.Combine(scratch, name + ".wav"); File.Copy(Path.Combine(fixtures, name + ".wav"), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var modified = File.GetLastWriteTimeUtc(original);
            ImmutableArray<FlacMetadataBlock> pictures;
            using (var file = File.OpenRead(original)) pictures = (await WaveMetadata.ReadAsync(file, default, true)).Pictures;
            foreach (var target in name is "neutral" or "large" ? new[] { "flac", "mp3", "m4a", "vorbis", "opus" } : new[] { "flac", "mp3", "vorbis", "opus" })
            {
                var admissions = access.Admissions;
                vm.Admit(new(Guid.NewGuid(), "convert", target, [original])); await vm.WaitForIdleAsync();
                var row = vm.Rows[^1]; var output = row.OutputPath;
                check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated && access.Admissions == admissions + 1,
                    "WAV artwork direct: validated copy without routine prompt " + name + "/" + target + " " + row.Status);
                ImmutableArray<FlacMetadataBlock> actual; IReadOnlyDictionary<string, string> tags;
                using (var file = File.OpenRead(output))
                {
                    if (target == "flac")
                    {
                        var header = FlacMetadata.Parse(await File.ReadAllBytesAsync(output));
                        actual = header.Blocks.Where(block => block.Type == 6).ToImmutableArray(); tags = FlacConversionMetadata.Read(header, true);
                    }
                    else if (target == "mp3")
                    { var inventory = await Mp3Metadata.ReadAsync(file, default, true); actual = inventory.Pictures; tags = inventory.Tags; }
                    else if (target == "m4a")
                    { var inventory = await M4aMetadata.ReadAsync(file, default, true); actual = inventory.Pictures; tags = inventory.Tags; }
                    else
                    {
                        var inventory = await OggMetadata.ReadAsync(file, default, true);
                        actual = OggPictureComments.Read(inventory.Descriptions); tags = inventory.ConversionTags(pictures);
                    }
                }
                check(tags["title"] == "Artwork title" && actual.Length == pictures.Length &&
                    actual.Zip(pictures).All(pair => pair.First.Data.AsSpan().SequenceEqual(pair.Second.Data.AsSpan())),
                    "WAV artwork direct: published complete ordered covers and title " + name + "/" + target);
                var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
                vm.Admit(new(Guid.NewGuid(), "convert", target, [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                    SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "WAV artwork direct: collision keeps previous output " + name + "/" + target);
                var afterConversions = access.Admissions;
                vm.Admit(new(Guid.NewGuid(), "convert", target, [output])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == afterConversions, "WAV artwork direct: output already target skips admission " + name + "/" + target);
                reports.Add(new { Source = original, Target = target, Output = output, SourceSha256 = Convert.ToHexString(hash), OutputSha256 = Convert.ToHexString(outputHash), Pictures = pictures.Length });
            }
            var beforeSkip = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == beforeSkip, "WAV artwork direct: original same-format no-op " + name);
            if (name is not ("neutral" or "large"))
            {
                vm.Admit(new(Guid.NewGuid(), "convert", "m4a", [original])); await vm.WaitForIdleAsync();
                check(vm.Rows[^1].Result.State == OperationState.Unsupported && access.Admissions == beforeSkip && !File.Exists(Path.ChangeExtension(original, ".m4a")),
                    "WAV artwork direct: M4A cannot discard picture labels/descriptions " + name);
            }
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(original) == modified,
                "WAV artwork direct: original bytes and time unchanged " + name);
        }
        foreach (var index in new[] { 0, 1, 2 })
        {
            var original = Path.Combine(scratch, "refused-" + index + ".wav"); File.Copy(Path.Combine(fixtures, Path.GetFileName(original)), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var admissions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "flac", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == (index == 0 ? OperationState.Failed : OperationState.Unsupported) && access.Admissions == admissions &&
                !Directory.GetFiles(scratch, "refused-" + index + "*.flac").Any() && SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash),
                "WAV artwork direct: malformed/unsupported metadata cannot publish " + index);
        }
        await File.WriteAllTextAsync(Path.Combine(scratch, "wave-artwork-direct.json"), JsonSerializer.Serialize(reports));
    }
}
