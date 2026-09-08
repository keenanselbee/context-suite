using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;
using ContextSuite.Private;
using ContextSuite.Private.Images;
using ContextSuite.Runtime;

if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--parent" || args[4] != "--scratch" || !Path.IsPathFullyQualified(args[5]) ||
    !int.TryParse(args[3], out var parentId) || !args[1].StartsWith("ContextSuite-Worker-", StringComparison.Ordinal))
    return 2;

try
{
    using var parent = Process.GetProcessById(parentId);
    using var lifetime = new CancellationTokenSource();
    parent.EnableRaisingEvents = true;
    // Native decoder calls are not guaranteed to poll cancellation. Do not leave a native worker
    // running after the owning app is gone; uncommitted output remains journaled by the publisher.
    parent.Exited += (_, _) => Environment.Exit(3);
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
    ImageAdapter? adapter = null;
    while (!lifetime.IsCancellationRequested)
    {
        var command = await JsonFrames.ReadAsync<WorkerCommand>(pipe, lifetime.Token);
        command.Validate();
        if (command.Command == "shutdown") return 0;
        if (command.Command == "engine-info")
        {
            await JsonFrames.WriteAsync(pipe,
                new WorkerReply(1, command.RequestId, [], ImageEngineRuntime.GetIdentity()), lifetime.Token);
            continue;
        }
        WorkerReply reply;
        try
        {
            if (command.Command == "capabilities") reply = new(1, command.RequestId, catalog.Capabilities.ToArray());
            else
            {
                adapter ??= new ImageAdapter(args[5]);
                reply = command.Command switch
                {
                    "image-probe" => new(1, command.RequestId, [], Source: adapter.Probe(command.Probe!, lifetime.Token)),
                    "png-probe" => new(1, command.RequestId, [], Source: adapter.ProbeOptimization(command.Probe!, lifetime.Token)),
                    "png-optimize" => new(1, command.RequestId, [], ImageResult: await adapter.OptimizeAsync(command.Optimization!, lifetime.Token)),
                    "image-preview" => new(1, command.RequestId, [], Preview: adapter.Preview(command.Preview!, lifetime.Token)),
                    "image-convert" => new(1, command.RequestId, [], ImageResult: adapter.Convert(command.Work!, lifetime.Token)),
                    _ => throw new InvalidDataException("Unknown image operation.")
                };
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or
            UnauthorizedAccessException or ImageMagick.MagickException or System.Xml.XmlException or
            System.Runtime.InteropServices.COMException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or OutOfMemoryException)
        {
            // A damaged/unsupported item is not a worker crash. Do not return engine strings containing file paths.
            var failure = error switch
            {
                ImageFailureException known => known.Failure,
                InvalidDataException or ArgumentException or System.Xml.XmlException => ImageFailure.InvalidInput,
                UnauthorizedAccessException or IOException => ImageFailure.FileAccess,
                ImageMagick.MagickResourceLimitErrorException => ImageFailure.ResourceLimit,
                OutOfMemoryException => ImageFailure.ResourceLimit,
                ImageMagick.MagickCorruptImageErrorException => ImageFailure.InvalidInput,
                _ => ImageFailure.EngineFailure
            };
            reply = new(1, command.RequestId, [], Failure: failure);
        }
        await JsonFrames.WriteAsync(pipe, reply, lifetime.Token);
    }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ArgumentException or
    InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
{
    // Do not print paths or protocol payloads into diagnostics.
    return 3;
}
