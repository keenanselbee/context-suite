using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Transport;
using ContextSuite.Private;
using ContextSuite.Runtime;

if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--parent" ||
    !int.TryParse(args[3], out var parentId) || !args[1].StartsWith("ContextSuite-Worker-", StringComparison.Ordinal))
    return 2;

try
{
    using var parent = Process.GetProcessById(parentId);
    using var lifetime = new CancellationTokenSource();
    parent.EnableRaisingEvents = true;
    parent.Exited += (_, _) => lifetime.Cancel();
    if (parent.HasExited) return 3;
    await using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
    {
        startup.CancelAfter(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(startup.Token);
    }
    LocalPipe.VerifyPeer(pipe, false, parentId, parent.MainModule!.FileName!);
    var catalog = new ProductionCatalog();
    while (!lifetime.IsCancellationRequested)
    {
        var command = await JsonFrames.ReadAsync<WorkerCommand>(pipe, lifetime.Token);
        if (command.Version != 1 || command.RequestId == Guid.Empty) return 4;
        if (command.Command == "shutdown") return 0;
        if (command.Command != "capabilities") return 4;
        await JsonFrames.WriteAsync(pipe,
            new WorkerReply(1, command.RequestId, catalog.Capabilities.ToArray()), lifetime.Token);
    }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ArgumentException or
    InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
{
    // Do not print paths or protocol payloads into diagnostics.
    return 3;
}
