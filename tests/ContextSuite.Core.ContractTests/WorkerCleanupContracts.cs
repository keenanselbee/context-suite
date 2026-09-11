using System.Diagnostics;
using ContextSuite.Application.Infrastructure;

internal static class WorkerCleanupContracts
{
    public static async Task RunAsync(string scratch, string executable, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "delayed-cleanup-" + Guid.NewGuid().ToString("N"));
        var client = new WorkerClient(executable, root);
        FileStream? held = null;
        try
        {
            Directory.CreateDirectory(root);
            var fixture = Path.Combine(root, "generated.png");
            ContextSuite.Core.ContractTests.ImageWorkerContracts.WritePng(fixture);
            await client.ProbeAsync(new(Guid.NewGuid(), fixture), default);
            var directory = Directory.GetDirectories(root).Single();
            held = new FileStream(Path.Combine(directory, "generated-held-file.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var process = Process.GetProcessById(client.ProcessId!.Value);
            var stopping = client.DisposeAsync().AsTask();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(250);
            check(!stopping.IsCompleted && Directory.Exists(directory), "worker cleanup: bounded retry waits for a temporarily held scratch file");
            held.Dispose(); held = null;
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            check(!Directory.Exists(directory), "worker cleanup: released scratch file is removed before shutdown completes");
        }
        finally { held?.Dispose(); await client.DisposeAsync(); }
    }
}
