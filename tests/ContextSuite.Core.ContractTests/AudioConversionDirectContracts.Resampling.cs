using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task ResamplingAsync(string scratch, string executable, string fixtures,
        string taggedFixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh resampling publication evidence.");
        Directory.CreateDirectory(scratch);
        var sources = new List<(string Path, int Rate, int Channels, int Frames, bool Tagged, string Hash, DateTime Modified)>();
        foreach (var item in new[] { (8000, 1, 1), (11025, 2, 16), (44100, 2, 47), (96000, 1, 47),
            (192000, 2, 1), (192000, 2, 257), (96000, 2, 256) })
        {
            var name = $"{item.Item1}-{item.Item2}-{item.Item3}.wav";
            var path = Path.Combine(scratch, name);
            File.Copy(Path.Combine(fixtures, name), path);
            sources.Add((path, item.Item1, item.Item2, item.Item3, false,
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), File.GetLastWriteTimeUtc(path)));
        }
        foreach (var (format, extension) in new[] { ("Flac", ".flac"), ("Mp3", ".mp3"), ("M4a", ".m4a"), ("Vorbis", ".ogg") })
        {
            var path = Path.Combine(scratch, "tagged-" + format + extension);
            File.Copy(Path.Combine(taggedFixtures, "47-2-" + format + extension), path);
            sources.Add((path, 44100, 2, 47, true,
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))), File.GetLastWriteTimeUtc(path)));
        }
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        var accept = false;
        var prompts = 0;
        vm.AudioConversionRequested += async (decision, _) =>
        {
            prompts++;
            check(decision.Plan.Target == AudioFormat.Opus && decision.Plan.RequiredConsent.HasFlag(AudioConversionConsent.Resampling),
                "Resampling direct: fixed Opus action explicitly requests resampling consent");
            if (decision.Plan.Items.Length > 1)
                check(decision.Plan.RequiredConsent.HasFlag(AudioConversionConsent.LossyTranscoding),
                    "Resampling direct: mixed lossy sources also require transcoding consent");
            if (!accept) return null;
            await decision.RefreshAccessAsync();
            ConfirmedAudioConversion? result = null;
            decision.Confirmed += confirmed => result = confirmed;
            decision.ConfirmCommand.Execute(null);
            return result;
        };
        vm.Admit(new(Guid.NewGuid(), "convert", "opus", [sources[0].Path]));
        await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Cancelled && access.Admissions == 0 &&
            !Directory.EnumerateFiles(scratch, "*.opus").Any(), "Resampling direct: declined consent publishes nothing and consumes no admission");
        accept = true;
        var workerId = worker.ProcessId;
        vm.Admit(new(Guid.NewGuid(), "convert", "opus", [.. sources.Select(source => source.Path)]));
        await vm.WaitForIdleAsync();
        var rows = vm.Rows.TakeLast(sources.Count).ToArray();
        check(prompts == 2 && access.Admissions == 1 && workerId is > 0 && worker.ProcessId == workerId,
            "Resampling direct: one confirmed batch uses one admission and the same worker");
        var reports = new List<object>();
        foreach (var (source, row) in sources.Zip(rows))
        {
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                row.OutputPath != source.Path && File.Exists(row.OutputPath), "Resampling direct: validated named copy for " + Path.GetFileName(source.Path));
            using var output = File.OpenRead(row.OutputPath);
            var inventory = await OggMetadata.ReadAsync(output, default, true);
            check(inventory.SampleRate == 48000 && inventory.Channels == source.Channels &&
                (!source.Tagged || inventory.ConversionTags([])["title"] == "Finite resampling"),
                "Resampling direct: published rate, channels and descriptive metadata");
            check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source.Path))) == source.Hash &&
                File.GetLastWriteTimeUtc(source.Path) == source.Modified, "Resampling direct: original bytes and write time preserved");
            reports.Add(new { source = source.Path, output = row.OutputPath, source.Rate, source.Channels, source.Frames,
                minimumFrames = Math.Max(1, (long)source.Frames * 48000 / source.Rate),
                maximumFrames = ((long)source.Frames * 48000 + source.Rate - 1) / source.Rate, source.Hash });
        }
        var firstOutput = rows[0].OutputPath;
        var firstHash = SHA256.HashData(await File.ReadAllBytesAsync(firstOutput));
        vm.Admit(new(Guid.NewGuid(), "convert", "opus", [sources[0].Path]));
        await vm.WaitForIdleAsync();
        check(vm.Rows[^1].Result.State == OperationState.Succeeded && vm.Rows[^1].OutputPath != firstOutput &&
            SHA256.HashData(await File.ReadAllBytesAsync(firstOutput)).AsSpan().SequenceEqual(firstHash),
            "Resampling direct: repeat conversion preserves the first copy and chooses a new name");
        var repeated = sources[0];
        reports.Add(new { source = repeated.Path, output = vm.Rows[^1].OutputPath, repeated.Rate, repeated.Channels,
            repeated.Frames, minimumFrames = Math.Max(1, (long)repeated.Frames * 48000 / repeated.Rate),
            maximumFrames = ((long)repeated.Frames * 48000 + repeated.Rate - 1) / repeated.Rate,
            repeated.Hash, repeat = true });
        await File.WriteAllTextAsync(Path.Combine(scratch, "resampling-direct.json"),
            JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}
