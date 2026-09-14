using System.Diagnostics;
using System.Text.Json;

internal static partial class OfficeEngineLifetimeContracts
{
    internal static async Task FileReleaseContractsAsync(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh file-release evidence directory.");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "disposable.txt"); await File.WriteAllTextAsync(path, "authored lock fixture");
        FileRelease released;
        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var waiting = WaitForFileRelease(path); await Task.Delay(60);
            if (waiting.IsCompleted) throw new Exception("Release check ignored the held file.");
            held.Dispose(); released = await waiting;
            if (released.SharingViolations == 0) throw new Exception("Release check lacked a sharing violation.");
        }
        var timer = Stopwatch.StartNew();
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { await WaitForFileRelease(path); throw new Exception("Persistent lock was accepted."); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { }
        }
        var deadlineMilliseconds = timer.ElapsedMilliseconds;
        if (deadlineMilliseconds < 5000) throw new Exception("Persistent lock failed before its release deadline.");
        timer.Restart();
        try { await WaitForFileRelease(Path.Combine(root, "missing.txt")); throw new Exception("Missing file was accepted."); }
        catch (FileNotFoundException) { }
        await File.WriteAllTextAsync(Path.Combine(root, "file-release-contracts.json"), JsonSerializer.Serialize(new
        { Passed = 3, DelayedRelease = released, PersistentLockMilliseconds = deadlineMilliseconds, MissingFileMilliseconds = timer.ElapsedMilliseconds }));
        Console.WriteLine("PASS: three file-release contracts (delayed release, persistent lock and missing file).");
    }
}
