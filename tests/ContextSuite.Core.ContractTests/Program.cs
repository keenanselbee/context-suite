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
using ContextSuite.Core.ContractTests;
using ContextSuite.Core.Settings;

if (args.Length == 2 && args[0] == "--benchmark-analyze") return await AnalyzeBenchmark.RunAsync(args[1]);
if (args.Length == 2 && args[0] == "--analysis-mapping")
{
    var checks = 0;
    await AnalysisMappingContracts.RunAsync(args[1], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated analysis mapping checks.");
    return 0;
}
if (args.Length == 2 && args[0] == "--analysis-io")
{
    var checks = 0;
    await AnalysisIoContracts.RunAsync(args[1], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated analysis I/O checks.");
    return 0;
}
if (args.Length == 5 && args[0] == "--audio-publication-crash-child")
    return await AudioPublicationCrashContracts.RunChildAsync(args[1], args[2], args[3], args[4]);
if (args.Length == 4 && args[0] == "--audio-publication-crashes")
{
    var checks = 0;
    await AudioPublicationCrashContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated audio publication crash checks.");
    return 0;
}
if (args.Length == 3 && args[0] is "--audio-interruptions" or "--flac-interruptions")
{
    var checks = 0;
    await AudioInterruptionContracts.RunAsync(args[1], args[2], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }, args[0] == "--flac-interruptions");
    Console.WriteLine($"Passed {checks} isolated {(args[0] == "--flac-interruptions" ? "FLAC optimization" : "audio")} interruption checks.");
    return 0;
}
if (args.Length == 2 && args[0] == "--recycle") return await WindowsRecycleContracts.RunAsync(args[1]);
if (args.Length == 5 && args[0] == "--image-pdf-publication-crash") return await ImagePdfPublicationCrashContracts.RunChildAsync(args[1], args[2], args[3], args[4]);
if (args.Length == 3 && args[0] == "--image-pdf-precision-worker")
{
    await ImagePdfPrecisionWorkerContracts.RunAsync(args[1], args[2]);
    return 0;
}
if (args.Length == 4 && args[0] == "--image-pdf-resource-worker")
{
    await ImagePdfResourceWorkerContracts.RunAsync(args[1], args[2], args[3]);
    return 0;
}
if (args.Length == 4 && args[0] == "--image-pdf-worker")
{
    var checks = 0;
    await ImagePdfWorkerContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated combined PDF workflow checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--image-pdf-direct")
{
    var checks = 0;
    await ImagePdfDirectContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct image PDF checks.");
    return 0;
}
if (args.Length == 5 && args[0] == "--pdf-publication-crash") return await PdfPublicationCrashContracts.RunChildAsync(args[1], args[2], args[3], args[4]);
if (args.Length == 3 && args[0] == "--pdf-page-geometry")
{
    var checks = 0;
    await PdfPageGeometryContracts.RunAsync(args[1], args[2], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF page geometry checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--wave-output-artwork-direct")
{
    var waveOutputChecks = new List<string>();
    await AudioConversionDirectContracts.WaveOutputPicturesAsync(args[1], args[2], args[3], (passed, message) =>
    { if (!passed) throw new InvalidDataException(message); waveOutputChecks.Add(message); });
    Console.WriteLine($"Passed {waveOutputChecks.Count} WAV output artwork direct checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--wave-artwork-direct")
{
    var waveChecks = new List<string>();
    await AudioConversionDirectContracts.WavePicturesAsync(args[1], args[2], args[3], (passed, message) =>
    { if (!passed) throw new InvalidDataException(message); waveChecks.Add(message); });
    Console.WriteLine($"Passed {waveChecks.Count} WAV artwork direct checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--m4a-output-artwork-direct")
{
    var m4aOutputChecks = new List<string>();
    await AudioConversionDirectContracts.M4aOutputPicturesAsync(args[1], args[2], args[3], (passed, message) =>
    { if (!passed) throw new InvalidDataException(message); m4aOutputChecks.Add(message); });
    Console.WriteLine($"Passed {m4aOutputChecks.Count} M4A output artwork direct checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--mp3-output-artwork-direct")
{
    var mp3OutputChecks = new List<string>();
    await AudioConversionDirectContracts.Mp3OutputPicturesAsync(args[1], args[2], args[3], (passed, message) =>
    { if (!passed) throw new InvalidDataException(message); mp3OutputChecks.Add(message); });
    Console.WriteLine($"Passed {mp3OutputChecks.Count} MP3 output artwork direct checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--m4a-artwork-direct")
{
    var checks = 0;
    await AudioConversionDirectContracts.M4aPicturesAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct M4A artwork checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--ogg-artwork-direct")
{
    var checks = 0;
    await AudioConversionDirectContracts.OggPicturesAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct Ogg artwork checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--mp3-artwork-direct")
{
    var checks = 0;
    await AudioConversionDirectContracts.Mp3PicturesAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct MP3 artwork checks.");
    return 0;
}
if (args.Length is 4 or 5 && args[0] == "--audio-conversion-direct")
{
    var checks = 0;
    await AudioConversionDirectContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }, args.Length == 5 ? args[4] : null);
    Console.WriteLine($"Passed {checks} isolated direct audio conversion checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--audio-conversion-worker")
{
    var checks = 0;
    await AudioConversionWorkflowContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated audio conversion workflow checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--flac-worker")
{
    var checks = 0;
    await FlacWorkflowContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated FLAC workflow checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--audio-direct")
{
    var checks = 0;
    await AudioDirectContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct-audio checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-worker")
{
    var checks = 0;
    await PdfWorkerContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF-worker checks.");
    return 0;
}
if (args.Length == 5 && args[0] == "--pdf-direct")
{
    var checks = 0;
    await PdfDirectContracts.RunAsync(args[1], args[2], args[3], args[4], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct PDF checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-failures")
{
    var checks = 0;
    await PdfFailureContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF failure checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-optimization-worker")
{
    var checks = 0;
    await PdfOptimizationWorkflowContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF optimization workflow checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-page-worker")
{
    var checks = 0;
    await PdfPageWorkflowContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF page workflow checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-page-failures")
{
    var checks = 0;
    await PdfPageFailureContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated PDF page interruption checks.");
    return 0;
}
if (args.Length == 4 && args[0] == "--pdf-page-direct")
{
    var checks = 0;
    await PdfPageDirectContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); });
    Console.WriteLine($"Passed {checks} isolated direct PDF page checks.");
    return 0;
}
if (args.Length is 4 or 5 && args[0] == "--audio-worker")
{
    var checks = 0;
    await AudioWorkerContracts.RunAsync(args[1], args[2], args[3], (condition, message) =>
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }, args.Length == 5 ? args[4] : null);
    Console.WriteLine($"Passed {checks} isolated audio-worker checks.");
    return 0;
}
if (args.Length == 3 && args[0] == "--recycle-images") return await WindowsRecycleContracts.RunImagesAsync(args[1], args[2]);
if (args.Length == 3 && args[0] == "--crash-publication") return await PublicationCrashContracts.RunChildAsync(args[1], args[2]);
if (args.Length is < 1 or > 3) throw new ArgumentException("Expected scratch directory, optional worker executable, and optional application executable.");
Directory.CreateDirectory(args[0]);
var fixture = Path.Combine(args[0], "contract-" + Guid.NewGuid().ToString("N") + ".txt");
await File.WriteAllTextAsync(fixture, "A deliberately non-media fixture; no transformation is allowed.");
var passed = 0;
try
{
    PaidLicenseContracts.Run(Check);
    await LicenseStorageContracts.RunAsync(args[0], Check);
    await SettingsContracts.RunAsync(args[0], Check);
    await DdsContracts.RunAsync(args[0], Check);
    await AnalysisContracts.RunAsync(args[0], Check);
    await ImageHeaderContracts.RunAsync(args[0], Check);
    await DocumentAnalysisContracts.RunAsync(args[0], Check);
    await LegacyDocumentAnalysisContracts.RunAsync(args[0], Check);
    PdfProbeContracts.Run(Check);
    await PdfAnalysisContracts.RunAsync(args[0], Check);
    FlacMetadataContracts.Run(Check);
    FlacDescriptionContracts.Run(Check);
    FlacConversionMetadataContracts.Run(Check);
    await OggMetadataContracts.RunAsync(Check);
    await Mp3MetadataContracts.RunAsync(Check);
    await M4aMetadataContracts.RunAsync(Check);
    await AudioBatchContracts.RunAsync(args[0], Check);
    await AudioDecisionContracts.RunAsync(args[0], Check);
    PdfRewriteContracts.Run(Check);
    PdfRasterContracts.Run(Check);
    ImagePdfContracts.Run(args[0], Check);
    ImagePdfValidationContracts.Run(args[0], Check);
    await ImagePdfBatchContracts.RunAsync(args[0], Check);
    await ImagePdfOrderContracts.RunAsync(args[0], Check);
    await PdfBatchContracts.RunAsync(args[0], Check);
    FlacSeekContracts.Run(Check);
    await FlacBatchContracts.RunAsync(args[0], Check);
    AudioProbeContracts.Run(Check);
    AudioConversionContracts.Run(Check);
    await WaveMetadataContracts.RunAsync(Check);
    await AudioSampleContracts.RunAsync(Check);
    ImagePlanContracts.Run(args[0], Check);
    await PngOptimizationContracts.RunAsync(args[0], Check);
    await TrialContracts.RunAsync(args[0], Check);
    await PublicationContracts.RunAsync(args[0], Check);
    await PublicationCrashContracts.RunAsync(args[0], Check);
    var id = Guid.NewGuid();
    string RequestText(string operation = "analyze", string action = "open-details", int count = 1) =>
        $"ContextSuiteActivation/1\nrequestId={id:D}\noperation={operation}\naction={action}\npathCount={count}\npath={fixture}\n";
    var request = ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText()));
    foreach (var state in Enum.GetValues<AccessState>())
        Check(new AccessDecision(state, "Test state").CanStart == (state is AccessState.Trial or AccessState.Paid),
            "access admission: " + state);
    Check(request.RequestId == id && request.Paths.Single() == fixture, "valid activation");
    var settingsRequest = ActivationParser.Parse(Encoding.UTF8.GetBytes(
        $"ContextSuiteActivation/1\nrequestId={Guid.NewGuid():D}\noperation=convert\naction=settings\npathCount=0\n"));
    Check(settingsRequest.IsSettingsRequest && settingsRequest.Paths.IsEmpty, "pathless settings activation");
    Reject(() => (settingsRequest with { Paths = [fixture] }).Validate(), "settings rejects media paths");
    Reject(() => (settingsRequest with { Operation = "analyze" }).Validate(), "Analyze has no settings submenu action");
    Reject(() => (request with { Paths = [] }).Validate(), "empty media selection still rejected");
    Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText().Replace("\n", "\r\n"))).Paths.Length == 1, "CRLF activation");
    foreach (var pair in new[] { ("convert", "choose-format"), ("optimize", "choose-preset") })
        Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(pair.Item1, pair.Item2))).Operation == pair.Item1, pair.Item1);
    Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText("convert", "dds"))).IsQuickConversion,
        "DDS menu activation retains direct-command lifecycle");
    Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText("optimize", "choose-preset"))).IsQuickOptimization,
        "legacy optimization activation uses direct lossless default");
    Reject(() => ActivationParser.Parse([]), "empty activation");
    Reject(() => ActivationParser.Parse(new byte[ActivationParser.MaximumBytes + 1]), "oversized activation");
    Reject(() => ActivationParser.Parse([0xff]), "invalid UTF-8");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(count: 2))), "count mismatch");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(action: "choose-preset"))), "mismatched action");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText(operation: "delete"))), "unknown operation");
    Reject(() => ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText().Replace("ContextSuiteActivation/1", "ContextSuiteActivation/2"))), "unknown schema");
    Reject(() => (request with { RequestId = Guid.Empty }).Validate(), "empty ID");
    Reject(() => (request with { Paths = ["relative.png"] }).Validate(), "relative path");
    foreach (var unavailablePath in new[] { args[0], fixture + ".missing" })
    {
        (request with { Paths = [unavailablePath] }).Validate();
        Check(ActivationParser.Parse(Encoding.UTF8.GetBytes(RequestText().Replace(fixture, unavailablePath))).Paths.Single() == unavailablePath,
            "Analyze defers selected-path availability through parser and admission");
        foreach (var transform in new[] { ("convert", "png"), ("optimize", "auto") })
            Reject(() => (request with { Operation = transform.Item1, Action = transform.Item2, Paths = [unavailablePath] }).Validate(),
                transform.Item1 + " still rejects missing/directory selections");
    }
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

    await OnUiThreadAsync(async () =>
    {
        using var owner = new ActivationGate();
        Check(owner.TryEnter() && owner.TryEnter(), "handoff gate: same owner does not recursively acquire");
        await OnUiThreadAsync(async () =>
        {
            using var contender = new ActivationGate();
            Check(!contender.TryEnter(), "handoff gate: competing dispatcher cannot close during forwarding");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));
            await RejectAsync(() => contender.EnterAsync(cancel.Token), "handoff gate: waiting is cancellable without blocking dispatcher");
        });
    });
    await OnUiThreadAsync(async () =>
    {
        using var next = new ActivationGate();
        Check(next.TryEnter(), "handoff gate: release allows next owner");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await RejectAsync(() => next.EnterAsync(cancelled.Token), "handoff gate: pre-cancellation retained");
    });
    using (var abandoned = new Mutex(false, LocalPipe.SessionName + "-handoff",
        new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true }))
    {
        var acquired = false;
        var thread = new Thread(() => acquired = abandoned.WaitOne(1000));
        thread.Start();
        Check(thread.Join(2000) && acquired, "handoff gate: terminated owner fixture acquired mutex");
        await OnUiThreadAsync(() =>
        {
            using var recovered = new ActivationGate();
            Check(recovered.TryEnter(), "handoff gate: abandoned owner recovers without stale lock cleanup");
            return Task.CompletedTask;
        });
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
        string? settingsSection = null;
        vm.SettingsRequested += section => settingsSection = section;
        Check(vm.Admit(settingsRequest).Accepted && settingsSection == "convert" && vm.Rows.Count == 0 && !vm.IsBusy,
            "settings activation opens section without worker or media admission");
        Check(vm.Admit(request).Accepted && vm.Admit(request).Accepted && vm.Rows.Count == 1, "duplicate request ID");
        vm.Admit(request with { RequestId = Guid.NewGuid(), Operation = "convert", Action = "choose-format" });
        vm.Settings = new SuiteSettings { Convert = new(true) };
        Check(!vm.Rows[^1].Settings.Preferences.ReplaceOriginals, "queued file retains its settings snapshot");
        vm.Admit(request with { RequestId = Guid.NewGuid(), Operation = "convert", Action = "choose-format" });
        Check(vm.Rows[^1].Settings.Preferences.ReplaceOriginals, "future batch captures changed preferences");
        vm.Admit(request with { RequestId = Guid.NewGuid(), Paths = [fixture, fixture, fixture] });
        Check(vm.Rows.TakeLast(3).Select(row => row.Batch).Distinct().Count() == 1, "shared batch identity");
        vm.CancelCommand.Execute(null);
        Check(vm.Rows.All(row => row.Status == "Cancelled"), "cancel queued batches");
        for (var index = 0; index < 3; index++)
            Check(vm.Admit(many with { RequestId = Guid.NewGuid() }).Accepted, "bounded queue admission");
        Check(!vm.Admit(many with { RequestId = Guid.NewGuid() }).Accepted, "queue overflow rejected");
        vm.CancelCommand.Execute(null);
    });

    await OnUiThreadAsync(async () =>
    {
        await using var vm = new MainViewModel(new WorkerClient(Path.Combine(args[0], "missing-worker.exe")));
        vm.Admit(request with { Paths = [fixture, fixture, fixture] });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (vm.IsBusy) await Task.Delay(10, timeout.Token);
        vm.RecordResult(vm.Rows[0], new PublicationResult(fixture, PublicationOutcome.CopyCreated, "Copy created", fixture + ".out").ToFileResult());
        vm.RecordResult(vm.Rows[1], new PublicationResult(fixture, PublicationOutcome.BackupRetained, "Backup retained", fixture + ".out2", fixture + ".original", fixture + ".json").ToFileResult());
        vm.RecordResult(vm.Rows[2], new PublicationResult(fixture, PublicationOutcome.Failed, "Publication failed").ToFileResult());
        Check(vm.Summary.Contains("2 completed (1 warnings)", StringComparison.Ordinal) && vm.Summary.Contains("1 failed", StringComparison.Ordinal),
            "UI summary reconciles committed results, warning, and failure");
        Check(vm.Rows[1].OutputPath.EndsWith(".out2", StringComparison.Ordinal) && vm.Rows[1].RetainedOriginalPath.EndsWith(".original", StringComparison.Ordinal) &&
            vm.Rows[1].RecoveryRecordPath.EndsWith(".json", StringComparison.Ordinal), "UI exposes actual output and recovery paths");
        vm.CancelCommand.Execute(null);
        Check(vm.Rows[0].Result.State == OperationState.Succeeded && vm.Rows[1].Result.State == OperationState.Succeeded,
            "UI cancellation never undoes committed results");
    });

    await OnUiThreadAsync(async () =>
    {
        await using var vm = new MainViewModel(new WorkerClient(Path.Combine(args[0], "missing-worker.exe")));
        var retryPath = Path.Combine(args[0], "retry-source.png");
        File.WriteAllText(retryPath, "disposable retry admission fixture");
        var oldFailure = new FileRow(1, "convert", fixture, new("convert", new()), "webp");
        var laterSuccess = new FileRow(2, "convert", fixture.ToUpperInvariant(), new("convert", new()), "png");
        var retry = new FileRow(3, "optimize", retryPath, new("optimize", new()), "smallest");
        var missing = new FileRow(4, "convert", Path.Combine(args[0], "missing-retry.png"), new("convert", new()), "jpeg");
        foreach (var row in new[] { oldFailure, laterSuccess, retry, missing }) vm.Rows.Add(row);
        foreach (var row in new[] { oldFailure, retry, missing }) vm.RecordResult(row, new(row.Path, OperationState.Failed, "Test failure"));
        vm.RecordResult(laterSuccess, new(laterSuccess.Path, OperationState.Succeeded, "Test success"));
        var message = vm.RetryFailed();
        Check(vm.Rows.Count == 5 && vm.Rows[^1].Action == "smallest" && vm.Rows[^1].Path == retryPath &&
            message.StartsWith("1 file(s) could not be restarted"),
            "retry: latest case-insensitive result suppresses old failure; original preset retained; missing source reported");
        vm.RetryFailed();
        Check(vm.Rows.Count == 5, "retry: double-click cannot duplicate queued retry");
        Check(!vm.DisplayRows.Contains(retry) && vm.DisplayRows.Contains(missing) && vm.Rows.Contains(retry),
            "retry: accepted attempt replaces stale failure in display while missing file and history remain");
        vm.CancelCommand.Execute(null);
        vm.RecordResult(vm.Rows[^1], new PublicationResult(retryPath, PublicationOutcome.CopyCreated, "Created", retryPath + ".out").ToFileResult());
        vm.RetryFailed();
        Check(vm.Rows.Count == 5 && vm.Rows[^1].Result.Publication?.IsCommitted == true,
            "retry: completed retry never reprocesses historical failure or committed output");
    });

    if (args.Length >= 2)
    {
        await WorkerCleanupContracts.RunAsync(args[0], args[1], Check);
        await ImageWorkerContracts.RunAsync(args[0], args[1], Check);
        await PngOptimizationContracts.RunWorkerAsync(args[0], args[1], Check);
        await PngInterruptionContracts.RunAsync(args[0], args[1], Check);
        await DdsWorkerContracts.RunAsync(args[0], args[1], Check);
        await ImageInterruptionContracts.RunAsync(args[0], args[1], Check);
        await ImageInterruptionContracts.RunAsync(args[0], args[1], (passed, name) => Check(passed, "DDS " + name), dds: true);
        await using var worker = new WorkerClient(args[1], Path.Combine(args[0], "worker-scratch"));
        var capabilities = await worker.GetCapabilitiesAsync(CancellationToken.None);
        var expectedCapabilities = (from input in new[] { "png", "jpeg", "webp", "bmp", "tga" }
            from output in new[] { "png", "jpeg", "webp", "bmp", "tga" } where input != output
            select new MediaCapability("convert", input, output)).ToHashSet();
        expectedCapabilities.UnionWith(new[] { "png", "jpeg", "webp", "bmp", "tga" }.Select(input => new MediaCapability("convert", input, "dds")));
        expectedCapabilities.UnionWith([new("convert", "dds", "dds"), new("convert", "dds", "png"), new("optimize", "png", "png")]);
        Check(capabilities.Length == 28 && expectedCapabilities.SetEquals(capabilities), "real private catalog advertises 27 conversion pairs and bounded lossless PNG optimization");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).SequenceEqual(capabilities), "worker reuse retains capabilities");
        var engine = await worker.GetEngineIdentityAsync(CancellationToken.None);
        Check(engine.Package == "Magick.NET-Q16-x64" && engine.Version.Contains("14.17.1", StringComparison.Ordinal) &&
            engine.NativeVersion.Contains("7.1.2", StringComparison.Ordinal), "real worker loads pinned image engine and native payload");
        // A pre-cancelled request leaves capabilities usable; active cancellation is tested above.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await RejectAsync(() => worker.GetCapabilitiesAsync(cancelled.Token), "worker cancellation");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).SequenceEqual(capabilities), "worker usable after pre-cancelled request");
        using var process = Process.GetProcessById(worker.ProcessId!.Value);
        process.Kill(true);
        await process.WaitForExitAsync();
        await RejectAsync(() => worker.GetCapabilitiesAsync(CancellationToken.None), "worker crash reported");
        Check((await worker.GetCapabilitiesAsync(CancellationToken.None)).SequenceEqual(capabilities), "worker restart after crash retains capabilities");
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
