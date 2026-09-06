using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Activation;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Runtime;

if (args.Length is < 1 or > 3) throw new ArgumentException("Expected scratch directory, optional worker executable, and optional application executable.");
Directory.CreateDirectory(args[0]);
var fixture = Path.Combine(args[0], "contract-" + Guid.NewGuid().ToString("N") + ".txt");
await File.WriteAllTextAsync(fixture, "A deliberately non-media fixture; no transformation is allowed.");
var passed = 0;
try
{
    var id = Guid.NewGuid();
    string RequestText(string operation = "analyze", string action = "open-details", int count = 1) =>
        $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount={count}\npath={fixture}\n";
    var request = ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText()));
    foreach (var state in Enum.GetValues<AccessState>())
        Check(new AccessDecision(state, "Test state").CanStart == (state is AccessState.Trial or AccessState.Paid),
            "access admission: " + state);
    Check(request.RequestId == id && request.Paths.Single() == fixture, "valid activation");
    Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText().Replace("\n", "\r\n"))).Paths.Length == 1, "CRLF activation");
    foreach (var pair in new[] { ("convert", "choose-format"), ("optimize", "choose-preset") })
        Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(pair.Item1, pair.Item2))).Operation == pair.Item1, pair.Item1);
    Reject(() => ActivationParser.Parse([]), "empty activation");
    Reject(() => ActivationParser.Parse(new byte[ActivationParser.MaximumBytes + 1]), "oversized activation");
    Reject(() => ActivationParser.Parse([0xff]), "invalid UTF-8");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(count: 2))), "count mismatch");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(action: "choose-preset"))), "mismatched action");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(operation: "delete"))), "unknown operation");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText().Replace("ContextSuiteActivation/1", "ContextSuiteActivation/2"))), "unknown schema");
    Reject(() => (request with { RequestId = Guid.Empty }).Validate(), "empty ID");
    Reject(() => (request with { Paths = ["relative.png"] }).Validate(), "relative path");
    Reject(() => (request with { Paths = [args[0]] }).Validate(), "directory selection");
    Reject(() => (request with { Paths = [fixture + ".missing"] }).Validate(), "missing selection");
    Reject(() => (request with { Paths = ImmutableArray.CreateRange(new string[4097]) }).Validate(), "path limit");
    var many = request with { Paths = Enumerable.Repeat(fixture, 4096).ToImmutableArray() };
    many.Validate();
    Check(many.Paths.Length == 4096, "maximum batch");

    using (var stream = new MemoryStream())
    {
        await JsonFrames.WriteAsync(stream, new ActivationMessage(1, many), CancellationToken.None);
        stream.Position = 0;
        var copy = await JsonFrames.ReadAsync<ActivationMessage>(stream, CancellationToken.None);
        Check(copy.Request!.Paths.SequenceEqual(many.Paths), "framed full selection roundtrip");
    }
    foreach (var length in new[] { -1, 0, JsonFrames.MaximumBytes + 1 })
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        await RejectAsync(() => JsonFrames.ReadAsync<ActivationMessage>(new MemoryStream(bytes), CancellationToken.None), "invalid frame size");
    }
    await RejectAsync(() => JsonFrames.ReadAsync<ActivationMessage>(new MemoryStream([10, 0, 0, 0, 1]), CancellationToken.None), "truncated frame");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        await RejectAsync(() => JsonFrames.ReadAsync<ActivationMessage>(new MemoryStream([1, 0, 0, 0, 1]), cancelled.Token), "cancelled frame");
    }

    var pipeName = "ContextSuite-Contract-" + Guid.NewGuid().ToString("N");
    await using (var server = LocalPipe.CreateServer(pipeName))
    await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        LocalPipe.VerifyPeer(server, true, Environment.ProcessId, Environment.ProcessPath!);
        LocalPipe.VerifyPeer(client, false, Environment.ProcessId, Environment.ProcessPath!);
        passed++;
        Reject(() => LocalPipe.VerifyPeer(server, true, int.MaxValue, Environment.ProcessPath!), "unexpected peer");
        await RejectAsync(() => JsonFrames.ReadAsync<ActivationMessage>(server, new CancellationTokenSource(50).Token), "blocked pipe cancellation");
    }

    // Do not interfere with a running production application in the same session.
    await using (var router = ActivationRouter.TryCreate() ?? throw new IOException("Close Context Suite before running router contracts."))
    {
        var count = 0;
        router.Start(incoming =>
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(new ActivationReply(1, incoming?.RequestId ?? Guid.Empty, true, "Accepted"));
        });
        Check(ActivationRouter.TryCreate() is null, "single owner");
        await ActivationRouter.ForwardAsync(request, CancellationToken.None);
        await ActivationRouter.ForwardAsync(null, CancellationToken.None);
        Check(count == 2, "repeated activation forwarding");
        // A malformed client must not take down the listening owner.
        await using (var malformed = new NamedPipeClientStream(".", LocalPipe.SessionName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await malformed.ConnectAsync(timeout.Token);
            await JsonFrames.WriteAsync(malformed, new ActivationMessage(999, request), timeout.Token);
            await RejectAsync(() => JsonFrames.ReadAsync<ActivationReply>(malformed, timeout.Token), "unknown IPC version");
        }
        await ActivationRouter.ForwardAsync(request, CancellationToken.None);
        Check(count == 3, "activation after malformed client");
        await using (var slow = new NamedPipeClientStream(".", LocalPipe.SessionName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await slow.ConnectAsync(timeout.Token);
            await JsonFrames.WriteAsync(slow, new ActivationMessage(1, request), timeout.Token);
            await Task.Delay(100, timeout.Token);
            var reply = await JsonFrames.ReadAsync<ActivationReply>(slow, timeout.Token);
            Check(reply.Accepted && reply.RequestId == request.RequestId, "slow client retains unread acknowledgement");
            await JsonFrames.WriteAsync(slow, new ActivationReceipt(1, request.RequestId), timeout.Token);
        }
        router.StopAccepting();
        await RejectAsync(() => ActivationRouter.ForwardAsync(request, CancellationToken.None), "closing owner rejects activation");
    }

    await OnUiThreadAsync(async () =>
    {
        await using var vm = new MainViewModel(new WorkerClient(Path.Combine(args[0], "missing-worker.exe")));
        Check(vm.Admit(request).Accepted && vm.Admit(request).Accepted && vm.Rows.Count == 1, "duplicate request ID");
        vm.Admit(request with { RequestId = Guid.NewGuid(), Paths = [fixture, fixture, fixture] });
        Check(vm.Rows.Skip(1).Select(row => row.Batch).Distinct().Count() == 1, "shared batch identity");
        vm.CancelCommand.Execute(null);
        Check(vm.Rows.All(row => row.Status == "Cancelled"), "cancel queued batches");
        for (var index = 0; index < 3; index++)
            Check(vm.Admit(many with { RequestId = Guid.NewGuid() }).Accepted, "bounded queue admission");
        Check(!vm.Admit(many with { RequestId = Guid.NewGuid() }).Accepted, "queue overflow rejected");
        vm.CancelCommand.Execute(null);
    });

    if (args.Length >= 2)
    {
        await using var worker = new WorkerClient(args[1]);
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).Length == 0, "real private catalog has no fake capabilities");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).Length == 0, "worker reuse");
        // Cancellation kills the owned worker; a subsequent request must start a new one.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await RejectAsync(() => worker.GetCapabilitiesAsync(cancelled.Token), "worker cancellation");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).Length == 0, "worker restart after cancellation");
        using var process = Process.GetProcessById(worker.ProcessId!.Value);
        process.Kill(true);
        await process.WaitForExitAsync();
        await RejectAsync(() => worker.GetCapabilitiesAsync(CancellationToken.None), "worker crash reported");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).Length == 0, "worker restart after crash");
    }
    if (args.Length == 3)
    {
        await ApplicationSmoke.RunAsync(args[2], fixture);
        Check(true, "actual WPF startup, repeated activation, request consumption, and parent-exit cleanup");
    }
    Console.WriteLine($"Passed {passed} foundation contracts.");
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
finally { File.Delete(fixture); }
return 0;

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException)
    { Check(true, name); return; }
    throw new InvalidOperationException("FAILED to reject: " + name);
}
async Task RejectAsync(Func<Task> action, string name)
{
    try { await action(); }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or OperationCanceledException)
    { Check(true, name); return; }
    throw new InvalidOperationException("FAILED to reject: " + name);
}

static Task OnUiThreadAsync(Func<Task> action)
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.InvokeAsync(async () =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
            finally { dispatcher.InvokeShutdown(); }
        });
        System.Windows.Threading.Dispatcher.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}
