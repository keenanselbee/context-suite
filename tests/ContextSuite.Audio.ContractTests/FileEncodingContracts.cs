using System.Diagnostics;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static class FileEncodingContracts
{
    public static async Task RunAsync(string binary, string evidence, List<string> checks, List<object> reports)
    {
        var path = Path.Combine(evidence, "authored-five-minute-pcm24.wav");
        await WriteLongWaveAsync(path);
        var scratch = Path.Combine(evidence, "file-encoding-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var originalHash = Convert.ToHexString(await SHA256.HashDataAsync(source));
        source.Position = 37;
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        long flacBytes, waveBytes;
        using (var flac = await adapter.ConvertFileAsync(source, AudioFormat.Flac, AudioConversionConsent.None, default))
        {
            if (flac.Result.SourceBytes <= AudioEncodingAdapter.MaximumEncodedBytes || !flac.Result.ExactSamples || flac.Result.DecodedFrames != 14_400_000 ||
                flac.Result.SourceSha256 != originalHash || source.Position != 37)
                throw new Exception("Long source snapshot, source position or exact samples were not preserved.");
            checks.Add("file audio: five-minute PCM24 input exceeds old input/decoded-array limits and retains exact samples");
            flacBytes = flac.Result.OutputBytes;
            try { using var writer = new FileStream(flac.Content.Name, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Candidate mutation allowed."); }
            catch (IOException) { checks.Add("file audio: returned artifact holds a read lease"); }
            using var wave = await adapter.ConvertFileAsync(flac.Content, AudioFormat.Wave, AudioConversionConsent.None, default);
            if (wave.Content.Length <= AudioEncodingAdapter.MaximumEncodedBytes || !wave.Result.ExactSamples || wave.Result.DecodedFrames != 14_400_000 ||
                wave.Result.SourceSha256 != flac.Result.OutputSha256)
                throw new Exception("Long WAVE output did not retain complete samples and source identity.");
            checks.Add("file audio: five-minute WAVE output exceeds old byte-array limit");
            waveBytes = wave.Result.OutputBytes;
        }
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        if (allocated >= 64L * 1024 * 1024) throw new Exception("Streaming operations allocated a recording-sized managed buffer.");
        checks.Add("file audio: both long operations allocate less than 64 MiB managed memory in total");
        if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new Exception("Disposed artifacts left owned files.");
        checks.Add("file audio: artifact disposal removes owned candidates and directories");
        reports.Add(new { operation = "five-minute-file-roundtrip", sourceBytes = source.Length, flacBytes, waveBytes,
            frames = 14_400_000, canonicalDecodedBytes = 230_400_000, managedAllocatedBytes = allocated,
            elapsedMs = watch.ElapsedMilliseconds, allocationScope = "Managed allocation across these two operations, not process peak working set" });

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        long observedBytes = 0;
        using var watcher = new FileSystemWatcher(scratch, "reference.f64le") { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.Size | NotifyFilters.FileName };
        void OnReference(object sender, FileSystemEventArgs args)
        {
            try
            {
                var length = new FileInfo(args.FullPath).Length;
                if (length >= 65536 && Interlocked.CompareExchange(ref observedBytes, length, 0) == 0) cancel.Cancel();
            }
            catch (IOException) { }
        }
        watcher.Changed += OnReference;
        watcher.Created += OnReference;
        watcher.EnableRaisingEvents = true;
        try { using var result = await adapter.ConvertFileAsync(source, AudioFormat.Flac, AudioConversionConsent.None, cancel.Token); throw new Exception("Running file operation ignored cancellation."); }
        catch (OperationCanceledException)
        {
            if (observedBytes < 65536) throw new Exception("No native decoded output was observed before cancellation.");
            checks.Add("file audio: cancellation after native decoded output is observed removes scratch");
        }
        watcher.EnableRaisingEvents = false;
        if (Directory.EnumerateFileSystemEntries(scratch).Any() || source.Position != 37) throw new Exception("Cancelled file work left scratch or changed stream position.");
        source.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(source)) != originalHash) throw new Exception("Long source changed.");
        checks.Add("file audio: original bytes survive success and running-operation cancellation");
        reports.Add(new { operation = "file-cancellation", decodedBytesObservedBeforeCancellation = observedBytes });
    }

    internal static async Task DelayedCleanupAsync(string evidence, List<string> checks)
    {
        var path = Path.Combine(evidence, "owned-delayed-cleanup.tmp");
        FileStream? held = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var cleanup = AudioEncodingAdapter.DeleteOwnedFileAsync(path);
            await Task.Delay(250);
            if (cleanup.IsCompleted || !File.Exists(path)) throw new Exception("Cleanup did not wait for its owned file handle.");
            held.Dispose(); held = null;
            await cleanup.WaitAsync(TimeSpan.FromSeconds(3));
            if (File.Exists(path)) throw new Exception("Released owned file was not cleaned.");
            checks.Add("file audio: sharing-violation cleanup waits for delayed release and removes only the owned file");
        }
        finally { held?.Dispose(); }
        using (var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            try { await AudioEncodingAdapter.DeleteOwnedFileAsync(path); throw new Exception("Persistent lock was silently ignored."); }
            catch (IOException error) when ((error.HResult & 0xffff) == 32)
            { checks.Add("file audio: persistent sharing violation is reported after bounded retries"); }
        }
        await AudioEncodingAdapter.DeleteOwnedFileAsync(path);
    }

    private static async Task WriteLongWaveAsync(string path)
    {
        const int rate = 48000, seconds = 300, frameBytes = 6;
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write("RIFF"u8); writer.Write(36 + rate * seconds * frameBytes); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(rate); writer.Write(rate * frameBytes);
            writer.Write((ushort)frameBytes); writer.Write((ushort)24); writer.Write("data"u8); writer.Write(rate * seconds * frameBytes);
        }
        var second = new byte[rate * frameBytes];
        for (var frame = 0; frame < rate; frame++)
        for (var channel = 0; channel < 2; channel++)
        {
            var sample = (int)Math.Round(Math.Sin(2 * Math.PI * (channel == 0 ? 997 : 631) * frame / rate) * 6000000);
            var offset = frame * frameBytes + channel * 3;
            second[offset] = (byte)sample; second[offset + 1] = (byte)(sample >> 8); second[offset + 2] = (byte)(sample >> 16);
        }
        for (var index = 0; index < seconds; index++) await stream.WriteAsync(second);
    }
}
