using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task WaveTextAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh WAV text publication evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.ConversionRequested += (_, _) => throw new InvalidOperationException("Audio cannot open an image planner.");
        vm.AudioConversionRequested += (_, _) => throw new InvalidOperationException("WAV retains these sources without a quality prompt.");
        var cases = new[] { ("Flac", ".flac"), ("Mp3", ".mp3"), ("M4a", ".m4a"), ("Vorbis", ".ogg"), ("Opus", ".opus") }
            .SelectMany(item => new[] { (item.Item1 + "-text", item.Item2), (item.Item1 + "-picture", item.Item2) })
            .Concat(new[] { ("rich-fields", ".flac"), ("maximum-field", ".flac"), ("literal-structured-fields", ".flac") });
        var reports = new List<object>();
        foreach (var (name, extension) in cases)
        {
            var original = Path.Combine(scratch, name + extension); File.Copy(Path.Combine(fixtures, name + ".source"), original);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(original)); var modified = File.GetLastWriteTimeUtc(original);
            ImmutableDictionary<string, string> expected; ImmutableArray<FlacMetadataBlock> pictures;
            using (var source = File.OpenRead(original))
            {
                if (extension == ".flac")
                {
                    var header = FlacMetadata.Parse(await File.ReadAllBytesAsync(original)); expected = FlacConversionMetadata.Read(header, true);
                    pictures = header.Blocks.Where(block => block.Type == 6).ToImmutableArray();
                }
                else if (extension == ".mp3")
                { var inventory = await Mp3Metadata.ReadAsync(source, default, true); expected = inventory.Tags; pictures = inventory.Pictures; }
                else if (extension == ".m4a")
                { var inventory = await M4aMetadata.ReadAsync(source, default, true); expected = inventory.Tags; pictures = inventory.Pictures; }
                else
                {
                    var inventory = await OggMetadata.ReadAsync(source, default, true); pictures = OggPictureComments.Read(inventory.Descriptions);
                    expected = inventory.ConversionTags(pictures);
                }
            }
            pictures = Mp3PictureFrames.ForOutput(pictures);
            var admissions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            var row = vm.Rows[^1]; var output = row.OutputPath;
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated && access.Admissions == admissions + 1,
                "WAV text direct: validated copy " + name + " " + row.Status);
            using (var saved = File.OpenRead(output))
            {
                var inventory = await WaveMetadata.ReadAsync(saved, default, true);
                var tags = inventory.Tags.Where(tag => tag.Name != "encoder").ToDictionary(tag => tag.Name, tag => tag.Value);
                check(inventory.UnsupportedChunks.IsEmpty && tags.Count == expected.Count && expected.All(tag => tags.GetValueOrDefault(tag.Key) == tag.Value) &&
                    inventory.Pictures.Length == pictures.Length && !inventory.Pictures.Where((picture, index) => !picture.Data.AsSpan().SequenceEqual(pictures[index].Data.AsSpan())).Any(),
                    "WAV text direct: exact published fields and pictures " + name);
            }
            var outputHash = SHA256.HashData(await File.ReadAllBytesAsync(output));
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [original])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != output &&
                SHA256.HashData(await File.ReadAllBytesAsync(output)).AsSpan().SequenceEqual(outputHash), "WAV text direct: collision keeps previous output " + name);
            var afterConversions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "wav", [output])); await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == afterConversions,
                "WAV text direct: same-format no-op needs no new admission " + name);
            check(SHA256.HashData(await File.ReadAllBytesAsync(original)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(original) == modified,
                "WAV text direct: original bytes and time unchanged " + name);
            reports.Add(new { Source = original, Output = output, SourceSha256 = Convert.ToHexString(hash), OutputSha256 = Convert.ToHexString(outputHash), Tags = expected, Pictures = pictures.Length });
        }
        var refused = Path.Combine(scratch, "refused-control.flac"); File.Copy(Path.Combine(fixtures, "refused-control.flac"), refused);
        var refusedHash = SHA256.HashData(await File.ReadAllBytesAsync(refused)); var beforeRefusal = access.Admissions;
        vm.Admit(new(Guid.NewGuid(), "convert", "wav", [refused])); await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Unsupported && access.Admissions == beforeRefusal &&
            !Directory.GetFiles(scratch, "refused-control*.wav").Any() && SHA256.HashData(await File.ReadAllBytesAsync(refused)).AsSpan().SequenceEqual(refusedHash),
            "WAV text direct: unrepresentable control characters cannot admit or publish");
        await File.WriteAllTextAsync(Path.Combine(scratch, "wave-text-direct.json"), JsonSerializer.Serialize(reports));
    }
}
