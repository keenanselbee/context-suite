using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Operations;

internal static partial class AudioConversionDirectContracts
{
    internal static async Task ShortAudioAsync(string scratch, string executable, string fixtures, Action<bool, string> check)
    {
        if (Directory.Exists(scratch)) throw new IOException("Use fresh short-audio publication evidence.");
        Directory.CreateDirectory(scratch);
        var access = new Access { Allowed = true };
        var publisher = new OutputPublisher(Path.Combine(scratch, "records"), new RefusingRecycler());
        var worker = new WorkerClient(executable, Path.Combine(scratch, "worker"));
        await using var vm = new MainViewModel(worker, new() { PlayCompletionSound = false }, publisher, access);
        vm.AudioConversionRequested += (_, _) => throw new InvalidOperationException("Short lossless-source conversion needs no quality prompt.");
        var reports = new List<object>();
        foreach (var frames in new[] { 1, 2, 15, 16, 47, 127 })
        {
            var source = Path.Combine(scratch, frames + "-original.wav");
            File.Copy(Path.Combine(fixtures, frames + "-source.wav"), source);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var time = File.GetLastWriteTimeUtc(source);
            var admissions = access.Admissions;
            vm.Admit(new(Guid.NewGuid(), "convert", "flac", [source]));
            await vm.WaitForIdleAsync();
            var row = vm.Rows[^1];
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                access.Admissions == admissions + 1, "Short FLAC direct: validated copy for " + frames + " frames: " + row.Status);
            var output = row.OutputPath;
            var header = FlacMetadata.Parse(await File.ReadAllBytesAsync(output));
            check(header.SampleFrames == frames && header.SampleRate == 48000 && header.Channels == 2 &&
                SHA256.HashData(await File.ReadAllBytesAsync(source)).AsSpan().SequenceEqual(hash) && File.GetLastWriteTimeUtc(source) == time,
                "Short FLAC direct: exact extent and unchanged original for " + frames);
            vm.Admit(new(Guid.NewGuid(), "convert", "flac", [output]));
            await vm.WaitForIdleAsync();
            check(vm.Rows[^1].Result.State == OperationState.Unchanged && access.Admissions == admissions + 1,
                "Short FLAC direct: no-op avoids another admission for " + frames);
            reports.Add(new { frames, source, output, SourceSha256 = Convert.ToHexString(hash) });
        }
        var workerId = worker.ProcessId;
        foreach (var frames in new[] { 1, 2, 15, 16, 46, 47 })
        {
            var source = Path.Combine(scratch, frames + "-mp3-source.wav");
            File.Copy(Path.Combine(fixtures, frames + "-source.wav"), source);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(source));
            var time = File.GetLastWriteTimeUtc(source);
            vm.Admit(new(Guid.NewGuid(), "convert", "mp3", [source]));
            await vm.WaitForIdleAsync();
            var row = vm.Rows[^1];
            check(row.Result.State == OperationState.Succeeded && row.Result.Publication?.Outcome == PublicationOutcome.CopyCreated &&
                File.Exists(row.OutputPath) && worker.ProcessId == workerId,
                "Short MP3 direct: validated copy on the same worker for " + frames + " frames: " + row.Status);
            check(SHA256.HashData(await File.ReadAllBytesAsync(source)).AsSpan().SequenceEqual(hash) &&
                File.GetLastWriteTimeUtc(source) == time, "Short MP3 direct: unchanged original for " + frames);
            reports.Add(new { frames, target = "mp3", source, output = row.OutputPath, SourceSha256 = Convert.ToHexString(hash) });
        }
        await File.WriteAllTextAsync(Path.Combine(scratch, "short-audio-direct.json"), JsonSerializer.Serialize(reports,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
